using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

return Run(args);

static int Run(string[] args)
{
    var options = Options.Parse(args);
    if (options.ShowHelp)
    {
        PrintHelp();
        return 0;
    }

    try
    {
        var outputRoot = Path.GetFullPath(options.OutputRoot);
        Directory.CreateDirectory(outputRoot);

        var assets = ScanAssets(options).ToArray();
        var ida = LoadIdaExport(options.IdaExportRoot);
        var cards = BuildCards(assets, ida, options).ToArray();
        var summary = BuildSummary(options, assets, ida, cards);

        WriteInventory(Path.Combine(outputRoot, "asset-linker-inventory.csv"), assets);
        WriteFileStatus(Path.Combine(outputRoot, "asset-linker-file-status.csv"), assets, cards);
        foreach (var sourceGroup in assets.GroupBy(static x => x.Source, StringComparer.OrdinalIgnoreCase))
        {
            WriteFileStatus(
                Path.Combine(outputRoot, $"{sourceGroup.Key}-file-status.csv"),
                sourceGroup.OrderBy(static x => x.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(),
                cards);
        }

        WriteJson(Path.Combine(outputRoot, "asset-linker-summary.json"), summary);
        WriteJson(Path.Combine(outputRoot, "asset-linker-cards.json"), cards);
        WriteFunctionQueue(Path.Combine(outputRoot, "asset-linker-functions.csv"), cards);
        WriteMarkdown(Path.Combine(outputRoot, "asset-linker-report.md"), summary, cards, options);
        WriteDomainReport(Path.Combine(outputRoot, "asset-linker-domain-report.md"), summary, cards);

        Console.WriteLine($"Assets scanned: {assets.Length.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"IDA strings: {ida.Strings.Count.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"IDA xrefs: {ida.Xrefs.Count.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"Cards: {cards.Length.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"Output: {outputRoot}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"AssetLinkerLab failed: {ex.Message}");
        return 1;
    }
}

static IReadOnlyList<AssetFile> ScanAssets(Options options)
{
    var result = new List<AssetFile>();
    foreach (var root in options.Roots)
    {
        if (!Directory.Exists(root.Path))
        {
            Console.Error.WriteLine($"Skipping missing {root.Source}: {root.Path}");
            continue;
        }

        foreach (var path in Directory.EnumerateFiles(root.Path, "*", SearchOption.AllDirectories))
        {
            var info = new FileInfo(path);
            var relative = NormalizePath(Path.GetRelativePath(root.Path, path));
            var fileName = Path.GetFileName(path);
            var stem = GetStem(fileName);
            var kind = ClassifyKind(fileName);
            var family = ClassifyFamily(root.Source, relative, kind);
            var tokens = BuildAssetTokens(relative, fileName, stem, kind, family);
            result.Add(new AssetFile(
                Source: root.Source,
                Root: root.PortableRoot,
                RelativePath: relative,
                FileName: fileName,
                Stem: stem,
                Kind: kind,
                Family: family,
                Size: info.Length,
                Tokens: tokens));
        }
    }

    return result
        .OrderBy(static x => x.Source, StringComparer.OrdinalIgnoreCase)
        .ThenBy(static x => x.RelativePath, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static IdaExport LoadIdaExport(string idaExportRoot)
{
    var root = Path.GetFullPath(idaExportRoot);
    if (!Directory.Exists(root))
        throw new DirectoryNotFoundException($"IDA export root not found: {root}");

    var strings = ReadJsonl(Path.Combine(root, "strings.jsonl"), element =>
        new IdaString(
            Ea: ReadString(element, "ea"),
            Value: ReadString(element, "value")));

    var xrefs = ReadJsonl(Path.Combine(root, "xrefs.jsonl"), element =>
        new IdaXref(
            From: ReadString(element, "from"),
            To: ReadString(element, "to"),
            Function: ReadString(element, "function"),
            Type: ReadInt(element, "type")));

    var functions = ReadJsonl(Path.Combine(root, "functions.jsonl"), element =>
        new IdaFunction(
            Ea: ReadString(element, "ea"),
            Name: ReadString(element, "name"),
            PseudocodeFile: ReadString(element, "pseudocode_file"),
            DisasmFile: ReadString(element, "disasm_file"),
            Size: ReadInt(element, "size")));

    var xrefsByString = xrefs
        .GroupBy(static x => x.To, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(static x => x.Key, static x => x.ToArray(), StringComparer.OrdinalIgnoreCase);

    var functionsByName = functions
        .GroupBy(static x => x.Name, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(static x => x.Key, static x => x.First(), StringComparer.OrdinalIgnoreCase);

    return new IdaExport(root, strings, xrefs, functions, xrefsByString, functionsByName);
}

static IEnumerable<LinkCard> BuildCards(IReadOnlyList<AssetFile> assets, IdaExport ida, Options options)
{
    var assetTokenIndex = BuildTokenIndex(assets);
    var familyIndex = assets
        .GroupBy(static x => $"{x.Source}|{x.Family}|{x.Kind}", StringComparer.OrdinalIgnoreCase)
        .ToDictionary(static x => x.Key, static x => x.ToArray(), StringComparer.OrdinalIgnoreCase);

    var cards = new List<LinkCard>();
    foreach (var idaString in ida.Strings)
    {
        var valueNorm = NormalizeText(idaString.Value);
        var stringTokens = TokenizeString(idaString.Value).ToArray();
        var relevant = IsRelevantString(valueNorm, stringTokens, assetTokenIndex);
        if (!relevant)
            continue;

        var matches = new Dictionary<int, AssetMatch>();
        foreach (var token in stringTokens)
        {
            if (IsWeakToken(token))
                continue;

            if (!assetTokenIndex.TryGetValue(token, out var ids))
                continue;

            if (ids.Count > options.MaxAssetsPerToken)
                continue;

            foreach (var id in ids)
            {
                var asset = assets[id];
                var matchType = DetermineMatchType(idaString.Value, token, asset);
                var score = ScoreMatch(matchType, idaString.Value, asset);
                AddOrImprove(matches, id, new AssetMatch(asset.Source, asset.RelativePath, asset.Kind, asset.Family, matchType, token, score));
            }
        }

        foreach (var familyMatch in MatchFormatFamilies(valueNorm, familyIndex))
        {
            foreach (var sample in familyMatch.Assets.Take(options.MaxAssetsPerCard))
            {
                var id = Array.IndexOf(assets as AssetFile[] ?? assets.ToArray(), sample);
                if (id < 0)
                    continue;

                AddOrImprove(matches, id, new AssetMatch(
                    sample.Source,
                    sample.RelativePath,
                    sample.Kind,
                    sample.Family,
                    familyMatch.MatchType,
                    familyMatch.Token,
                    familyMatch.Score));
            }
        }

        if (matches.Count == 0)
            continue;

        ida.XrefsByString.TryGetValue(idaString.Ea, out var xrefs);
        xrefs ??= Array.Empty<IdaXref>();

        var functions = xrefs
            .Select(x => x.Function)
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var functionDetails = functions
            .Select(name => ida.FunctionsByName.TryGetValue(name, out var fn)
                ? new FunctionHit(fn.Ea, fn.Name, fn.Size, fn.PseudocodeFile, fn.DisasmFile)
                : new FunctionHit("", name, 0, "", ""))
            .ToArray();

        var topMatches = matches.Values
            .OrderByDescending(static x => x.Score)
            .ThenBy(static x => x.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static x => x.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(options.MaxAssetsPerCard)
            .ToArray();

        var bestScore = topMatches.Length == 0 ? 0 : topMatches.Max(static x => x.Score);
        var evidence = DetermineEvidence(idaString.Value, functionDetails, topMatches, bestScore);
        var renameAction = SuggestRenameAction(functionDetails, topMatches, evidence);
        cards.Add(new LinkCard(
            StringEa: idaString.Ea,
            StringValue: idaString.Value,
            Evidence: evidence,
            Score: bestScore + Math.Min(functionDetails.Length, 5),
            XrefCount: xrefs.Length,
            Functions: functionDetails,
            Assets: topMatches,
            RenameAction: renameAction));
    }

    return cards
        .OrderByDescending(static x => x.Score)
        .ThenByDescending(static x => x.XrefCount)
        .ThenBy(static x => x.StringValue, StringComparer.OrdinalIgnoreCase);
}

static Dictionary<string, List<int>> BuildTokenIndex(IReadOnlyList<AssetFile> assets)
{
    var index = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < assets.Count; i++)
    {
        foreach (var token in assets[i].Tokens)
        {
            if (IsWeakToken(token))
                continue;

            if (!index.TryGetValue(token, out var list))
            {
                list = new List<int>();
                index[token] = list;
            }

            list.Add(i);
        }
    }

    return index;
}

static IEnumerable<FamilyMatch> MatchFormatFamilies(string valueNorm, Dictionary<string, AssetFile[]> familyIndex)
{
    if (valueNorm.Contains("battle/kernel", StringComparison.OrdinalIgnoreCase) && valueNorm.Contains(".bin", StringComparison.OrdinalIgnoreCase))
    {
        foreach (var match in FamiliesContaining(familyIndex, "battle/kernel", ".bin", "battle/kernel/*.bin", "format-family", 8))
            yield return match;
    }

    if (valueNorm.Contains("battle/btl", StringComparison.OrdinalIgnoreCase) && valueNorm.Contains(".bin", StringComparison.OrdinalIgnoreCase))
    {
        foreach (var match in FamiliesContaining(familyIndex, "battle/btl", ".bin", "battle/btl/*.bin", "format-family", 8))
            yield return match;
    }

    if (valueNorm.Contains("battle/btl", StringComparison.OrdinalIgnoreCase) && valueNorm.Contains(".ftc", StringComparison.OrdinalIgnoreCase))
    {
        foreach (var match in FamiliesContaining(familyIndex, "battle/btl", ".ftc", "battle/btl/*.ftc", "format-family", 8))
            yield return match;
    }

    if (valueNorm.Contains("resident", StringComparison.OrdinalIgnoreCase) && valueNorm.Contains(".mgrp", StringComparison.OrdinalIgnoreCase))
    {
        foreach (var match in FamiliesContaining(familyIndex, "/mot", ".mgrp", "resident*.mgrp", "format-family", 8))
            yield return match;
    }

    if (valueNorm.Contains("ps3data", StringComparison.OrdinalIgnoreCase) && valueNorm.Contains("ahwin32", StringComparison.OrdinalIgnoreCase))
    {
        foreach (var match in FamiliesContaining(familyIndex, "chr", ".ahwin32", "ps3data/chr/*/ahwin32", "format-family", 7))
            yield return match;
    }
}

static IEnumerable<FamilyMatch> FamiliesContaining(Dictionary<string, AssetFile[]> familyIndex, string familyNeedle, string kind, string token, string matchType, int score)
{
    foreach (var (_, assets) in familyIndex)
    {
        var first = assets.FirstOrDefault();
        if (first is null)
            continue;

        var family = NormalizeText(first.Family);
        if (!family.Contains(familyNeedle.Trim('/'), StringComparison.OrdinalIgnoreCase))
            continue;

        if (!first.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase))
            continue;

        yield return new FamilyMatch(token, matchType, score, assets);
    }
}

static void AddOrImprove(Dictionary<int, AssetMatch> matches, int id, AssetMatch candidate)
{
    if (!matches.TryGetValue(id, out var existing) || candidate.Score > existing.Score)
        matches[id] = candidate;
}

static string DetermineMatchType(string stringValue, string token, AssetFile asset)
{
    var lower = NormalizeText(stringValue);
    if (ContainsPathSegment(lower, NormalizeText(asset.FileName)))
        return "exact-file";

    if (!string.IsNullOrWhiteSpace(asset.Stem) && ContainsPathSegment(lower, NormalizeText(asset.Stem)))
        return "stem";

    if (token.Contains('/'))
        return "path-fragment";

    if (asset.Kind.Equals(token, StringComparison.OrdinalIgnoreCase))
        return "kind";

    return "token";
}

static int ScoreMatch(string matchType, string stringValue, AssetFile asset)
{
    var score = matchType switch
    {
        "exact-file" => 12,
        "stem" => 9,
        "path-fragment" => 8,
        "format-family" => 8,
        "token" => 5,
        "kind" => 2,
        _ => 1
    };

    var lower = NormalizeText(stringValue);
    if (lower.Contains(NormalizeText(asset.Family), StringComparison.OrdinalIgnoreCase))
        score += 2;

    if (asset.Source.Equals("ps2", StringComparison.OrdinalIgnoreCase) && lower.Contains("battle", StringComparison.OrdinalIgnoreCase))
        score += 1;

    if (asset.Source.Equals("ps3data", StringComparison.OrdinalIgnoreCase) && lower.Contains("ps3data", StringComparison.OrdinalIgnoreCase))
        score += 1;

    return score;
}

static string DetermineEvidence(string stringValue, IReadOnlyList<FunctionHit> functions, IReadOnlyList<AssetMatch> matches, int bestScore)
{
    if (functions.Count > 0 && matches.Any(static x => x.MatchType == "exact-file" || x.MatchType == "stem"))
        return "proved-string-xref";

    if (functions.Count > 0 && matches.Any(static x => x.MatchType == "format-family" || x.MatchType == "path-fragment"))
        return "structural-format-xref";

    if (functions.Count > 0 && bestScore >= 5)
        return "structural-token-xref";

    if (IsRelevantString(NormalizeText(stringValue), TokenizeString(stringValue).ToArray(), new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase)))
        return "structural-string";

    return "guess";
}

static string SuggestRenameAction(IReadOnlyList<FunctionHit> functions, IReadOnlyList<AssetMatch> matches, string evidence)
{
    if (functions.Count == 0)
        return "No rename yet; string has no exported xref in this snapshot.";

    var functionList = string.Join(", ", functions.Take(4).Select(static x => x.Name));
    var assetKinds = string.Join("/", matches.Select(static x => x.Kind).Distinct(StringComparer.OrdinalIgnoreCase).Take(3));

    return evidence switch
    {
        "proved-string-xref" => $"Inspect then rename/comment function(s): {functionList}; asset kind(s): {assetKinds}.",
        "structural-format-xref" => $"Conservative loader/path-builder candidate: {functionList}; comment format-family evidence before rename.",
        "structural-token-xref" => $"Comment first; rename only after decompile proves parser/loader role: {functionList}.",
        _ => $"Use as search seed for {functionList}; no direct rename."
    };
}

static AssetLinkSummary BuildSummary(Options options, IReadOnlyList<AssetFile> assets, IdaExport ida, IReadOnlyList<LinkCard> cards)
{
    var assetsBySource = assets
        .GroupBy(static x => x.Source, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(static x => x.Key, static x => x.Count(), StringComparer.OrdinalIgnoreCase);

    var assetsByKind = assets
        .GroupBy(static x => x.Kind, StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(static x => x.Count())
        .Take(40)
        .ToDictionary(static x => x.Key, static x => x.Count(), StringComparer.OrdinalIgnoreCase);

    var cardsByEvidence = cards
        .GroupBy(static x => x.Evidence, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(static x => x.Key, static x => x.Count(), StringComparer.OrdinalIgnoreCase);

    var highValue = cards
        .Where(static x => x.Score >= 10 && x.XrefCount > 0)
        .Take(100)
        .Select(static x => new HighValueCard(x.StringEa, x.StringValue, x.Evidence, x.Score, x.XrefCount, x.Functions.Select(static f => f.Name).Take(5).ToArray(), x.Assets.Take(5).ToArray()))
        .ToArray();

    return new AssetLinkSummary(
        GeneratedAt: DateTimeOffset.Now,
        IdaExportRoot: Path.GetFullPath(options.IdaExportRoot),
        Roots: options.Roots,
        AssetCount: assets.Count,
        IdaStringCount: ida.Strings.Count,
        IdaXrefCount: ida.Xrefs.Count,
        IdaFunctionCount: ida.Functions.Count,
        CardCount: cards.Count,
        AssetsBySource: assetsBySource,
        TopKinds: assetsByKind,
        CardsByEvidence: cardsByEvidence,
        HighValueCards: highValue);
}

static void WriteInventory(string path, IReadOnlyList<AssetFile> assets)
{
    var builder = new StringBuilder();
    builder.AppendLine("source,relative_path,kind,family,size,tokens");
    foreach (var asset in assets)
    {
        var row = new[]
        {
            asset.Source,
            asset.RelativePath,
            asset.Kind,
            asset.Family,
            asset.Size.ToString(CultureInfo.InvariantCulture),
            string.Join(" ", asset.Tokens.Take(20))
        };
        builder.AppendLine(string.Join(",", row.Select(Csv)));
    }

    File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
}

static void WriteFileStatus(string path, IReadOnlyList<AssetFile> assets, IReadOnlyList<LinkCard> cards)
{
    var hitsByAsset = BuildAssetLinkHits(cards);
    var builder = new StringBuilder();
    builder.AppendLine("source,relative_path,top_partition,kind,family,basename_key,sidecar_group_key,size,best_evidence,best_score,match_count,xref_function_count,functions,string_samples,match_types,proposed_action,blocked_reason");

    foreach (var asset in assets.OrderBy(static x => x.Source, StringComparer.OrdinalIgnoreCase).ThenBy(static x => x.RelativePath, StringComparer.OrdinalIgnoreCase))
    {
        hitsByAsset.TryGetValue(AssetKey(asset.Source, asset.RelativePath), out var hits);
        hits ??= new List<AssetLinkHit>();
        var hitArray = hits
            .OrderByDescending(static x => x.Card.Score)
            .ThenByDescending(static x => x.Card.XrefCount)
            .ToArray();
        var best = hitArray.FirstOrDefault();
        var functions = hitArray
            .SelectMany(static x => x.Card.Functions)
            .Where(static x => !string.IsNullOrWhiteSpace(x.Ea) || !string.IsNullOrWhiteSpace(x.Name))
            .Select(static x => string.IsNullOrWhiteSpace(x.Ea) ? x.Name : $"{x.Ea}:{x.Name}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
        var stringSamples = hitArray
            .Select(static x => x.Card.StringValue)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();
        var matchTypes = hitArray
            .Select(static x => x.Match.MatchType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var values = new[]
        {
            asset.Source,
            asset.RelativePath,
            BuildTopPartition(asset),
            asset.Kind,
            asset.Family,
            NormalizeText(asset.Stem),
            BuildSidecarGroupKey(asset),
            asset.Size.ToString(CultureInfo.InvariantCulture),
            best?.Card.Evidence ?? "structural-file-link",
            (best?.Card.Score ?? 0).ToString(CultureInfo.InvariantCulture),
            hitArray.Length.ToString(CultureInfo.InvariantCulture),
            functions.Length.ToString(CultureInfo.InvariantCulture),
            string.Join(" | ", functions),
            string.Join(" | ", stringSamples),
            string.Join(";", matchTypes),
            SuggestFileAction(asset, hitArray),
            GetBlockedReason(asset)
        };
        builder.AppendLine(string.Join(",", values.Select(Csv)));
    }

    File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
}

static Dictionary<string, List<AssetLinkHit>> BuildAssetLinkHits(IReadOnlyList<LinkCard> cards)
{
    var result = new Dictionary<string, List<AssetLinkHit>>(StringComparer.OrdinalIgnoreCase);
    foreach (var card in cards)
    {
        foreach (var match in card.Assets)
        {
            var key = AssetKey(match.Source, match.RelativePath);
            if (!result.TryGetValue(key, out var list))
            {
                list = new List<AssetLinkHit>();
                result[key] = list;
            }

            list.Add(new AssetLinkHit(card, match));
        }
    }

    return result;
}

static string AssetKey(string source, string relativePath) => $"{source}|{NormalizePath(relativePath)}";

static string BuildTopPartition(AssetFile asset)
{
    var parts = asset.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (parts.Length == 0)
        return asset.Source;

    if (asset.Source.Equals("ps2", StringComparison.OrdinalIgnoreCase))
        return string.Join('/', parts.Take(Math.Min(3, parts.Length)));

    if (asset.Source.Equals("ps3data", StringComparison.OrdinalIgnoreCase))
    {
        if (parts.Length >= 3 && (parts[0].Equals("map", StringComparison.OrdinalIgnoreCase)
            || parts[0].Equals("btlmap", StringComparison.OrdinalIgnoreCase)
            || parts[0].Equals("chr", StringComparison.OrdinalIgnoreCase)
            || parts[0].Equals("event", StringComparison.OrdinalIgnoreCase)))
        {
            return string.Join('/', parts.Take(3));
        }

        return parts[0];
    }

    return parts[0];
}

static string BuildSidecarGroupKey(AssetFile asset)
{
    var relative = NormalizePath(asset.RelativePath);
    var slash = relative.LastIndexOf('/');
    var dir = slash >= 0 ? relative[..slash] : "";
    var stem = NormalizeText(asset.Stem);

    if (asset.Kind.Equals(".mgrp", StringComparison.OrdinalIgnoreCase))
    {
        var match = Regex.Match(stem, @"^(.*?)(\d{2})$");
        if (match.Success && match.Groups[1].Value.Length > 0)
            stem = match.Groups[1].Value;
    }

    if (asset.Kind.Equals(".clt", StringComparison.OrdinalIgnoreCase) && stem.StartsWith("c_", StringComparison.OrdinalIgnoreCase))
        stem = "t_" + stem[2..];

    return string.IsNullOrWhiteSpace(dir) ? stem : $"{dir}/{stem}";
}

static string SuggestFileAction(AssetFile asset, IReadOnlyList<AssetLinkHit> hits)
{
    if (hits.Count == 0)
        return "inventory-only; no exported executable string match yet";

    var hasFunction = hits.Any(static x => x.Card.Functions.Length > 0);
    var hasExact = hits.Any(static x => x.Match.MatchType is "exact-file" or "stem");
    var hasFormat = hits.Any(static x => x.Match.MatchType is "format-family" or "path-fragment");

    if (hasFunction && hasExact)
        return "inspect/decompile xref owner; rename loader/path-builder only if role is clear";

    if (hasFunction && hasFormat)
        return "comment or conservative loader rename after decompile; format-family evidence";

    if (hasFunction)
        return "comment first; token xref needs decompile before rename";

    return "structural string/link only; no IDA rename";
}

static string GetBlockedReason(AssetFile asset)
{
    var family = NormalizeText(asset.Family);
    return asset.Kind.ToLowerInvariant() switch
    {
        ".bin" when family.Contains("battle/kernel", StringComparison.OrdinalIgnoreCase) => "kernel table loader may be named; table fields/writer require consumer+runtime proof",
        ".bin" => "container payload/chunk semantics blocked until parser and consumer proof",
        ".ftc" => "FTCX sidecar framing known; table-byte semantics blocked",
        ".mgrp" => "loader/registry allowed; decode/playback/pose blocked until runtime consumer proof",
        ".chr" => "carrier/linkage allowed; skeleton/mesh semantics blocked without parser/runtime proof",
        ".vpa" => "MAP1 framing/linkage allowed; map geometry/walkmesh semantics blocked",
        ".ebp" => "EV01 framing/linkage allowed; event script/payload semantics blocked",
        ".omd" => "companion structural file; material/mesh semantics blocked",
        ".wd" => "PS2 sound bank structural; playback/decode semantics blocked",
        ".dds.phyre" => "texture carrier; material/effect recipe semantics blocked",
        ".dae.phyre" => "geometry carrier; engine-exact mesh/material blocked until Phyre/runtime proof",
        ".ags.phyre" => "texture animation carrier; not geometry",
        ".fx.phyre" => "shader carrier; DXBC/shader semantics blocked until disasm/runtime proof",
        ".swf" => "UI SWF asset; gameplay logic blocked",
        ".fev" => "FMOD project load allowed; clip/event resolution blocked",
        ".fsb" => "FMOD bank load allowed; sample decode/clip mapping blocked",
        ".webm" => "direct media; FFX.exe WebM decoder not proved",
        ".dat" when family.Contains("abmap", StringComparison.OrdinalIgnoreCase) => "ABMap DAT final carrier; packer/writer blocked",
        ".dat" => "generic DAT sidecar; schema blocked",
        ".txt" when family.Contains("sound_pc", StringComparison.OrdinalIgnoreCase) => "sound text/index may be binary; schema must be proved before semantic rename",
        ".txt" => "text/list role must be confirmed per folder",
        _ => ""
    };
}

static void WriteFunctionQueue(string path, IReadOnlyList<LinkCard> cards)
{
    var rows = cards
        .SelectMany(card => card.Functions.Select(fn => new { Card = card, Function = fn }))
        .GroupBy(item => item.Function.Name, StringComparer.OrdinalIgnoreCase)
        .Select(group =>
        {
            var groupedCards = group.Select(static x => x.Card)
                .Distinct()
                .OrderByDescending(static x => x.Score)
                .ToArray();
            var firstFunction = group.Select(static x => x.Function).First();
            var evidence = groupedCards
                .GroupBy(static x => x.Evidence, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(static x => x.Count())
                .First().Key;
            var assetKinds = groupedCards
                .SelectMany(static x => x.Assets)
                .Select(static x => x.Kind)
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToArray();
            var sources = groupedCards
                .SelectMany(static x => x.Assets)
                .Select(static x => x.Source)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new
            {
                Function = firstFunction,
                Count = groupedCards.Length,
                BestScore = groupedCards.Max(static x => x.Score),
                Evidence = evidence,
                Sources = string.Join(";", sources),
                AssetKinds = string.Join(";", assetKinds),
                Strings = string.Join(" | ", groupedCards.Take(5).Select(static x => x.StringValue)),
                Assets = string.Join(" | ", groupedCards.SelectMany(static x => x.Assets).Take(8).Select(static x => $"{x.Source}:{x.RelativePath}"))
            };
        })
        .OrderByDescending(static x => x.Count)
        .ThenByDescending(static x => x.BestScore)
        .ThenBy(static x => x.Function.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    var builder = new StringBuilder();
    builder.AppendLine("function_ea,function_name,card_count,best_score,evidence,sources,asset_kinds,string_samples,asset_samples");
    foreach (var row in rows)
    {
        var values = new[]
        {
            row.Function.Ea,
            row.Function.Name,
            row.Count.ToString(CultureInfo.InvariantCulture),
            row.BestScore.ToString(CultureInfo.InvariantCulture),
            row.Evidence,
            row.Sources,
            row.AssetKinds,
            row.Strings,
            row.Assets
        };
        builder.AppendLine(string.Join(",", values.Select(Csv)));
    }

    File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
}

static void WriteMarkdown(string path, AssetLinkSummary summary, IReadOnlyList<LinkCard> cards, Options options)
{
    var builder = new StringBuilder();
    builder.AppendLine("# FFX Asset-to-Executable Link Graph");
    builder.AppendLine();
    builder.AppendLine($"Generated: `{summary.GeneratedAt:O}`");
    builder.AppendLine();
    builder.AppendLine("## Scope");
    builder.AppendLine();
    builder.AppendLine($"- IDA export: `{summary.IdaExportRoot}`");
    foreach (var root in summary.Roots)
        builder.AppendLine($"- {root.Source}: `{root.Path}`");
    builder.AppendLine();
    builder.AppendLine("## Summary");
    builder.AppendLine();
    builder.AppendLine($"- Assets scanned: `{summary.AssetCount.ToString(CultureInfo.InvariantCulture)}`");
    builder.AppendLine($"- IDA strings: `{summary.IdaStringCount.ToString(CultureInfo.InvariantCulture)}`");
    builder.AppendLine($"- IDA xrefs: `{summary.IdaXrefCount.ToString(CultureInfo.InvariantCulture)}`");
    builder.AppendLine($"- IDA functions: `{summary.IdaFunctionCount.ToString(CultureInfo.InvariantCulture)}`");
    builder.AppendLine($"- Link cards: `{summary.CardCount.ToString(CultureInfo.InvariantCulture)}`");
    builder.AppendLine();
    builder.AppendLine("## Assets By Source");
    builder.AppendLine();
    builder.AppendLine("| Source | Count |");
    builder.AppendLine("|---|---:|");
    foreach (var (source, count) in summary.AssetsBySource.OrderBy(static x => x.Key, StringComparer.OrdinalIgnoreCase))
        builder.AppendLine($"| `{source}` | `{count.ToString(CultureInfo.InvariantCulture)}` |");
    builder.AppendLine();
    builder.AppendLine("## Top Asset Kinds");
    builder.AppendLine();
    builder.AppendLine("| Kind | Count |");
    builder.AppendLine("|---|---:|");
    foreach (var (kind, count) in summary.TopKinds)
        builder.AppendLine($"| `{kind}` | `{count.ToString(CultureInfo.InvariantCulture)}` |");
    builder.AppendLine();
    builder.AppendLine("## Cards By Evidence");
    builder.AppendLine();
    builder.AppendLine("| Evidence | Count | Meaning |");
    builder.AppendLine("|---|---:|---|");
    foreach (var (evidence, count) in summary.CardsByEvidence.OrderByDescending(static x => x.Value))
        builder.AppendLine($"| `{evidence}` | `{count.ToString(CultureInfo.InvariantCulture)}` | {ExplainEvidence(evidence)} |");
    builder.AppendLine();
    builder.AppendLine("## High Value IDA String Cards");
    builder.AppendLine();
    builder.AppendLine("These are the first queue for IDA decompile/rename work. They link a concrete IDA string to asset files or asset families.");
    builder.AppendLine();
    builder.AppendLine("| Score | Evidence | String EA | IDA string | Xref functions | Asset samples | Rename action |");
    builder.AppendLine("|---:|---|---|---|---|---|---|");

    foreach (var card in cards.Where(static x => x.Score >= 10 && x.XrefCount > 0).Take(options.MarkdownCardLimit))
    {
        var functions = string.Join("<br>", card.Functions.Take(4).Select(static x => EscapeMd(x.Name)));
        var assets = string.Join("<br>", card.Assets.Take(4).Select(static x => $"{EscapeMd(x.Source)}: `{EscapeMd(x.RelativePath)}`"));
        builder.AppendLine($"| `{card.Score.ToString(CultureInfo.InvariantCulture)}` | `{card.Evidence}` | `{card.StringEa}` | `{EscapeMd(Shorten(card.StringValue, 90))}` | {functions} | {assets} | {EscapeMd(card.RenameAction)} |");
    }

    builder.AppendLine();
    builder.AppendLine("## Immediate Rename Queue");
    builder.AppendLine();
    builder.AppendLine("1. For `proved-string-xref` cards: decompile each listed function and apply names/comments when the role is loader/parser/consumer-clear.");
    builder.AppendLine("2. For `structural-format-xref` cards: name path builders and generic loaders conservatively; do not name field parsers from path evidence alone.");
    builder.AppendLine("3. For `structural-token-xref` cards: comment first; rename only after the decompile proves the file or table role.");
    builder.AppendLine("4. For `magicFiles`: open the per-DLL IDB under `Notes-magic_####.dll\\Extract Ida Pro` and keep FFX-2 separate.");
    builder.AppendLine();
    builder.AppendLine("## Output Files");
    builder.AppendLine();
    builder.AppendLine("- `asset-linker-inventory.csv`: complete file inventory.");
    builder.AppendLine("- `asset-linker-summary.json`: machine-readable summary.");
    builder.AppendLine("- `asset-linker-cards.json`: full link-card list.");
    builder.AppendLine("- `asset-linker-functions.csv`: function-centric queue.");
    builder.AppendLine("- `asset-linker-domain-report.md`: domain-centric queue.");
    builder.AppendLine("- `asset-linker-report.md`: this report.");

    File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
}

static void WriteDomainReport(string path, AssetLinkSummary summary, IReadOnlyList<LinkCard> cards)
{
    var domains = new[]
    {
        new DomainSpec("Battle kernel", ["battle/kernel", "a_ability", "important", "w_name", "command", "monmagic", "ply_save", "btl_txt", "btlend_txt", "sphere", "panel"]),
        new DomainSpec("Battle btl", ["battle/btl", "btlbin", "system_01", ".ftc"]),
        new DomainSpec("SphereGrid / ABMap", ["sphere", "panel", "abmap", "eiichi_abmap", "menu/abmap"]),
        new DomainSpec("MGRP / motion", [".mgrp", "resident", "chrdata", "motion", "regmot"]),
        new DomainSpec("ps3data chr/map", ["ps3data/chr", "ps3data/map", "ps3data/btlmap", "ahwin32", ".dae.phyre", ".ags.phyre"]),
        new DomainSpec("Magic", ["magic_", "magic/%", "magicfiles", "bat_eff", "dat_et", "monmagic"]),
        new DomainSpec("Sound / voice", ["sound_pc", "music/", "music_ps2", "voice/", "sfx/", ".fev", ".fsb", "tk:snd"]),
        new DomainSpec("Flash / UI", ["/flash/", ".swf", "keyboard_icon", "pad_icon", "pausemenu", "escmenu"]),
        new DomainSpec("CD index / archive", ["cdrom.fid", "cdrom.fnd", "cdrom.mdg", "sizetbl.bin", "modulesize.bin"])
    };

    var builder = new StringBuilder();
    builder.AppendLine("# FFX AssetLinker Domain Queue");
    builder.AppendLine();
    builder.AppendLine($"Generated: `{summary.GeneratedAt:O}`");
    builder.AppendLine();
    builder.AppendLine("This view is the practical queue for IDA work. It groups the raw asset-to-string cards by domain and by xref function.");

    foreach (var domain in domains)
    {
        var domainCards = cards
            .Where(card => DomainMatches(card, domain))
            .OrderByDescending(static x => x.Score)
            .ThenByDescending(static x => x.XrefCount)
            .ToArray();

        builder.AppendLine();
        builder.AppendLine($"## {domain.Name}");
        builder.AppendLine();
        builder.AppendLine($"Cards: `{domainCards.Length.ToString(CultureInfo.InvariantCulture)}`");
        builder.AppendLine();

        if (domainCards.Length == 0)
        {
            builder.AppendLine("No cards in this snapshot.");
            continue;
        }

        builder.AppendLine("### Top Functions");
        builder.AppendLine();
        builder.AppendLine("| Function | Cards | Best score | String samples | Rename stance |");
        builder.AppendLine("|---|---:|---:|---|---|");
        foreach (var group in domainCards
            .SelectMany(static card => card.Functions.Select(fn => new { Function = fn, Card = card }))
            .GroupBy(static x => x.Function.Name, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static g => g.Count())
            .ThenByDescending(static g => g.Max(static x => x.Card.Score))
            .Take(15))
        {
            var fn = group.First().Function;
            var groupCards = group.Select(static x => x.Card).OrderByDescending(static x => x.Score).ToArray();
            var samples = string.Join("<br>", groupCards.Take(3).Select(static x => EscapeMd(Shorten(x.StringValue, 70))));
            var stance = BuildDomainRenameStance(domain.Name, groupCards);
            builder.AppendLine($"| `{fn.Name}` | `{groupCards.Length.ToString(CultureInfo.InvariantCulture)}` | `{groupCards.Max(static x => x.Score).ToString(CultureInfo.InvariantCulture)}` | {samples} | {EscapeMd(stance)} |");
        }

        builder.AppendLine();
        builder.AppendLine("### Top Cards");
        builder.AppendLine();
        builder.AppendLine("| Score | Evidence | String EA | IDA string | Functions | Asset samples |");
        builder.AppendLine("|---:|---|---|---|---|---|");
        foreach (var card in domainCards.Take(20))
        {
            var functions = string.Join("<br>", card.Functions.Take(4).Select(static x => EscapeMd(x.Name)));
            var assets = string.Join("<br>", card.Assets.Take(4).Select(static x => $"{EscapeMd(x.Source)}: `{EscapeMd(x.RelativePath)}`"));
            builder.AppendLine($"| `{card.Score.ToString(CultureInfo.InvariantCulture)}` | `{card.Evidence}` | `{card.StringEa}` | `{EscapeMd(Shorten(card.StringValue, 90))}` | {functions} | {assets} |");
        }
    }

    File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
}

static string BuildDomainRenameStance(string domain, IReadOnlyList<LinkCard> cards)
{
    if (cards.Any(static x => x.Evidence == "proved-string-xref"))
        return domain switch
        {
            "MGRP / motion" => "Name loader/dispatcher only; no decode/playback name without consumer proof.",
            "ps3data chr/map" => "Name ps3data/Phyre loaders conservatively; no mesh/material claim from texture path alone.",
            "Magic" => "Name texture/path/DLL dispatch only; effect recipe remains separate proof.",
            _ => "Decompile and rename/comment if loader/parser/consumer role is clear."
        };

    if (cards.Any(static x => x.Evidence == "structural-format-xref"))
        return "Conservative path-builder/loader candidate; comment before rename.";

    return "Comment/search seed first; no rename yet.";
}

static bool DomainMatches(LinkCard card, DomainSpec domain)
{
    var text = NormalizeText(card.StringValue + " " + string.Join(" ", card.Assets.Select(static x => x.RelativePath)) + " " + string.Join(" ", card.Assets.Select(static x => x.Family)));
    return domain.Needles.Any(needle => text.Contains(NormalizeText(needle).Replace("%", ""), StringComparison.OrdinalIgnoreCase));
}

static string ExplainEvidence(string evidence) => evidence switch
{
    "proved-string-xref" => "IDA string has xrefs and matches concrete asset stem/file token.",
    "structural-format-xref" => "IDA string has xrefs and matches a path/format family.",
    "structural-token-xref" => "IDA string has xrefs and token overlap, but needs decompile before naming.",
    "structural-string" => "Relevant string without usable xref in export.",
    _ => "Weak or exploratory seed."
};

static void WriteJson<T>(string path, T value)
{
    var options = new JsonSerializerOptions { WriteIndented = true };
    File.WriteAllText(path, JsonSerializer.Serialize(value, options), Encoding.UTF8);
}

static List<T> ReadJsonl<T>(string path, Func<JsonElement, T> factory)
{
    if (!File.Exists(path))
        throw new FileNotFoundException($"Required IDA export file missing: {path}", path);

    var result = new List<T>();
    foreach (var line in File.ReadLines(path, Encoding.UTF8))
    {
        if (string.IsNullOrWhiteSpace(line))
            continue;

        using var document = JsonDocument.Parse(line);
        result.Add(factory(document.RootElement));
    }

    return result;
}

static string ReadString(JsonElement element, string property)
{
    if (!element.TryGetProperty(property, out var value))
        return "";

    return value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Number => value.GetRawText(),
        _ => value.GetRawText()
    };
}

static int ReadInt(JsonElement element, string property)
{
    if (!element.TryGetProperty(property, out var value))
        return 0;

    if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        return number;

    if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        return number;

    return 0;
}

static string[] BuildAssetTokens(string relative, string fileName, string stem, string kind, string family)
{
    var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        NormalizeText(fileName),
        NormalizeText(stem),
        NormalizeText(kind),
        NormalizeText(family)
    };

    foreach (var part in NormalizePath(relative).Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        tokens.Add(NormalizeText(part));

    foreach (var part in NormalizePath(family).Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        tokens.Add(NormalizeText(part));

    if (Regex.IsMatch(stem, @"^magic_\d{4}$", RegexOptions.IgnoreCase))
        tokens.Add("magic_%04d.dll");

    if (Regex.IsMatch(stem, @"^resident\d+$", RegexOptions.IgnoreCase))
        tokens.Add("resident");

    if (kind.Equals(".mgrp", StringComparison.OrdinalIgnoreCase))
        tokens.Add("mgrp");

    if (kind.Equals(".ahwin32", StringComparison.OrdinalIgnoreCase))
        tokens.Add("ahwin32");

    return tokens
        .Where(static x => x.Length > 0)
        .Where(static x => !IsWeakToken(x))
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static IEnumerable<string> TokenizeString(string value)
{
    var lower = NormalizeText(value);
    var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (Match match in Regex.Matches(lower, @"[a-z0-9_][a-z0-9_\-%]*\.[a-z0-9_\.]+"))
        tokens.Add(match.Value.Replace("%04d", "%04d").Replace("%d", ""));

    foreach (Match match in Regex.Matches(lower, @"[a-z0-9_][a-z0-9_]{2,}"))
        tokens.Add(match.Value);

    if (lower.Contains("magic_%04d.dll", StringComparison.OrdinalIgnoreCase))
        tokens.Add("magic_%04d.dll");

    if (lower.Contains("resident", StringComparison.OrdinalIgnoreCase))
        tokens.Add("resident");

    if (lower.Contains(".mgrp", StringComparison.OrdinalIgnoreCase))
        tokens.Add("mgrp");

    if (lower.Contains("ahwin32", StringComparison.OrdinalIgnoreCase))
        tokens.Add("ahwin32");

    if (lower.Contains("battle/kernel", StringComparison.OrdinalIgnoreCase))
        tokens.Add("battle/kernel");

    if (lower.Contains("battle/btl", StringComparison.OrdinalIgnoreCase))
        tokens.Add("battle/btl");

    return tokens.Where(static x => !IsWeakToken(x));
}

static bool IsRelevantString(string valueNorm, IReadOnlyList<string> stringTokens, Dictionary<string, List<int>> assetTokenIndex)
{
    string[] needles =
    {
        ".bin", ".ftc", ".mgrp", ".dds.phyre", ".dae.phyre", ".ags.phyre",
        ".ahwin32", ".dll", "battle/", "battle\\", "kernel", "ps3data",
        "magic", "chrdata", "resident", "sphere", "panel", "ply_save",
        "a_ability", "important", "w_name", "btl_txt", "monmagic"
    };

    if (needles.Any(needle => valueNorm.Contains(needle, StringComparison.OrdinalIgnoreCase)))
        return true;

    return stringTokens.Any(token => assetTokenIndex.TryGetValue(token, out var ids) && ids.Count <= 100);
}

static bool IsWeakToken(string token)
{
    if (string.IsNullOrWhiteSpace(token) || token.Length < 3)
        return true;

    var normalized = NormalizeText(token);
    string[] weak =
    {
        "ffx", "data", "game", "gamedata", "ps2", "ps3", "pc", "new", "old",
        "master", "jppc", "uspc", "inpc", "d3d11", "texture", "textures",
        "tex", "mdl", "mot", "bin", "dll", "map", "chr", "mon", "obj",
        "menu", "icon", "font", "base", "file", "read", "write", "load",
        "event", "common", "system", "battle", "kernel", "magicfiles"
    };

    return weak.Contains(normalized, StringComparer.OrdinalIgnoreCase);
}

static string ClassifyKind(string fileName)
{
    var lower = fileName.ToLowerInvariant();
    string[] multi =
    {
        ".dds.phyre", ".dae.phyre", ".ags.phyre", ".ahwin32"
    };

    foreach (var suffix in multi)
    {
        if (lower.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return suffix;
    }

    return Path.GetExtension(fileName).ToLowerInvariant();
}

static string ClassifyFamily(string source, string relative, string kind)
{
    var parts = NormalizePath(relative).Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (parts.Length == 0)
        return source;

    if (source.Equals("ps2", StringComparison.OrdinalIgnoreCase))
    {
        var start = Array.FindIndex(parts, static p => p.Equals("master", StringComparison.OrdinalIgnoreCase));
        if (start >= 0 && parts.Length > start + 2)
            return string.Join('/', parts.Skip(start + 2).Take(Math.Min(3, parts.Length - start - 3)));

        return string.Join('/', parts.Take(Math.Min(4, parts.Length - 1)));
    }

    if (source.Equals("ps3data", StringComparison.OrdinalIgnoreCase))
        return string.Join('/', parts.Take(Math.Min(4, parts.Length - 1)));

    if (source.StartsWith("magicFiles", StringComparison.OrdinalIgnoreCase))
    {
        if (parts.Length > 0 && Regex.IsMatch(parts[0], @"^Notes-magic_\d{4}\.dll$", RegexOptions.IgnoreCase))
            return parts[0];

        return parts[0];
    }

    return string.Join('/', parts.Take(Math.Min(4, parts.Length - 1)));
}

static string GetStem(string fileName)
{
    var lower = fileName.ToLowerInvariant();
    foreach (var suffix in new[] { ".dds.phyre", ".dae.phyre", ".ags.phyre", ".ahwin32" })
    {
        if (lower.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return fileName[..^suffix.Length];
    }

    return Path.GetFileNameWithoutExtension(fileName);
}

static string NormalizePath(string path) => path.Replace('\\', '/').Trim('/');

static string NormalizeText(string text) => NormalizePath(text).ToLowerInvariant();

static bool ContainsPathSegment(string text, string segment)
{
    if (string.IsNullOrWhiteSpace(segment))
        return false;

    var normalizedText = "/" + NormalizeText(text).Trim('/') + "/";
    var normalizedSegment = NormalizeText(segment).Trim('/');
    return normalizedText.Contains("/" + normalizedSegment + "/", StringComparison.OrdinalIgnoreCase);
}

static string Csv(string value)
{
    if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        return "\"" + value.Replace("\"", "\"\"") + "\"";

    return value;
}

static string EscapeMd(string value) => value
    .Replace("\\", "\\\\")
    .Replace("|", "\\|")
    .Replace("\r", " ")
    .Replace("\n", " ");

static string Shorten(string value, int max)
{
    if (value.Length <= max)
        return value;

    return value[..Math.Max(0, max - 3)] + "...";
}

static void PrintHelp()
{
    Console.WriteLine("""
AssetLinkerLab - build an asset-to-IDA link graph for FFX.

Usage:
  dotnet run --project RuntimeTools/AssetLinkerLab/AssetLinkerLab.csproj -- [options]

Options:
  --ps2-root <path>          Default: D:\FFX Extracted\FFX\ffx_ps2
  --ps3-root <path>          Default: D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data
  --magic-root <path>        Default: Steam FFX magicFiles\FFX root
  --ida-export <path>        Default: Codex IDA export folder from 2026-06-02
  --output <path>            Default: work\asset_linker
  --markdown-card-limit <n>  Default: 150
  --help                    Show this help.
""");
}

record RootSpec(string Source, string Path, string PortableRoot);

record Options(
    string Ps2Root,
    string Ps3Root,
    string MagicRoot,
    string IdaExportRoot,
    string OutputRoot,
    int MaxAssetsPerToken,
    int MaxAssetsPerCard,
    int MarkdownCardLimit,
    bool ShowHelp)
{
    public RootSpec[] Roots =>
    [
        new("ps2", Ps2Root, "<ffx_ps2>"),
        new("ps3data", Ps3Root, "<ps3data>"),
        new("magicFilesFFX", MagicRoot, "<magicFiles/FFX>")
    ];

    public static Options Parse(string[] args)
    {
        var ps2Root = @"D:\FFX Extracted\FFX\ffx_ps2";
        var ps3Root = @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data";
        var magicRoot = @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\magicFiles\FFX";
        var idaExport = @"C:\Users\wande\Documents\Codex\2026-06-02\como-usar-o-codex-no-ida\outputs\ida_export\FFX.exe_20260602_190300";
        var output = Path.Combine(Directory.GetCurrentDirectory(), "work", "asset_linker");
        var maxAssetsPerToken = 500;
        var maxAssetsPerCard = 24;
        var markdownCardLimit = 150;
        var showHelp = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string Next()
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException($"Missing value for {arg}");
                return args[++i];
            }

            switch (arg)
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                case "--ps2-root":
                    ps2Root = Next();
                    break;
                case "--ps3-root":
                    ps3Root = Next();
                    break;
                case "--magic-root":
                    magicRoot = Next();
                    break;
                case "--ida-export":
                    idaExport = Next();
                    break;
                case "--output":
                    output = Next();
                    break;
                case "--max-assets-per-token":
                    maxAssetsPerToken = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--max-assets-per-card":
                    maxAssetsPerCard = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--markdown-card-limit":
                    markdownCardLimit = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {arg}");
            }
        }

        return new Options(ps2Root, ps3Root, magicRoot, idaExport, output, maxAssetsPerToken, maxAssetsPerCard, markdownCardLimit, showHelp);
    }
}

record AssetFile(string Source, string Root, string RelativePath, string FileName, string Stem, string Kind, string Family, long Size, string[] Tokens);

record IdaString(string Ea, string Value);

record IdaXref(string From, string To, string Function, int Type);

record IdaFunction(string Ea, string Name, string PseudocodeFile, string DisasmFile, int Size);

record IdaExport(
    string Root,
    IReadOnlyList<IdaString> Strings,
    IReadOnlyList<IdaXref> Xrefs,
    IReadOnlyList<IdaFunction> Functions,
    Dictionary<string, IdaXref[]> XrefsByString,
    Dictionary<string, IdaFunction> FunctionsByName);

record AssetMatch(string Source, string RelativePath, string Kind, string Family, string MatchType, string Token, int Score);

record FunctionHit(string Ea, string Name, int Size, string PseudocodeFile, string DisasmFile);

record LinkCard(string StringEa, string StringValue, string Evidence, int Score, int XrefCount, FunctionHit[] Functions, AssetMatch[] Assets, string RenameAction);

record AssetLinkHit(LinkCard Card, AssetMatch Match);

record FamilyMatch(string Token, string MatchType, int Score, AssetFile[] Assets);

record HighValueCard(string StringEa, string StringValue, string Evidence, int Score, int XrefCount, string[] Functions, AssetMatch[] Assets);

record AssetLinkSummary(
    DateTimeOffset GeneratedAt,
    string IdaExportRoot,
    RootSpec[] Roots,
    int AssetCount,
    int IdaStringCount,
    int IdaXrefCount,
    int IdaFunctionCount,
    int CardCount,
    Dictionary<string, int> AssetsBySource,
    Dictionary<string, int> TopKinds,
    Dictionary<string, int> CardsByEvidence,
    HighValueCard[] HighValueCards);

record DomainSpec(string Name, string[] Needles);
