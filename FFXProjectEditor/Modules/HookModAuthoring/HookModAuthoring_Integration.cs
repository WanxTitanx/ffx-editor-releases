using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXProjectEditor.FfxLib.Mods;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.HookModAuthoring;

internal partial class HookModAuthoring_DataModel
{
    private HookProjectProfile project = new();
    private readonly Dictionary<string, (HookArtifact[] Artifacts, HookSourceSnapshot[] Sources)> prepared = new(StringComparer.Ordinal);
    private HookExportPreview? packagePreview;
    private bool loadingProfile;
    private string? profileHash;
    private static string MetadataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FFXProjectEditor", "hook-projects");
    public MonsterRewards_DataModel Rewards { get; } = new();
    public TextLanguageBuild? LanguageBuild { get; private set; }
    [ObservableProperty] private bool onlyModEnabled;
    [ObservableProperty] private string executablePath = "";
    [ObservableProperty] private string validatorPath = "";
    [ObservableProperty] private string consumerReport = "";
    [ObservableProperty] private string packageReport = "";
    [ObservableProperty] private string registryJson = "{}";
    [ObservableProperty] private string presetJson = new VanguardPreset().Serialize();
    [ObservableProperty] private string commandBankPath = "";
    [ObservableProperty] private string elementalJson = Encoding.UTF8.GetString(ElementalPackDocument.Create().Serialize());
    [ObservableProperty] private string elementalInputsJson = "[]";
    [ObservableProperty] private string affinityPreviewJson = JsonSerializer.Serialize(new ElementalPreviewRequest(),HookProjectProfile.JsonOptions);
    [ObservableProperty] private string affinityReport = "";
    [ObservableProperty] private string f7Json = "{}";
    [ObservableProperty] private string scanIni = "[element_scan]\n";
    [ObservableProperty] private string productionRecordPath = "";
    [ObservableProperty] private string nativeSaveImagePath = "";
    [ObservableProperty] private string nativeWindowsSavePath = "";
    [ObservableProperty] private string receiptPath = "";
    [ObservableProperty] private string saveIdentityHex = "";
    [ObservableProperty] private string workshopRequestPath = "";
    [ObservableProperty] private string workshopEconomyPath = "";
    [ObservableProperty] private string workshopPlanJson = JsonSerializer.Serialize(new WorkshopPlanAuthoring(), HookProjectProfile.JsonOptions);
    [ObservableProperty] private string arcanaRecordPath = "";
    [ObservableProperty] private string arcanaSaveHash = "";
    [ObservableProperty] private string arcanaPackHash = "";
    [ObservableProperty] private string arcanaAssetsJson = "[]";
    [ObservableProperty] private string hooksSourcePath = "";
    [ObservableProperty] private string pythonPath = "python3";
    [ObservableProperty] private string vbfPath = "";
    [ObservableProperty] private string textValidatorPath = "";
    [ObservableProperty] private string languageDestination = "";
    [ObservableProperty] private string languageRecipeJson = "{\n  \"locale\": \"pt-BR\",\n  \"display_name\": \"Português (Brasil)\",\n  \"pack_version\": \"1.0.0\",\n  \"resources\": {}\n}";

