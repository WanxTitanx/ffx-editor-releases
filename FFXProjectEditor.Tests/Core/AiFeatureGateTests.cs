using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.Core;
using FFXProjectEditor.Modules.Main;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    /// <summary>
    /// AI Assistant feature gate (Jarvis-UI 2026-09-15): the module ships disabled
    /// by default and only enters the public navigation surface on explicit opt-in.
    /// Covers persistence, the Changed hook and the ModuleRegistry.Public filter.
    /// </summary>
    [Collection("AiFeatureGate")]
    public class AiFeatureGateTests : IDisposable
    {
        readonly string _file = Path.Combine(Path.GetTempPath(), "flags-" + Guid.NewGuid().ToString("N") + ".json");

        public AiFeatureGateTests()
        {
            AiFeatureGate.SettingsPathOverride = _file;
            AiFeatureGate.ResetForTests();
        }

        public void Dispose()
        {
            AiFeatureGate.SettingsPathOverride = null;
            AiFeatureGate.ResetForTests();
            try { File.Delete(_file); } catch { }
        }

        [Fact]
        public void Default_NoFile_IsDisabled()
        {
            Assert.False(AiFeatureGate.Enabled);
        }

        [Fact]
        public void Enable_Persists_AndFiresChanged()
        {
            int fired = 0;
            AiFeatureGate.Changed += () => fired++;

            AiFeatureGate.Enabled = true;

            Assert.True(AiFeatureGate.Enabled);
            Assert.Equal(1, fired);
            Assert.True(File.Exists(_file));
            Assert.Contains("true", File.ReadAllText(_file));

            // Reload from disk (fresh cache) keeps the flag.
            AiFeatureGate.ResetForTests();
            Assert.True(AiFeatureGate.Enabled);
        }

        [Fact]
        public void DisableAfterEnable_PersistsOff()
        {
            AiFeatureGate.Enabled = true;
            AiFeatureGate.Enabled = false;
            AiFeatureGate.ResetForTests();
            Assert.False(AiFeatureGate.Enabled);
        }

        [Fact]
        public void SameValue_DoesNotRefire()
        {
            int fired = 0;
            AiFeatureGate.Changed += () => fired++;
            AiFeatureGate.Enabled = false; // already off
            Assert.Equal(0, fired);
        }

        [Fact]
        public void CorruptFile_StaysDisabled()
        {
            File.WriteAllText(_file, "{not json");
            AiFeatureGate.ResetForTests();
            Assert.False(AiFeatureGate.Enabled);
        }

        // ── registry surface ────────────────────────────────────────────────

        [Fact]
        public void Public_RegistryHidesAssistant_WhenOff()
        {
            AiFeatureGate.Enabled = false;
            Assert.DoesNotContain(ModuleRegistry.Public, e => e.Id == "ai-assistant");
            Assert.Contains(ModuleRegistry.All, e => e.Id == "ai-assistant"); // still in the full catalog
        }

        [Fact]
        public void Public_RegistryShowsAssistant_WhenOn()
        {
            AiFeatureGate.Enabled = true;
            Assert.Contains(ModuleRegistry.Public, e => e.Id == "ai-assistant");
        }
    }
}
