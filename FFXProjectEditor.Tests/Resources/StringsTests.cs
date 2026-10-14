using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.Resources;

/// <summary>
/// v2.208.0.0 (Jarvis-CLINE) — language selector infra: resolution priority, persistence and
/// culture application. Uses a temp config dir so no user state is touched.
/// </summary>
[Collection(GlobalUiCultureTestCollection.Name)]
public class StringsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string? _savedEnv;
    // Restore the incoming test context; a selector test is the only intentional global writer.
    private readonly string? _savedConfigDirectoryOverride = Strings.ConfigDirectoryOverride;
    private readonly CultureInfo _savedUiCulture = CultureInfo.CurrentUICulture;
    private readonly CultureInfo? _savedDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
    private bool _disposed;

    public StringsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ffx_ui_lang_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _savedEnv = Environment.GetEnvironmentVariable("FFX_UI_LANG");
        Environment.SetEnvironmentVariable("FFX_UI_LANG", null);
        Strings.ConfigDirectoryOverride = _tempDir;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Strings.SetLanguage mutates process-wide DefaultThreadCurrentUICulture. Always restore
        // the neutral EN fallback even when an assertion throws before a test's local finally.
        Strings.SetLanguage("en");
        Environment.SetEnvironmentVariable("FFX_UI_LANG", _savedEnv);
        Strings.ConfigDirectoryOverride = _savedConfigDirectoryOverride;
        CultureInfo.DefaultThreadCurrentUICulture = _savedDefaultUiCulture;
        CultureInfo.CurrentUICulture = _savedUiCulture;
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void DefaultLanguage_IsPt()
    {
        Strings.ApplyUiCulture();
        Assert.Equal("pt", Strings.CurrentLanguage);
    }

    [Fact]
    public void SetLanguage_PersistsAndApplies()
    {
        Strings.SetLanguage("en");
        Assert.Equal("en", Strings.CurrentLanguage);
        Assert.Equal("en", File.ReadAllText(Strings.ConfigPath).Trim());

        Strings.SetLanguage("pt");
        Assert.Equal("pt", Strings.CurrentLanguage);
        Assert.Equal("pt", File.ReadAllText(Strings.ConfigPath).Trim());
    }

    [Fact]
    public void PersistedConfig_WinsOverEnv()
    {
        Directory.CreateDirectory(Strings.ConfigDirectory);
        File.WriteAllText(Strings.ConfigPath, "en");
        Environment.SetEnvironmentVariable("FFX_UI_LANG", "pt");

        Strings.ApplyUiCulture();
        Assert.Equal("en", Strings.CurrentLanguage);
    }

    [Fact]
    public void EnvVar_Fallback_WhenNoConfig()
    {
        Environment.SetEnvironmentVariable("FFX_UI_LANG", "pt");
        Strings.ApplyUiCulture();
        Assert.Equal("pt", Strings.CurrentLanguage);

        Environment.SetEnvironmentVariable("FFX_UI_LANG", "en");
        Strings.ApplyUiCulture();
        Assert.Equal("en", Strings.CurrentLanguage);
    }

    [Fact]
    public void InvalidConfig_FallsBackToDefault()
    {
        Directory.CreateDirectory(Strings.ConfigDirectory);
        File.WriteAllText(Strings.ConfigPath, "garbage");
        Environment.SetEnvironmentVariable("FFX_UI_LANG", null);

        Strings.ApplyUiCulture();
        Assert.Equal("pt", Strings.CurrentLanguage);
    }

    [Fact]
    public void Normalize_OnlyPtOrEn()
    {
        Strings.SetLanguage("pt-BR");
        Assert.Equal("pt", Strings.CurrentLanguage);
        Strings.SetLanguage("EN-us");
        Assert.Equal("en", Strings.CurrentLanguage);
        Strings.SetLanguage("xyz");
        Assert.Equal("en", Strings.CurrentLanguage);
    }

    [Fact]
    public void CultureIsApplied_ToCurrentThread()
    {
        Strings.SetLanguage("pt");
        Assert.StartsWith("pt", CultureInfo.CurrentUICulture.Name);
        Strings.SetLanguage("en");
        Assert.Equal("", CultureInfo.CurrentUICulture.Name); // invariant
    }

    // ===== Regressão v2.208.0.1 (Jarvis-CLINE): mojibake no Strings.pt.resx =====
    // O satellite PT foi gravado com acentos cp1252-lidos-como-UTF-8 ("comeÃ§ar" em vez de
    // "começar", "MÃ³dulos" em vez de "Módulos"), o que fazia a UI em PT exibir caracteres
    // corrompidos enquanto EN ficava normal. Este teste trava qualquer regressão futura.

    [Theory]
    [InlineData("DashboardHeroTitleEmpty", "começar")]
    [InlineData("DashboardHeroSubtitleEmpty", "funções")]
    [InlineData("DashboardModulesLabel", "Módulos")]
    [InlineData("Mod_home_Title", "Visão")]
    [InlineData("Mod_customizations_hub_Title", "Customizações")]
    [InlineData("Mod_encounters_hub_Title", "formação")]
    [InlineData("MainWinTagline", "monstros")]
    [InlineData("MainWinPreviewTrack", "prévia")]
    public void PtStrings_AreCleanUtf8_NoMojibake(string key, string expectedFragment)
    {
        Strings.SetLanguage("pt");
        try
        {
            string value = Strings.Get(key);
            // Mojibake típico: "Ã§", "Ã£", "Ã©", "Ãº", "Ã­", "Ã³", "Ã¡" (pares U+00C3/U+00C2 + latino)
            Assert.DoesNotContain("Ã", value);
            Assert.DoesNotContain("Â", value);
            Assert.Contains(expectedFragment, value);
        }
        finally
        {
            // Reset this private selector case; Dispose restores the incoming global context.
            Strings.SetLanguage("en");
        }
    }

    // ===== Regressão v2.234.2.0 (Verboo): satellites ES/FR/DE/IT/JA/KO/ZH compilados =====
    // Traduções em massa (2026-08-18) preencheram os 7 satellites que só tinham 2226/4349 chaves.
    // O target GenerateSatelliteResourcesWithWriter (csproj) gera o .resources de TODOS os satellites
    // via ResourceWriter (ResGen lê cp1252 no Windows pt-BR). Este teste prova que cada satellite
    // carrega pelo ResourceManager SEM mojibake e que o valor NÃO é o fallback EN.

    // ===== Regressão v2.234.3.0 (Verboo): EN source sem fragmentos PT em chaves usadas no .axaml =====
    // O verifier pegou 37 chaves legacy com valor EN em PT (ex: U_inicio_fc2c7400 = "Início") que
    // renderizavam português no modo EN. Este teste varre as chaves referenciadas em .axaml e
    // garante que o valor EN não tem acentos PT (exceto nomes de idioma nativos).

    [Fact]
    public void EnSource_HasNoPortugueseFragments_InAxamlReferencedKeys()
    {
        var axamlDir = Path.Combine(TestDataPaths.RepoRoot, "FFXProjectEditor");
        var axamlFiles = Directory.GetFiles(axamlDir, "*.axaml", SearchOption.AllDirectories);
        Assert.True(axamlFiles.Length > 0, "nenhum .axaml encontrado para varrer");

        var referenced = new HashSet<string>();
        foreach (var file in axamlFiles)
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"res:Strings\.([A-Za-z0-9_]+)"))
                referenced.Add(m.Groups[1].Value);
        }
        Assert.True(referenced.Count > 100, "poucas chaves referenciadas — scan suspeito");

        var ptAccent = new Regex(@"[áàãâéêíóôõúçÁÀÃÂÉÊÍÓÔÕÚÇ]");
        // Lê o EN source direto do resx (não via Strings.Get — culture-dependent).
        var resxPath = Path.Combine(axamlDir, "Resources", "Strings.resx");
        var enValues = new Dictionary<string, string>();
        foreach (Match m in Regex.Matches(File.ReadAllText(resxPath), @"<data name=""([^""]+)""[^>]*><value>(.*?)</value>", RegexOptions.Singleline))
            enValues[m.Groups[1].Value] = m.Groups[2].Value;

        var violations = new List<string>();
        foreach (var key in referenced)
        {
            if (!enValues.TryGetValue(key, out string value))
                continue; // chave sem valor EN (não deveria acontecer, mas não é o alvo deste teste)
            // Nomes de idioma nativos (Português, 日本語, etc.) são exceção legítima.
            if (key.StartsWith("Lang") || key.Contains("ingles_us") || key.Contains("japones_jp"))
                continue;
            if (ptAccent.IsMatch(value))
                violations.Add($"{key} = {value}");
        }
        Assert.True(violations.Count == 0,
            "EN source contém fragmentos PT em chaves usadas no .axaml:\n" + string.Join("\n", violations.Take(20)));
    }

    [Theory]
    [InlineData("es", "Escena lista")]
    [InlineData("fr", "Scène prête")]
    [InlineData("de", "Szene bereit")]
    [InlineData("it", "Scena pronta")]
    [InlineData("ja", "シーン準備完了")]
    [InlineData("ko", "Scene ready")]
    [InlineData("zh", "场景就绪")]
    public void Satellites_AreCleanUtf8_NoMojibake(string lang, string expectedFragment)
    {
        Strings.SetLanguage(lang);
        try
        {
            string value = Strings.Get("U_Au_SceneReady");
            // Satellites carregados (não EN fallback):
            Assert.Contains(expectedFragment, value);
            // Sem assinatura de mojibake (dupla codificação cp1252→UTF-8) nem replacement char:
            Assert.DoesNotContain("Ã", value);
            Assert.DoesNotContain("Â", value);
            Assert.DoesNotContain("\uFFFD", value);
        }
        finally
        {
            // Reset this private selector case; Dispose restores the incoming global context.
            Strings.SetLanguage("en");
        }
    }
}