    public string CatalogReport => string.Join("\n", HookAbilityRegistry.Abilities.Select(a => $"#{a.DefaultId} / 0x{a.Word(a.DefaultId):X4} / {a.Key} / {a.Name} / {a.Definition}"));
    public string ArcanaCatalogReport => string.Join("\n", ArcanaAssetAuthoring.Catalog.Select(c => $"#{c.Id} · {c.Key} · {c.Name}\n{c.Description}\n{c.Asset}\n"));
    public string ElementRegistryReport
    {
        get
        {
            try
            {
                var registry=new ElementalPackDocument(Encoding.UTF8.GetBytes(ElementalJson)).Elements;
                var nativeOrder=new byte[]{1,2,4,8,16,128,32,64};var rows=nativeOrder.Select(bit=>registry.Single(e=>e.NativeBit==bit)).ToList();
                var text=new List<string>();for(int i=0;i<rows.Count;i++)text.Add($"{i+1}. {rows[i].Label} · {rows[i].Key} · 0x{rows[i].NativeBit:X2}");
                var external=registry.Where(e=>e.NativeBit==0).ToArray();
                for(int i=0;i<2;i++)text.Add(i>=external.Length?$"{i+9}. {Strings.U_HookIntegration_Unavailable}":$"{i+9}. {external[i].Label} · {external[i].Key}");
                text.Add(prepared.ContainsKey("elemental")?Strings.U_HookIntegration_PackageAdmitted:Strings.U_HookIntegration_NotAdmitted);
                return string.Join("\n",text);
            }
            catch(Exception ex){return ex.Message;}
        }
    }
    public string AscensionReport => string.Join("\n", WorkshopProductionInspector.AscensionRecipes.Select(r => $"{HookAbilityRegistry.Get(r.Key).Name}: {r.Gil:N0} Gil; {Strings.CommonOwner}: {r.OwnerMinimum}–{r.OwnerMaximum}; {Strings.CommonType}: {r.GearKind}; " +
        string.Join(", ", r.Items.Select(i => $"item #{i.Key} × {i.Value}")))) + "\n" + Strings.U_HookIntegration_PaidBoundary;
    public bool CanExportPackage => OnlyModEnabled && packagePreview != null && prepared.Count != 0;
    public string ProjectModeLabel => OnlyModEnabled ? "OnlyMod" : "Vanilla";
    private HookProjectProfile CurrentProject => project with { Mode = OnlyModEnabled ? HookProjectMode.OnlyMod : HookProjectMode.Vanilla };
    private void InitializeIntegration()
    {
        Rewards.Changed += () => { prepared.Remove("rewards"); InvalidatePackage(); };
        string suffix = OperatingSystem.IsWindows() ? ".exe" : "";
        string validator = Path.Combine(AppContext.BaseDirectory,"tools","hook-contracts","hook-contracts" + suffix);
        if (File.Exists(validator)) ValidatorPath = validator;
        string textValidator = Path.Combine(AppContext.BaseDirectory,"tools","hook-contracts","TextLanguageValidate" + suffix);
        if (File.Exists(textValidator)) TextValidatorPath = textValidator;
        ReloadProjectProfile();
    }
    private void ReloadProjectProfile()
    {
        if (loadingProfile || string.IsNullOrWhiteSpace(SourceRoot)) return;
        loadingProfile = true;
        try
        {
            project = HookProjectProfile.Load(SourceRoot, MetadataRoot);
            profileHash = HookExportTransaction.CurrentHash(HookProjectProfile.ProfilePath(SourceRoot, MetadataRoot));
            OnlyModEnabled = project.Mode == HookProjectMode.OnlyMod;
            RegistryJson = JsonSerializer.Serialize(project.AbilityIds, HookProjectProfile.JsonOptions);
            Locale = project.BankLocales.GetValueOrDefault("autoability", "new_uspc");
        }
        catch (Exception ex) { project = new(); OnlyModEnabled = false; Status = ex.Message; }
        finally { loadingProfile = false; }
    }
    partial void OnOnlyModEnabledChanged(bool value)
    {
        if(!value)
        {
            BuildLanguageCommand.Cancel();PrepareAbilitiesCommand.Cancel();AdmitElementalCommand.Cancel();
            InspectProductionCommand.Cancel();InspectArcanaCommand.Cancel();PreviewWorkshopPlanCommand.Cancel();
        }
        prepared.Clear(); ResetImport(); ResetWorkshop(); InvalidatePackage();
        OnPropertyChanged(nameof(ElementRegistryReport));
        OnPropertyChanged(nameof(ProjectModeLabel)); OnPropertyChanged(nameof(CanExportLanguagePack));
    }
    private void InvalidatePackage() { packagePreview = null; PackageReport = ""; OnPropertyChanged(nameof(CanExportPackage)); }
    private void InvalidateGroup(string key) { prepared.Remove(key); ConsumerReport = ""; InvalidatePackage(); OnPropertyChanged(nameof(ElementRegistryReport)); }
    partial void OnRegistryJsonChanged(string value) { InvalidateGroup("abilities"); InvalidateGroup("preset"); InvalidateGroup("elemental"); }
    partial void OnPresetJsonChanged(string value) => InvalidateGroup("preset");
    partial void OnCommandBankPathChanged(string value) => InvalidateGroup("preset");
    partial void OnElementalJsonChanged(string value) { InvalidateGroup("elemental"); InvalidateGroup("preferences"); }
    partial void OnElementalInputsJsonChanged(string value) => InvalidateGroup("elemental");
    partial void OnAffinityPreviewJsonChanged(string value) => AffinityReport = "";
    partial void OnValidatorPathChanged(string value) { prepared.Clear(); InvalidatePackage(); }
    partial void OnExecutablePathChanged(string value) { ConsumerReport = ""; InvalidatePackage(); }
    partial void OnF7JsonChanged(string value) => InvalidateGroup("preferences");
    partial void OnScanIniChanged(string value) => InvalidateGroup("preferences");
    partial void OnArcanaAssetsJsonChanged(string value) => InvalidateGroup("arcana-assets");
    private void InvalidateLanguage() { LanguageBuild = null; LanguageReport = ""; InvalidateGroup("language"); OnPropertyChanged(nameof(CanExportLanguagePack)); }
    partial void OnLanguageRecipeJsonChanged(string value) => InvalidateLanguage();
    partial void OnVbfPathChanged(string value) => InvalidateLanguage();
    partial void OnHooksSourcePathChanged(string value) => InvalidateLanguage();
    partial void OnTextValidatorPathChanged(string value) => InvalidateLanguage();
    partial void OnPythonPathChanged(string value) => InvalidateLanguage();
    partial void OnLanguageDestinationChanged(string value) => InvalidateLanguage();

