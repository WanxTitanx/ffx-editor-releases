using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Mods;
using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Modules.HookModAuthoring;
using FFXProjectEditor.Utils.Encoding;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

public sealed class LinuxSymlinkFactAttribute : FactAttribute
{
    public LinuxSymlinkFactAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Exercises Linux symlink semantics."; }
}

public class HookModAuthoringTests
{
    internal static (byte[] Ability, byte[] Price) Kernel(int count = 148, bool numeric = false)
    {
        var rows = new byte[count * 108];
        using var strings = new MemoryStream();
        strings.WriteByte(0);
        for (int i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(rows.AsSpan(i * 108), checked((ushort)strings.Length));
            var identity = Mod002Manifest.Default().Abilities.SingleOrDefault(a => a.Id == i);
            strings.Write(identity == null ? TextBinary_Util.EncodeScriptToBytes($"Name {i}", FfxEncoding.UsDecoder) :
                HookAbilityRegistry.CanonicalName(HookAbilityRegistry.Get(identity.Key), numeric));
            strings.WriteByte(0);
            BinaryPrimitives.WriteUInt16LittleEndian(rows.AsSpan(i * 108 + 8), checked((ushort)strings.Length));
            strings.Write(TextBinary_Util.EncodeScriptToBytes($"Description {i}", FfxEncoding.UsDecoder));
            strings.WriteByte(0);
            if (i < 134) rows[i * 108 + 0x67] = 0xA5; // Unknown vanilla bytes must survive.
        }
        return (EntryListFile.Pack(108, (short)count, rows, strings.ToArray()),
            EntryListFile.Pack(4, (short)count, Enumerable.Range(0, count).SelectMany(BitConverter.GetBytes).ToArray(), new byte[] { 0xAD, 0xDE }));
    }

    [Fact]
    public void VanillaAndModProfiles_AreIndependentAndOffByDefault()
    {
        var model = new HookModAuthoring_DataModel("/not-read-at-construction");
        Assert.False(model.Mod002Enabled); Assert.False(model.Mod004Enabled);
        Assert.False(model.Mod005Enabled); Assert.False(model.Mod006Enabled);
        Assert.False(model.HasSession); Assert.False(model.CanExport);
        model.Mod005Enabled = true;
        Assert.False(model.WorkshopEnabled);
        model.OnlyModEnabled = true;
        Assert.True(model.WorkshopEnabled); Assert.False(model.Mod004Enabled);
        Assert.False(model.CanExportLanguagePack);
        var (ability, price) = Kernel(134);
        var table = AutoAbility_File.Read(ability, price);
        Assert.Equal(134, table.Entries.Count);
        Assert.Equal(ability, AutoAbility_File.WriteAbilities(table));
        Assert.Equal(price, AutoAbility_File.WritePrices(table));
    }

    [Theory]
    [InlineData("new_uspc")][InlineData("inpc")][InlineData("new_depc")]
    [InlineData("new_frpc")][InlineData("new_itpc")][InlineData("new_sppc")]
    [InlineData("jppc")][InlineData("new_jppc")][InlineData("new_chpc")][InlineData("new_krpc")]
    public void NoEditAndPriceOnlyEdit_PreserveEveryLocaleByteForByte(string locale)
    {
        var (ability, price) = Kernel(numeric: Array.IndexOf(Mod002Authoring.Locales, locale) >= 6);
        var session = new Mod002Authoring(ability, price, Mod002Manifest.Default(), locale);
        var preview = session.Preview(session.Rows);
        Assert.Equal(ability, preview.Abilities); Assert.Equal(price, preview.Prices);
        var edits = session.Rows.ToArray(); edits[0] = edits[0] with { Price = 42 };
        var changed = session.Preview(edits);
        Assert.Equal(ability, changed.Abilities);
        var expectedPrice = (byte[])price.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(expectedPrice.AsSpan(20 + edits[0].Id * 4), 42);
        Assert.Equal(expectedPrice, changed.Prices);
    }

    [Fact]
    public void EditingMappedText_AppendsPoolAndChangesOnlyItsRefsAndPrice()
    {
        var (ability, price) = Kernel();
        var session = new Mod002Authoring(ability, price, Mod002Manifest.Default(), "new_uspc");
        var edits = session.Rows.ToArray();
        edits[0] = edits[0] with { Description = "Changed description", Price = 73 };
        var preview = session.Preview(edits);
        int row = 20 + edits[0].Id * 108;
        for (int i = 0; i < ability.Length; i++)
            if (i != row && i != row + 1 && i != row + 8 && i != row + 9) Assert.Equal(ability[i], preview.Abilities[i]);
        var expectedPrices = (byte[])price.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(expectedPrices.AsSpan(20 + edits[0].Id * 4), 73);
        Assert.Equal(expectedPrices, preview.Prices);
        Assert.Equal(ability, session.Preview(session.Rows).Abilities);
        Assert.Contains("Changed description", preview.Report);
    }

