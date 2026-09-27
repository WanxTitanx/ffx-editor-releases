using System;
using System.ComponentModel;
using System.IO;
using FFXProjectEditor.Modules.AiAssistant;
using Xunit;

namespace FFXProjectEditor.Tests.Modules.AiAssistant
{
    /// <summary>
    /// Jarvis-UI 2026-09-15: regressões do rework do painel — CanSend notificando (o botão Send
    /// nunca habilitava antes), IsProposalMode/ChatMode como inversos, persistência de config
    /// (provider/endpoint/model/mode — NUNCA a key) via SettingsPathOverride.
    /// </summary>
    public sealed class AiAssistantDataModelTests : IDisposable
    {
        readonly string tempDir;
        readonly string settingsFile;

        public AiAssistantDataModelTests()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "aia-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            settingsFile = Path.Combine(tempDir, "ai-assistant.json");
            AiAssistant_DataModel.SettingsPathOverride = settingsFile;
        }

        public void Dispose()
        {
            AiAssistant_DataModel.SettingsPathOverride = null;
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }

        [Fact]
        public void CanSend_RaisesPropertyChanged_WhenSessionConnects()
        {
            var dm = new AiAssistant_DataModel();
            string? raised = null;
            ((INotifyPropertyChanged)dm).PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AiAssistant_DataModel.CanSend)) raised = e.PropertyName;
            };

            Assert.False(dm.CanSend);
            dm.ConnectSession("sk-test-key-0001");

            Assert.True(dm.IsSessionActive);
            Assert.True(dm.CanSend);
            Assert.Equal(nameof(AiAssistant_DataModel.CanSend), raised);
        }

        [Fact]
        public void CanSend_False_AfterClearSession()
        {
            var dm = new AiAssistant_DataModel();
            dm.ConnectSession("sk-test-key-0002");
            Assert.True(dm.CanSend);

            dm.ClearSession();

            Assert.False(dm.IsSessionActive);
            Assert.False(dm.CanSend);
        }

        [Fact]
        public void ConnectSession_EmptyKey_KeepsInactive()
        {
            var dm = new AiAssistant_DataModel();
            dm.ConnectSession("   ");

            Assert.False(dm.IsSessionActive);
            Assert.False(dm.CanSend);
        }

        [Fact]
        public void ModeProperties_AreInverse()
        {
            var dm = new AiAssistant_DataModel();

            Assert.True(dm.ChatMode);          // default: Chat
            Assert.False(dm.IsProposalMode);

            dm.IsProposalMode = true;
            Assert.False(dm.ChatMode);
            Assert.True(dm.IsProposalMode);

            dm.ChatMode = true;
            Assert.False(dm.IsProposalMode);
        }

        [Fact]
        public void ModeIndex_ThreeStates_AreExclusive()
        {
            var dm = new AiAssistant_DataModel();

            Assert.True(dm.IsChatMode);
            Assert.False(dm.IsProposalMode);
            Assert.False(dm.IsAgentMode);

            dm.ModeIndex = 2;
            Assert.False(dm.IsChatMode);
            Assert.False(dm.IsProposalMode);
            Assert.True(dm.IsAgentMode);
            Assert.False(dm.ChatMode); // legacy: agente não é "chat"

            dm.ModeIndex = 1;
            Assert.True(dm.IsProposalMode);

            dm.ModeIndex = 0;
            Assert.True(dm.IsChatMode);
        }

        [Fact]
        public void Mode_Persists_Agent_And_Reloads()
        {
            var dm = new AiAssistant_DataModel { ModeIndex = 2 };
            Assert.True(File.Exists(settingsFile));

            var dm2 = new AiAssistant_DataModel();
            Assert.True(dm2.IsAgentMode);
        }

        [Fact]
        public void LegacyChatModeFalse_Loads_AsProposal()
        {
            File.WriteAllText(settingsFile,
                "{\"Provider\":\"x\",\"Endpoint\":\"http://127.0.0.1\",\"ModelId\":\"m\",\"ChatMode\":false}");
            var dm = new AiAssistant_DataModel();
            Assert.True(dm.IsProposalMode); // arquivo antigo sem "Mode" → proposal via ChatMode
        }

        [Fact]
        public void RejectProposal_WithoutPending_IsNoOp()
        {
            var dm = new AiAssistant_DataModel();
            dm.RejectProposal(); // não deve lançar nem alterar estado
            Assert.False(dm.HasPendingProposal);
        }

        [Fact]
        public void ApproveProposal_WithoutPending_IsNoOp()
        {
            var dm = new AiAssistant_DataModel();
            var t = dm.ApproveProposalAsync(); // sem sessão/proposta → retorna cedo
            Assert.True(t.IsCompleted);
            Assert.False(dm.HasPendingProposal);
        }

        [Fact]
        public void Settings_Persist_And_Reload()
        {
            var dm = new AiAssistant_DataModel();
            dm.Provider = "custom-provider";
            dm.Endpoint = "http://192.168.1.10:8000/v1/chat/completions";
            dm.ModelId = "my-model-7b";
            dm.ChatMode = false;

            Assert.True(File.Exists(settingsFile), "settings file should be written on change");

            var dm2 = new AiAssistant_DataModel();
            Assert.Equal("custom-provider", dm2.Provider);
            Assert.Equal("http://192.168.1.10:8000/v1/chat/completions", dm2.Endpoint);
            Assert.Equal("my-model-7b", dm2.ModelId);
            Assert.False(dm2.ChatMode);
        }

        [Fact]
        public void Settings_NeverPersist_ApiKey()
        {
            var dm = new AiAssistant_DataModel();
            dm.ApiKeyText = "sk-secret-MUST-NOT-PERSIST";
            dm.Provider = "trigger-save";

            string json = File.Exists(settingsFile) ? File.ReadAllText(settingsFile) : string.Empty;
            Assert.DoesNotContain("sk-secret-MUST-NOT-PERSIST", json);
            Assert.DoesNotContain("ApiKey", json);
        }

        [Fact]
        public void Corrupt_SettingsFile_FallsBackToDefaults()
        {
            File.WriteAllText(settingsFile, "{ not json !!!");
            var dm = new AiAssistant_DataModel();
            Assert.Equal("openai-compatible", dm.Provider);
            Assert.True(dm.ChatMode);
        }

        [Fact]
        public void SelectingPreset_AppliesEndpointAndModel()
        {
            var dm = new AiAssistant_DataModel();

            dm.SelectedProviderIndex = 0; // OpenAI
            Assert.Equal("openai", dm.Provider);
            Assert.Equal("https://api.openai.com/v1/chat/completions", dm.Endpoint);
            Assert.Equal("gpt-4o-mini", dm.ModelId);

            dm.SelectedProviderIndex = 3; // DeepSeek
            Assert.Equal("deepseek", dm.Provider);
            Assert.Equal("https://api.deepseek.com/v1/chat/completions", dm.Endpoint);
            Assert.Equal("deepseek-chat", dm.ModelId);
        }

        [Fact]
        public void SelectingCustom_PreservesUserFields()
        {
            var dm = new AiAssistant_DataModel();
            dm.SelectedProviderIndex = 0;              // OpenAI aplica os defaults
            dm.Endpoint = "http://192.168.0.50:9000/v1/chat/completions";
            dm.ModelId = "meu-modelo-fine-tuned";

            dm.SelectedProviderIndex = dm.ProviderPresets.Count - 1; // "custom"

            Assert.Equal("openai-compatible", dm.Provider);
            Assert.Equal("http://192.168.0.50:9000/v1/chat/completions", dm.Endpoint);
            Assert.Equal("meu-modelo-fine-tuned", dm.ModelId);
        }

        [Fact]
        public void LoadedProvider_MapsToMatchingDropdownIndex()
        {
            var dm = new AiAssistant_DataModel { Provider = "groq" };
            var dm2 = new AiAssistant_DataModel(); // relê o settings persistido

            Assert.Equal(5, dm2.SelectedProviderIndex); // groq = índice 5 na lista (verboo veio depois)
        }

        [Fact]
        public void UnknownProvider_MapsToCustomIndex()
        {
            var dm = new AiAssistant_DataModel { Provider = "provedor-estranho" };
            var dm2 = new AiAssistant_DataModel();
            Assert.Equal(dm2.ProviderPresets.Count - 1, dm2.SelectedProviderIndex);
        }

        [Fact]
        public void RemoteHost_ParsesHostFromEndpoint()
        {
            var dm = new AiAssistant_DataModel { Endpoint = "https://api.openai.com/v1/chat/completions" };
            Assert.Equal("api.openai.com", dm.RemoteHost);
            Assert.Contains("api.openai.com", dm.RemoteHostWarning);
        }
    }
}