    [RelayCommand] private void SaveProjectProfile() => Run(() =>
    {
        if (OnlyModEnabled)
        {
            HookJson.RejectDuplicates(Encoding.UTF8.GetBytes(RegistryJson));
            var ids = JsonSerializer.Deserialize<Dictionary<string,int>>(RegistryJson) ?? throw new InvalidDataException("Missing ID mapping.");
            _ = HookAbilityRegistry.Mapping(ids);
            var locales = new Dictionary<string,string>(project.BankLocales,StringComparer.Ordinal) { ["autoability"] = Locale };
            project = CurrentProject with { AbilityIds = ids, BankLocales = locales };
        }
        else project = project with { Mode = HookProjectMode.Vanilla };
        project.Save(SourceRoot, MetadataRoot, profileHash);
        profileHash = HookExportTransaction.CurrentHash(HookProjectProfile.ProfilePath(SourceRoot, MetadataRoot));
        Status = Strings.U_HookIntegration_ProfileSaved;
    });
    [RelayCommand] private void CheckConsumer() => Run(() =>
    { CurrentProject.Consumer.InspectExecutable(ExecutablePath); ConsumerReport = CurrentProject.Consumer.Contract + "\n" + CurrentProject.Consumer.ExeSha256 + "\n" + Strings.U_HookIntegration_DesiredOnly; });

    private void Put(string group, IEnumerable<HookArtifact> artifacts, IEnumerable<HookSourceSnapshot>? sources = null)
    { prepared[group] = (artifacts.ToArray(), sources?.ToArray() ?? Array.Empty<HookSourceSnapshot>()); InvalidatePackage(); OnPropertyChanged(nameof(ElementRegistryReport)); Status = Strings.U_HookIntegration_Prepared; }