    [Fact]
    public void ManifestRemap_UsesProvidedIdsAndRejectsCollisionsAndUnknownKeys()
    {
        var json = JsonNode.Parse(Mod002Manifest.Default().Json)!;
        json["abilities"]![0]!["default_id"] = 175;
        var (ability, price) = Kernel(176);
        BinaryPrimitives.WriteUInt16LittleEndian(ability.AsSpan(20 + 175 * 108), BinaryPrimitives.ReadUInt16LittleEndian(ability.AsSpan(20 + 135 * 108)));
        var remapped = Mod002Manifest.Parse(json.ToJsonString());
        var session = new Mod002Authoring(ability, price, remapped, "new_uspc");
        Assert.Contains(session.Rows, r => r.Id == 175); Assert.DoesNotContain(session.Rows, r => r.Id == 135);
        json["abilities"]![0]!["default_id"] = 136;
        Assert.Throws<InvalidDataException>(() => Mod002Manifest.Parse(json.ToJsonString()));
        json["abilities"]![0]!["default_id"] = 175;
        json["abilities"]![0]!["key"] = "made_up_effect";
        Assert.Throws<InvalidDataException>(() => Mod002Manifest.Parse(json.ToJsonString()));
    }

    [Fact]
    public void MissingIds_NativePayload_AsianText_UnsupportedCharacters_AreRefused()
    {
        var (vanilla, vanillaPrice) = Kernel(134);
        Assert.Throws<InvalidDataException>(() => new Mod002Authoring(vanilla, vanillaPrice, Mod002Manifest.Default(), "new_uspc"));
        var (ability, price) = Kernel();
        ability[20 + 135 * 108 + 0x10] = 1;
        Assert.Throws<InvalidDataException>(() => new Mod002Authoring(ability, price, Mod002Manifest.Default(), "new_uspc"));
        ability[20 + 135 * 108 + 0x10] = 0;
        var (asianAbility, asianPrice) = Kernel(numeric: true);
        var asian = new Mod002Authoring(asianAbility, asianPrice, Mod002Manifest.Default(), "new_jppc");
        var edits = asian.Rows.ToArray(); edits[0] = edits[0] with { Name = "US replacement" };
        Assert.Throws<InvalidDataException>(() => asian.Preview(edits));
        var western = new Mod002Authoring(ability, price, Mod002Manifest.Default(), "new_uspc");
        edits = western.Rows.ToArray(); edits[0] = edits[0] with { Name = "ãõ" };
        Assert.Throws<InvalidDataException>(() => western.Preview(edits));
        Assert.Contains('ã', Mod006Readiness.MissingUsCharacters("ação"));
        Assert.Contains('õ', Mod006Readiness.MissingUsCharacters("opções"));
        Assert.False(Mod006Readiness.CanExportPack);
    }

    [Theory]
    [InlineData("text-offset")][InlineData("price-offset")][InlineData("short-price")]
    public void MalformedInput_CannotBecomeAnExport(string fault)
    {
        var (ability, price) = Kernel();
        if (fault == "text-offset") BinaryPrimitives.WriteUInt16LittleEndian(ability.AsSpan(20 + 135 * 108), 0xFFFF);
        if (fault == "price-offset") BinaryPrimitives.WriteInt32LittleEndian(price.AsSpan(16), 0);
        if (fault == "short-price") price = new byte[10];
        Assert.Throws<InvalidDataException>(() => new Mod002Authoring(ability, price, Mod002Manifest.Default(), "new_uspc"));
    }

    [Fact]
    public void TextPoolOverflowAndDuplicateManifestProperties_AreRefused()
    {
        var (ability, price) = Kernel();
        var session = new Mod002Authoring(ability, price, Mod002Manifest.Default(), "new_uspc");
        var edits = session.Rows.ToArray(); edits[0] = edits[0] with { Name = new string('A', 65536) };
        Assert.Throws<InvalidDataException>(() => session.Preview(edits));
        string json = Mod002Manifest.Default().Json;
        json = json.Insert(json.IndexOf('{') + 1, "\"schema\":1,");
        Assert.Throws<InvalidDataException>(() => Mod002Manifest.Parse(json));
    }

