using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using FFXProjectEditor.Resources;
using Xunit;

namespace FFXProjectEditor.Tests.Resources;

/// <summary>
/// v2.218.1.0 (IFRT-2) — Substitui o smoke manual dos ~96 módulos:
/// varre TODOS os .axaml e valida que cada referência {x:Static res:Strings.X}
/// (1) existe como propriedade no Strings.cs, (2) existe como chave no resx
/// neutro (Strings.resx) — sem isso o ResourceManager devolve o nome da chave
/// na tela. Também valida que toda propriedade do Strings.cs tem chave neutra.
/// </summary>
public class StringsIntegrityTests
{
    private static readonly ResourceManager _rm =
        new("FFXProjectEditor.Resources.Strings", typeof(Strings).Assembly);

    private static readonly Regex XStaticRef =
        new(@"\{x:Static\s+res:Strings\.(\w+)\}", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex PropRef =
        new(@"public static string (\w+) =>", RegexOptions.Compiled);

    /// <summary>Sobe do BaseDirectory do teste até achar a raiz do repo (com Modules/).</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "FFXProjectEditor", "Modules")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Raiz do repo não encontrada a partir de " + AppContext.BaseDirectory);
    }

    private static string[] AllAxamlFiles(string repoRoot)
    {
        var modules = Path.Combine(repoRoot, "FFXProjectEditor", "Modules");
        return Directory.EnumerateFiles(modules, "*.axaml", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).Contains("MonsterAiEditor2")) // regra dura
            .ToArray();
    }

    private static bool NeutralKeyExists(string key)
    {
        // InvariantCulture => neutro (Strings.resx). CreateResourceSet com try-lookup.
        var set = _rm.GetResourceSet(System.Globalization.CultureInfo.InvariantCulture, true, true);
        return set != null && set.GetObject(key) != null;
    }

    [Fact]
    public void EveryXamlStaticReference_ResolvesToPropertyAndNeutralKey()
    {
        string repoRoot = FindRepoRoot();
        var files = AllAxamlFiles(repoRoot);
        Assert.True(files.Length > 80, $"esperava 80+ .axaml, achou {files.Length}");

        var props = typeof(Strings)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        int refs = 0;
        var missingProp = new System.Collections.Generic.List<string>();
        var missingKey = new System.Collections.Generic.List<string>();
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            string text = File.ReadAllText(file);
            foreach (Match m in XStaticRef.Matches(text))
            {
                string key = m.Groups[1].Value;
                refs++;
                if (seen.Add(key) == false)
                    continue;
                if (!props.Contains(key))
                    missingProp.Add($"{key}  ({Path.GetFileName(file)})");
                if (!NeutralKeyExists(key))
                    missingKey.Add($"{key}  ({Path.GetFileName(file)})");
            }
        }

        Assert.True(refs > 2000, $"esperava 2000+ referências x:Static, achou {refs}");
        Assert.True(missingProp.Count == 0,
            $"referências sem propriedade no Strings.cs ({missingProp.Count}):\n" + string.Join("\n", missingProp.Take(20)));
        Assert.True(missingKey.Count == 0,
            $"referências sem chave no Strings.resx ({missingKey.Count}):\n" + string.Join("\n", missingKey.Take(20)));
    }

    [Fact]
    public void EveryStringProperty_HasNeutralKey()
    {
        var props = typeof(Strings)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Select(p => p.Name)
            .Where(n => n != "CurrentLanguage" && n != "ConfigDirectory" && n != "ConfigPath")
            .ToArray();

        var missing = props.Where(name => !NeutralKeyExists(name)).ToList();
        Assert.True(missing.Count == 0,
            $"propriedades sem chave no Strings.resx ({missing.Count}):\n" + string.Join("\n", missing.Take(20)));
        // 251 originais + 25 (Onda 1) + 51 (Onda 2) + 127 (3a) + 1739 (3b) - 205 nao-strings = ~1988
        Assert.True(props.Length >= 1900, $"esperava 1900+ propriedades, achou {props.Length}");
    }

    [Fact]
    public void NoXamlHasRawHardcodedUiLiteral_StillUnmigrated()
    {
        // Regressão: qualquer literal de UI cru (>=3 chars, sem binding/resource) é dívida nova.
        // EXCEÇÃO legítima: lixo técnico não-traduzível (paths, hex, nomes de arquivo, números,
        // símbolos) — mesmo critério do classificador da Onda 3b (work/_i18n_onda3b_classify.py).
        string repoRoot = FindRepoRoot();
        var files = AllAxamlFiles(repoRoot);
        var raw = new System.Collections.Generic.List<string>();
        var attrRe = new Regex("(?:Text|Content|Header|Watermark|ToolTip\\.Tip)=\"([^\"{][^\"]*)\"",
            RegexOptions.Compiled);

        bool IsTechnicalJunk(string v)
        {
            var s = v.Trim();
            if (s.Length < 3) return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(s, @"^0x[0-9a-fA-F]+$")) return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(s, @"^[\d\s.,%+\-/\u00d7\u00f7]+$") &&
                System.Text.RegularExpressions.Regex.IsMatch(s, @"\d")) return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(s,
                    @"(%\w+%|[A-Za-z]:[\\/]|[\\/][\w.\-]+\.)(bin|json|txt|exe|dll|xml|axaml|ini|dat|log|csv|config|flag|sidecar)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(s, @"^[\w.\-\\/]+\.(exe|dll|bin|json|txt|log|ini|xml)$")) return true;
            return false;
        }

        foreach (var file in files)
        {
            string text = File.ReadAllText(file);
            foreach (Match m in attrRe.Matches(text))
            {
                string v = m.Groups[1].Value.Trim();
                if (v.Length >= 3 && !v.StartsWith("{") && !IsTechnicalJunk(v))
                    raw.Add($"{v}  ({Path.GetFileName(file)})");
            }
        }

        Assert.True(raw.Count == 0,
            $"literais de UI crus encontrados ({raw.Count}):\n" + string.Join("\n", raw.Take(20)));
    }
}