    [RelayCommand(IncludeCancelCommand = true)] private Task PrepareAbilitiesAsync(CancellationToken token) => RunAsync(async () =>
    {
        CurrentProject.Require("spira.v1");
        string ability = Path.Combine(SourceRoot, Locale, "battle/kernel/a_ability.bin"), price = Path.Combine(SourceRoot, Locale, "battle/kernel/arms_rate.bin");
        if (!File.Exists(price)) price = Path.Combine(SourceRoot, "jppc/battle/kernel/arms_rate.bin");
        HookJson.RejectDuplicates(Encoding.UTF8.GetBytes(RegistryJson));
        var ids = JsonSerializer.Deserialize<Dictionary<string,int>>(RegistryJson) ?? throw new InvalidDataException("Invalid ability mapping.");
        string selectedLocale=Locale,selectedRegistry=RegistryJson;
        byte[] originalAbilities=ModAuthoringFiles.Read(ability,65535),originalPrices=ModAuthoringFiles.Read(price,100000);
        var sourceProofs=new[]{new HookSourceSnapshot(Path.GetFullPath(ability),ModAuthoringFiles.Sha256(originalAbilities),originalAbilities.Length),
            new HookSourceSnapshot(Path.GetFullPath(price),ModAuthoringFiles.Sha256(originalPrices),originalPrices.Length)};
        var output = HookAbilityBankAuthoring.Generate(originalAbilities, originalPrices, Locale, ids);
        var admitted = await HookAbilityBankAuthoring.AdmitAsync(output,Locale,ids,new(ValidatorPath),token);
        if(!OnlyModEnabled||selectedLocale!=Locale||selectedRegistry!=RegistryJson)throw new InvalidDataException("Ability preview changed during native admission.");
        foreach(var source in sourceProofs)if(HookExportTransaction.Snapshot(source.Path)!=source)throw new IOException("Ability source changed during native admission.");
        string priceLocale = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(price))))!;
        Put("abilities", new[]
        {
            new HookArtifact($"data/mods/ffx_ps2/ffx/master/{Locale}/battle/kernel/a_ability.bin", output.Abilities,"autoability",Locale,"spira.v1",ability),
            new HookArtifact($"data/mods/ffx_ps2/ffx/master/{priceLocale}/battle/kernel/arms_rate.bin", output.Prices,"prices",priceLocale,"spira.v1",price),
            new HookArtifact("ability-ids.json",Encoding.UTF8.GetBytes(RegistryJson),"editor-mapping",null,"spira.v1","Editor")
        },sourceProofs);
        PreviewText = output.Report + "\n" + admitted.Report;
    });
    [RelayCommand] private void PreparePreset() => Run(() =>
    {
        var preset = VanguardPreset.Parse(Encoding.UTF8.GetBytes(PresetJson));
        var selectedMapping = JsonSerializer.Deserialize<Dictionary<string,int>>(RegistryJson) ?? throw new InvalidDataException("Missing registry mapping.");
        var registry = HookAbilityRegistry.Mapping(selectedMapping);
        foreach (var pair in preset.AbilityIds)
            if (!registry.TryGetValue(pair.Key,out int id) || id != pair.Value) throw new InvalidDataException("Preset IDs differ from the shared project registry.");
        preset = preset with { AbilityIds = selectedMapping };
        byte[]? commands = null, abilities = null, prices = null; var sources = new List<HookSourceSnapshot>();
        if (preset.Commands.Values.Any(binding => binding.Command != 0))
        {
            commands = ModAuthoringFiles.Read(CommandBankPath, 8 << 20); sources.Add(new(Path.GetFullPath(CommandBankPath),ModAuthoringFiles.Sha256(commands),commands.Length));
            if (!prepared.TryGetValue("abilities", out var banks)) throw new InvalidDataException("Prepare the matching ability bank before command bindings.");
            abilities = banks.Artifacts.Single(a => a.Kind == "autoability").Bytes; prices = banks.Artifacts.Single(a => a.Kind == "prices").Bytes;
        }
        string ini = preset.ExportIni(CurrentProject, commands, abilities, prices, Locale);
        Put("preset", new[] { new HookArtifact("hook-profile.ini", Encoding.UTF8.GetBytes(ini), "ini-fragment", null, "vanguard.v1", "Editor") }, sources);
    });
    [RelayCommand] private void PrepareRewards() => Run(() =>
    { CurrentProject.Require("monster-rewards.v1"); Put("rewards", new[] { new HookArtifact("monster-rewards-v1.tsv", Rewards.ExportBytes(), "reward-rates", null, "monster-rewards.v1", "Editor") }); });
    [RelayCommand] private void SaveRewardRates() => Run(() => { Rewards.Save(CurrentProject); Status = Strings.U_HookIntegration_DesiredOnly; });
    private ElementalInput[] ElementInputs()
    { HookJson.RejectDuplicates(Encoding.UTF8.GetBytes(ElementalInputsJson)); return JsonSerializer.Deserialize<ElementalInput[]>(ElementalInputsJson, HookProjectProfile.JsonOptions) ?? throw new InvalidDataException("Missing Elemental sources."); }
    [RelayCommand] private void RefreshElementalFingerprints() => Run(() =>
    { CurrentProject.Require("mod007.registry.v1"); ElementalJson = Encoding.UTF8.GetString(new ElementalPackDocument(Encoding.UTF8.GetBytes(ElementalJson)).RefreshFingerprints(ElementInputs()).Serialize()); });
    [RelayCommand] private void PreviewAffinity() => Run(() =>
    { CurrentProject.Require("mod007.affinity.v1");AffinityReport=ElementalPreviewRequest.Parse(Encoding.UTF8.GetBytes(AffinityPreviewJson)).Calculate(); });
    private async Task RunAsync(Func<Task> action)
    { try { await action(); } catch (Exception ex) { Status = string.Format(Strings.U_HookMods_Failed, ex.Message); } }
    [RelayCommand(IncludeCancelCommand = true)] private Task AdmitElementalAsync(CancellationToken token) => RunAsync(async () =>
    {
        string manifest = ElementalJson, inputs = ElementalInputsJson;
        var admission = await new ElementalPackDocument(Encoding.UTF8.GetBytes(manifest)).AdmitAsync(CurrentProject, ElementInputs(), new(ValidatorPath), token);
        if (manifest != ElementalJson || inputs != ElementalInputsJson || !OnlyModEnabled) throw new InvalidDataException("Elemental preview changed during admission.");
        Put("elemental", admission.Artifacts, admission.Sources); ConsumerReport = admission.Native.Report;
    });
    [RelayCommand] private void PreparePreferences() => Run(() =>
    {
        CurrentProject.Require("mod007.registry.v1"); var elements = new ElementalPackDocument(Encoding.UTF8.GetBytes(ElementalJson)).Elements;
        byte[] f7 = F7ElementPreferencesCodec.ValidateAndRoundTrip(Encoding.UTF8.GetBytes(F7Json), elements);
        var scan = ScanElementPreferences.Parse(ScanIni, elements);
        Put("preferences", new[] { new HookArtifact("f7-preset.json", f7,"f7-desired",null,"mod007.registry.v1","Editor"),
            new HookArtifact("hook-profile.ini",Encoding.UTF8.GetBytes(scan.Serialize()),"ini-fragment",null,"mod007.registry.v1","Editor") });
    });
    [RelayCommand(IncludeCancelCommand = true)] private Task InspectProductionAsync(CancellationToken token) => RunAsync(async () =>
    {
        CurrentProject.Require("workshop.plan.v5"); byte[]? native = string.IsNullOrWhiteSpace(NativeSaveImagePath) ? null : ModAuthoringFiles.Read(NativeSaveImagePath, 0x6900);
        var inspection = WorkshopProductionInspector.Read(ProductionRecordPath, native, NativeWindowsSavePath);
        WorkshopText = inspection.Report;
        if (inspection.State == null) return;
        byte[]? ledger = string.IsNullOrWhiteSpace(ReceiptPath) ? null : ModAuthoringFiles.Read(ReceiptPath, 784);
        if (ledger?.Length == 784)
        {
            var receipt = WorkshopProductionInspector.Read(ReceiptPath, null, NativeWindowsSavePath);
            if (receipt.Format != "FFXASCP1" || Convert.ToHexString(ledger.AsSpan(40,32)).ToLowerInvariant() != ModAuthoringFiles.Sha256(inspection.State))
                throw new InvalidDataException("Receipt envelope belongs to another Workshop state.");
            ledger = ledger.AsSpan(104).ToArray();
        }
        var result = await WorkshopProductionInspector.AdmitStateAsync(inspection.State, new(ValidatorPath), token, ledger,
            ledger == null ? null : Convert.FromHexString(SaveIdentityHex),
            string.IsNullOrWhiteSpace(WorkshopRequestPath) ? null : ModAuthoringFiles.Read(WorkshopRequestPath,62),
            string.IsNullOrWhiteSpace(WorkshopEconomyPath) ? null : ModAuthoringFiles.Read(WorkshopEconomyPath,188));
        WorkshopText += "\n" + result.Report;
        if(HookExportTransaction.CurrentHash(ProductionRecordPath)!=inspection.Sha256)throw new IOException("Production record changed during inspection.");
    });
    [RelayCommand(IncludeCancelCommand = true)] private Task PreviewWorkshopPlanAsync(CancellationToken token) => RunAsync(async () =>
    {
        CurrentProject.Require("workshop.plan.v5");
        byte[]? native = string.IsNullOrWhiteSpace(NativeSaveImagePath) ? null : ModAuthoringFiles.Read(NativeSaveImagePath,0x6900);
        var inspection = WorkshopProductionInspector.Read(ProductionRecordPath,native,NativeWindowsSavePath);
        if (inspection.Pending || inspection.State == null) throw new InvalidDataException("Resolve a pending production transaction with its host before planning.");
        var plan = WorkshopPlanAuthoring.Parse(Encoding.UTF8.GetBytes(WorkshopPlanJson)).Encode();
        var result = await WorkshopProductionInspector.AdmitStateAsync(inspection.State,new(ValidatorPath),token,request:plan.Request,economy:plan.Economy);
        WorkshopText = inspection.Report + "\n" + result.Report + "\n" + Strings.U_HookIntegration_PaidBoundary;
    });
    [RelayCommand(IncludeCancelCommand = true)] private Task InspectArcanaAsync(CancellationToken token) => RunAsync(async () =>
    { CurrentProject.Require("arcana.compiled.v1"); WorkshopText = await ArcanaRecordInspector.InspectAsync(ArcanaRecordPath, ArcanaSaveHash, ArcanaPackHash, new(ValidatorPath), token); });
    [RelayCommand] private void PrepareArcanaAssets() => Run(() =>
    { var assets = ArcanaAssetAuthoring.Prepare(Encoding.UTF8.GetBytes(ArcanaAssetsJson),CurrentProject); Put("arcana-assets",assets.Artifacts,assets.Sources); });
    [RelayCommand(IncludeCancelCommand = true)] private Task BuildLanguageAsync(CancellationToken token) => RunAsync(async () =>
    {
        InvalidateLanguage(); string recipe = LanguageRecipeJson;
        string Stamp()=>ModAuthoringFiles.Sha256(Encoding.UTF8.GetBytes(string.Join('\0',SourceRoot,HooksSourcePath,PythonPath,VbfPath,TextValidatorPath,LanguageDestination,LanguageRecipeJson)));
        string requestStamp=Stamp();
        var result = await TextLanguagePackageExporter.BuildAsync(CurrentProject, PythonPath, HooksSourcePath, VbfPath, Encoding.UTF8.GetBytes(recipe), LanguageDestination, TextValidatorPath, token);
        if (!OnlyModEnabled||requestStamp!=Stamp()) throw new InvalidDataException("Language inputs changed during build; reload the completed isolated output before using it.");
        string pack=Path.Combine(result.Directory,"languages","pt-BR"),manifestPath=Path.Combine(pack,"manifest.json");
        byte[] manifest=ModAuthoringFiles.Read(manifestPath,1<<20);
        if(ModAuthoringFiles.Sha256(manifest)!=result.ManifestHash)throw new IOException("Language manifest changed after admission.");
        using var document=JsonDocument.Parse(manifest);var artifacts=new List<HookArtifact>();var sources=new List<HookSourceSnapshot>();
        void Add(string relative,byte[] bytes)
        {
            string source=HookExportTransaction.Resolve(pack,relative);
            artifacts.Add(new("languages/pt-BR/"+relative,bytes,"text-language", "pt-BR","ffx.text-locale.api2",source));
            sources.Add(new(source,ModAuthoringFiles.Sha256(bytes),bytes.Length,pack));
        }
        Add("manifest.json",manifest);
        foreach(var resource in document.RootElement.GetProperty("resources").EnumerateArray())
        {
            string relative=resource.GetProperty("path").GetString()!;byte[] bytes=ModAuthoringFiles.Read(HookExportTransaction.Resolve(pack,relative),64<<20);
            if(bytes.Length!=resource.GetProperty("size").GetInt32()||ModAuthoringFiles.Sha256(bytes)!=resource.GetProperty("sha256").GetString())
                throw new IOException("Language resource changed after admission.");
            Add(relative,bytes);
        }
        Put("language",artifacts,sources);LanguageBuild = result; LanguageReport = result.Report; OnPropertyChanged(nameof(CanExportLanguagePack));
    });

    private (HookArtifact[] Artifacts, HookSourceSnapshot[] Sources) PackageInputs()
    {
        if (!string.IsNullOrWhiteSpace(SourceRoot) &&
            (ModAuthoringFiles.IsWithin(Destination,SourceRoot) || ModAuthoringFiles.IsWithin(SourceRoot,Destination)))
            throw new InvalidDataException("Choose an export outside the original project tree.");
        if (prepared.Count == 0) throw new InvalidDataException("Prepare at least one explicitly selected Hook feature.");
        if(prepared.ContainsKey("preset"))
        {
            var preset=VanguardPreset.Parse(Encoding.UTF8.GetBytes(PresetJson));
            void Need(bool required,string group) {if(required&&!prepared.ContainsKey(group))throw new InvalidDataException($"Enabled consumer requires a prepared and validated {group} artifact.");}
            Need(preset.SpiraEnabled||preset.AeonAscensionEnabled||HookAbilityRegistry.Abilities.Where(a=>a.DefaultId<148).Any(a=>preset.Rules.GetValueOrDefault(a.Key)),"abilities");
            Need(preset.ElementalCoreEnabled||preset.ElementalTacticsEnabled||preset.ElementalGravityEnabled||preset.ElementalMagicBdlEnabled,"elemental");
            Need(preset.MonsterRewardsEnabled,"rewards");Need(preset.TextLocale=="pt-BR","language");
        }
        var artifacts = prepared.Values.SelectMany(p => p.Artifacts).ToList();
        var ini = artifacts.Where(a => a.Kind == "ini-fragment").ToArray(); artifacts.RemoveAll(a => a.Kind == "ini-fragment");
        if (ini.Length != 0) artifacts.Add(new("hook-profile.ini",Encoding.UTF8.GetBytes(string.Join("\n",ini.Select(a => Encoding.UTF8.GetString(a.Bytes)))),"configuration",null,null,"Editor"));
        string recipe = "# RT2 roteiro — não executado\n\n" +
            "1. Use um save descartável e faça inventário/SHA-256 da DLL, executável, dados, configuração e sidecars.\n" +
            "2. Compare OFF/ON de um consumidor por vez, com a mesma entrada e controle vanilla.\n" +
            "3. F7: aplicar/restaurar Custom1/2 e externos; Scan deve coincidir com afinidades observadas.\n" +
            "4. Recompensas: normal/Overkill, individual/geral, depois bônus vanilla.\n" +
            "5. Workshop/Ascension: compra/remoção, invocar/reinvocar, save/reload/checkpoint; sem fabricar posse.\n" +
            "6. Nul misto e duração por ações; idiomas: menu/batalha/eventos e áudio independente.\n" +
            "7. Registre esperado/observado, hashes e limitações. Nenhum jogo é iniciado pelo exportador.\n";
        artifacts.Add(new("rt2-plan.md",Encoding.UTF8.GetBytes(recipe),"rt2-script",null,null,"Editor"));
        string pinnedInputs="\n## Entradas fixadas deste pacote\n\nExpected executable SHA-256: `"+CurrentProject.Consumer.ExeSha256+"`.\nHook contract revision: `"+CurrentProject.Consumer.Revision+"`.\n\n"+
            string.Join("\n",artifacts.Where(a=>a.RelativePath!="rt2-plan.md").Select(a=>$"- `{a.RelativePath}`: `{ModAuthoringFiles.Sha256(a.Bytes)}`"))+"\n";
        artifacts[^1]=artifacts[^1] with {Bytes=Encoding.UTF8.GetBytes(recipe+pinnedInputs)};
        var sources = prepared.Values.SelectMany(p => p.Sources).Distinct().ToList();
        if (!string.IsNullOrWhiteSpace(SourceRoot))
            for (int i=0;i<sources.Count;i++) if(ModAuthoringFiles.IsWithin(sources[i].Path,SourceRoot)) sources[i]=sources[i] with {SourceRoot=Path.GetFullPath(SourceRoot)};
        if (!string.IsNullOrWhiteSpace(ExecutablePath))
        {
            CurrentProject.Consumer.InspectExecutable(ExecutablePath);
            sources.Add(HookExportTransaction.Snapshot(ExecutablePath));
        }
        return (artifacts.ToArray(),sources.ToArray());
    }
    [RelayCommand] private void PreviewPackage() => Run(() =>
    { var inputs = PackageInputs(); packagePreview = HookExportTransaction.Preview(CurrentProject,Destination,inputs.Sources,inputs.Artifacts); PackageReport = packagePreview.Report; OnPropertyChanged(nameof(CanExportPackage)); });
    [RelayCommand] private void ExportPackage() => Run(() =>
    {
        if (!CanExportPackage) throw new InvalidDataException("Review the current package preview first.");
        var inputs = PackageInputs(); HookExportTransaction.Export(CurrentProject,Destination,inputs.Sources,inputs.Artifacts,packagePreview!.Fingerprint);
        InvalidatePackage(); Status = string.Format(Strings.U_HookMods_Exported,Path.GetFullPath(Destination));
    });
}