    [LinuxSymlinkFact]
    public void LinkedDestinationCannotAliasTheSource()
    {
        string root = Path.Combine(Path.GetTempPath(), "ffx-mod002-link-" + Guid.NewGuid().ToString("N"));
        try
        {
            string source = Path.Combine(root, "source"), alias = Path.Combine(root, "alias");
            string a = Path.Combine(source, "new_uspc/battle/kernel/a_ability.bin"), p = Path.Combine(source, "jppc/battle/kernel/arms_rate.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(a)!); Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            var (ability, price) = Kernel(); File.WriteAllBytes(a, ability); File.WriteAllBytes(p, price);
            Directory.CreateSymbolicLink(alias, source);
            var session = new Mod002Authoring(ability, price, Mod002Manifest.Default(), "new_uspc");
            var preview = session.Preview(session.Rows);
            Assert.Throws<InvalidDataException>(() => session.Export(source, a, p, Path.Combine(alias, "package"), session.Rows, preview.Fingerprint));
            Assert.False(Directory.Exists(Path.Combine(source, "package")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void Export_IsSeparateWithBackupAndRefusesStaleSourcePreviewOrExistingDestination()
    {
        string root = Path.Combine(Path.GetTempPath(), "ffx-mod002-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string source = Path.Combine(root, "source"), target = Path.Combine(root, "package");
            string a = Path.Combine(source, "new_uspc/battle/kernel/a_ability.bin"), p = Path.Combine(source, "jppc/battle/kernel/arms_rate.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(a)!); Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            var (ability, price) = Kernel(); File.WriteAllBytes(a, ability); File.WriteAllBytes(p, price);
            var session = new Mod002Authoring(ability, price, Mod002Manifest.Default(), "new_uspc");
            var edits = session.Rows.ToArray(); edits[0] = edits[0] with { Price = 42 };
            var preview = session.Preview(edits);
            Assert.Throws<InvalidDataException>(() => session.Export(source, a, p, Path.Combine(source, "package"), edits, preview.Fingerprint));
            Assert.Throws<InvalidDataException>(() => session.Export(source, a, p, target, edits, "stale"));
            File.AppendAllText(a, "changed");
            Assert.Throws<InvalidDataException>(() => session.Export(source, a, p, target, edits, preview.Fingerprint));
            File.WriteAllBytes(a, ability);
            session.Export(source, a, p, target, edits, preview.Fingerprint);
            Assert.Equal(ability, File.ReadAllBytes(a)); Assert.Equal(price, File.ReadAllBytes(p));
            Assert.Equal(ability, File.ReadAllBytes(Path.Combine(target, "backup/a_ability.bin")));
            Assert.Equal(price, File.ReadAllBytes(Path.Combine(target, "backup/arms_rate.bin")));
            Assert.Equal(preview.Prices, File.ReadAllBytes(Path.Combine(target, "data/mods/ffx_ps2/ffx/master/jppc/battle/kernel/arms_rate.bin")));
            Assert.Throws<IOException>(() => session.Export(source, a, p, target, edits, preview.Fingerprint));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    internal static (byte[] Native, byte[] State) Workshop()
    {
        var native = new byte[0x6900]; var state = new byte[16260];
        BinaryPrimitives.WriteUInt32LittleEndian(state, 1);
        BinaryPrimitives.WriteUInt64LittleEndian(state.AsSpan(12), 4);
        for (int i = 0; i < 200; i++) BinaryPrimitives.WriteUInt16LittleEndian(state.AsSpan(36 + i * 80 + 78), 0xFF);
        const int p = 36;
        state[p + 2] = 1; state[p + 11] = 4; state[p + 30] = 2; state[p + 32] = 1;
        BinaryPrimitives.WriteUInt64LittleEndian(state.AsSpan(p + 22), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(state.AsSpan(p + 14), 0x8062);
        BinaryPrimitives.WriteUInt64LittleEndian(state.AsSpan(p + 38), 2); state[p + 33] = 7;
        BinaryPrimitives.WriteUInt16LittleEndian(state.AsSpan(p + 78), 0x8079);
        BinaryPrimitives.WriteUInt64LittleEndian(state.AsSpan(p + 38 + 32), 3); state[p + 37] = 10;
        state.AsSpan(p, 22).CopyTo(native.AsSpan(0x44DC));
        return (native, state);
    }

    internal static byte[] Envelope(object payload)
    {
        var element = JsonSerializer.SerializeToElement(payload);
        return JsonSerializer.SerializeToUtf8Bytes(new { payload = element,
            sha256 = ModAuthoringFiles.Sha256(Encoding.ASCII.GetBytes(WorkshopInspector.Canonical(element))) });
    }
    internal static byte[] Sidecar(byte[] native, byte[] state, string path, string saveId = "0123456789abcdef0123456789abcdef") => Envelope(new {
        schema = 1, save_id = saveId, workspace_key = ModAuthoringFiles.Sha256(Encoding.UTF8.GetBytes(Path.GetFullPath(path))),
        native_sha256 = ModAuthoringFiles.Sha256(native), state = Convert.ToBase64String(state)
    });

    [Fact]
    public void Workshop_ValidatesIdentityAndShowsFifthWithoutWritingNativeBytes()
    {
        var (native, state) = Workshop(); var original = (byte[])native.Clone();
        string workspace = Path.GetFullPath("workshop");
        var result = WorkshopInspector.Read(native, Sidecar(native, state, workspace), workspace, "0123456789abcdef0123456789abcdef");
        var piece = Assert.Single(result.Pieces);
        Assert.Equal((ulong)1, piece.Id); Assert.Equal((ulong)3, piece.AbilityIds[4]);
        Assert.Equal(0x8079, piece.Abilities[4]); Assert.Equal(10, piece.Ranks[4]);
        Assert.Equal(original, native);
        Assert.Throws<InvalidDataException>(() => WorkshopInspector.Read(native, Sidecar(native, state, workspace), workspace + "-copy", result.SaveId));
    }

    [Theory]
    [InlineData("duplicate-id")][InlineData("fifth-mod002")][InlineData("rank")][InlineData("capacity")]
    [InlineData("unknown-version")][InlineData("material-mismatch")][InlineData("native-mismatch")][InlineData("vacant-metadata")]
    public void Workshop_RejectsCorruptOrUnsupportedState(string fault)
    {
        var (native, state) = Workshop();
        switch (fault)
        {
            case "duplicate-id": BinaryPrimitives.WriteUInt64LittleEndian(state.AsSpan(36 + 38 + 32), 2); break;
            case "fifth-mod002": BinaryPrimitives.WriteUInt16LittleEndian(state.AsSpan(36 + 78), 0x8087); break;
            case "rank": state[36 + 33] = 11; break;
            case "capacity": state[36 + 11] = native[0x44DC + 11] = 5; break;
            case "unknown-version": state[0] = 2; break;
            case "material-mismatch": state[16036] = 1; break;
            case "native-mismatch": native[0x44DC + 3] = 1; break;
            case "vacant-metadata": state[36 + 80 + 31] = 1; break;
        }
        string workspace = Path.GetFullPath("workshop");
        Assert.Throws<InvalidDataException>(() => WorkshopInspector.Read(native, Sidecar(native, state, workspace), workspace, "0123456789abcdef0123456789abcdef"));
    }

    [Fact]
    public void Workshop_RejectsTamperingUnknownFieldsAndPendingRecovery()
    {
        string root = Path.Combine(Path.GetTempPath(), "ffx-workshop-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var (native, state) = Workshop();
            File.WriteAllBytes(Path.Combine(root, "native.bin"), native);
            byte[] sidecar = Sidecar(native, state, root);
            File.WriteAllBytes(Path.Combine(root, "sidecar.json"), sidecar);
            File.WriteAllBytes(Path.Combine(root, "origin.json"), Envelope(new {
                source = "/original/save.ffx", source_sha256 = ModAuthoringFiles.Sha256(native), save_id = "0123456789abcdef0123456789abcdef"
            }));
            Assert.Single(WorkshopInspector.ReadDirectory(root).Pieces);
            File.WriteAllText(Path.Combine(root, "pending.json"), "{}");
            Assert.Throws<InvalidDataException>(() => WorkshopInspector.ReadDirectory(root));
            File.Delete(Path.Combine(root, "pending.json"));
            var doc = JsonNode.Parse(sidecar)!;
            doc["sha256"] = new string('0', 64);
            File.WriteAllText(Path.Combine(root, "sidecar.json"), doc.ToJsonString());
            Assert.Throws<InvalidDataException>(() => WorkshopInspector.ReadDirectory(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CanonicalJson_MatchesPythonAsciiEscaping()
    {
        using var doc = JsonDocument.Parse("{\"z\":\"a+b/ç😀\",\"schema\":1}");
        Assert.Equal("{\"schema\":1,\"z\":\"a+b/\\u00e7\\ud83d\\ude00\"}", WorkshopInspector.Canonical(doc.RootElement));
    }
}
