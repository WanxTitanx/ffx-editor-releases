// ── Legacy-compatible bounded sidecar transformation ───────────────────────────────────
// WHY: Linux preparation must preserve every observable ApplySlot success/error and byte result.
// MAINT: Large-key cases intentionally exercise accepted BCL hints/scratch; they do not measure RSS.
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FFXProjectEditor.Modules.Common.ViewerHub;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub;

[Collection(FileSystemReparseGuardHookCollection.Name)]
public sealed class LinuxSidecarJsonTests
{
    private readonly record struct Edit(
        int Slot,
        double? Dx,
        double? Dy,
        double? Dz,
        double? Heading,
        double? Scale);

    private static readonly Edit Complete = new(4, 1.25, -2.5, 3.75, 0.5, 1.5);

    [Fact]
    public void Prepare_NullInputMatchesLegacyAndReturnsIndependentExactUtf8()
    {
        byte[] expected = Legacy(null, Complete);
        byte[] first = Prepare(null, Complete);
        byte[] second = Prepare(null, Complete);

        Assert.Equal(expected, first);
        Assert.Equal(expected.Length, first.Length);
        Assert.NotSame(first, second);
        first[0] ^= 0x01;
        Assert.Equal(expected, second);
    }

    [Fact]
    public void Prepare_EmptyInputPreservesLegacyParseErrorInsteadOfMeaningMissing()
    {
        byte[] empty = Array.Empty<byte>();
        AssertLegacyParity(empty, Complete);
        Assert.IsAssignableFrom<JsonException>(Record.Exception(() => Prepare(empty, Complete)));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("0")]
    [InlineData("{\"actors\":null}")]
    [InlineData("{\"actors\":[]}")]
    [InlineData("{\"actors\":1}")]
    [InlineData("{\"actors\":{\"4\":null}}")]
    [InlineData("{\"actors\":{\"4\":[]}}")]
    [InlineData("{\"actors\":{\"4\":1}}")]
    public void Prepare_CoercesRootActorsAndSelectedSlotExactlyLikeLegacy(string json) =>
        AssertLegacyParity(Utf8(json), Complete);

    [Theory]
    [InlineData(1.0, 2.0, 3.0, 4.0, 5.0)]
    [InlineData(1.0, null, 3.0, null, null)]
    [InlineData(null, null, null, 4.0, null)]
    [InlineData(null, null, null, null, 5.0)]
    [InlineData(null, null, null, null, null)]
    [InlineData(-1.0, -2.0, -3.0, null, null)]
    public void Prepare_PreservesAllPartialAndNullDeltaRules(
        double? dx,
        double? dy,
        double? dz,
        double? heading,
        double? scale)
    {
        const string json = "{\"actors\":{\"4\":{\"position\":[9,8,7],\"heading\":6,\"scale\":5,\"keep\":4}}}";
        AssertLegacyParity(Utf8(json), new Edit(4, dx, dy, dz, heading, scale));
    }

    [Fact]
    public void Prepare_PreservesUnknownFieldsOrderSlotsAndInputOwnership()
    {
        const string json = "{\"before\":1,\"actors\":{\"7\":{\"z\":2},\"4\":{\"keep\":3},\"0\":[4]},\"after\":5}";
        byte[] input = Utf8(json);
        byte[] before = input.ToArray();

        AssertLegacyParity(input, Complete);

        Assert.Equal(before, input);
        string output = Encoding.UTF8.GetString(Prepare(input, Complete));
        Assert.True(output.IndexOf("\"before\"", StringComparison.Ordinal) <
                    output.IndexOf("\"actors\"", StringComparison.Ordinal));
        Assert.True(output.IndexOf("\"actors\"", StringComparison.Ordinal) <
                    output.IndexOf("\"after\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{\"keep\":1,\"keep\":2}")]
    [InlineData("{\"actors\":{},\"actors\":{}}")]
    [InlineData("{\"actors\":{\"4\":{},\"4\":{}}}")]
    [InlineData("{\"actors\":{\"4\":{\"x\":1,\"x\":2}}}")]
    [InlineData("{\"actors\":{\"7\":{\"x\":1,\"x\":2}}}")]
    public void Prepare_DuplicatePropertiesMatchLegacySuccessOrErrorAtTouchedAndUntouchedBranches(string json) =>
        AssertLegacyParity(Utf8(json), Complete);

    [Theory]
    [InlineData("utf8-bom")]
    [InlineData("utf16-le")]
    [InlineData("utf16-be")]
    [InlineData("utf32-le")]
    [InlineData("utf32-be")]
    public void Prepare_DetectsEveryExistingStoreBomEncoding(string kind)
    {
        byte[] input = EncodedWithPreamble("{\"label\":\"Spira 漢字\"}", kind);
        AssertLegacyParity(input, Complete);
    }

    [Fact]
    public void Prepare_UsesExistingStoreInvalidUtf8ReplacementFallback()
    {
        byte[] input = { (byte)'{', (byte)'\"', (byte)'v', (byte)'\"', (byte)':', (byte)'\"', 0xFF, (byte)'\"', (byte)'}' };

        AssertLegacyParity(input, Complete);
        Assert.Equal("\uFFFD", JsonNode.Parse(Prepare(input, Complete))!["v"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("{\"label\":\"é漢字🙂\"}")]
    [InlineData("{\"label\":\"<>&\"}")]
    [InlineData("{\"label\":\"quote\\\" slash\\\\ tab\\t line\\n\"}")]
    [InlineData("{\"label\":\"\\uD83D\\uDE00\"}")]
    [InlineData("{\"label\":\"\\uD800\"}")]
    public void Prepare_PreservesEscapingUnicodeAndSurrogateBehavior(string json) =>
        AssertLegacyParity(Utf8(json), Complete);

    [Fact]
    public void Prepare_PreservesUnknownNumericLexemesWithoutDoubleRoundTrip()
    {
        const string json = "{\"unknown\":\"keep\",\"unknownNumber\":1.2300e+4," +
            "\"huge\":184467440737095516161234567890,\"negativeZero\":-0," +
            "\"tiny\":0.0000000000000000000000001,\"actors\":{\"7\":{\"raw\":9.99e+9999}}}";
        AssertLegacyParity(Utf8(json), Complete);
    }

    [Theory]
    [InlineData("nan")]
    [InlineData("positive-infinity")]
    [InlineData("negative-infinity")]
    public void Prepare_UsedNonFiniteDoubleFailsWithLegacyExceptionType(string kind)
    {
        double value = kind switch
        {
            "nan" => double.NaN,
            "positive-infinity" => double.PositiveInfinity,
            _ => double.NegativeInfinity
        };
        // Each row reaches every independently optional numeric write, not heading alone.
        foreach (Edit edit in new[]
        {
            new Edit(4, value, 2, 3, null, null),
            new Edit(4, 1, value, 3, null, null),
            new Edit(4, 1, 2, value, null, null),
            new Edit(4, null, null, null, value, null),
            new Edit(4, null, null, null, null, value)
        })
            AssertLegacyParity(Utf8("{}"), edit);
    }

    [Fact]
    public void Prepare_IgnoresPartialPositionNaNWhenLegacyDoes()
    {
        const string json = "{\"actors\":{\"4\":{\"position\":[9,8,7]}}}";
        AssertLegacyParity(Utf8(json), new Edit(4, double.NaN, 2, null, null, null));
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("de-DE")]
    public void Prepare_FormatsArbitrarySlotWithInvariantCulture(string culture)
    {
        CultureInfo oldCulture = CultureInfo.CurrentCulture;
        CultureInfo oldUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var markedCulture = (CultureInfo)new CultureInfo(culture).Clone();
            markedCulture.NumberFormat.NegativeSign = "~";
            CultureInfo.CurrentCulture = markedCulture;
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            Assert.Equal("~1234567", (-1234567).ToString()); // Prove default formatting differs.
            var edit = new Edit(-1234567, null, null, null, null, null);
            AssertLegacyParity(Utf8("{}"), edit);
            JsonNode root = JsonNode.Parse(Prepare(Utf8("{}"), edit))!;
            Assert.NotNull(root["actors"]!["-1234567"]);
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
            CultureInfo.CurrentUICulture = oldUiCulture;
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(64)]
    [InlineData(65)]
    public void Prepare_UsesLegacyDepth64RatherThanRecordDepth8OrWriterDepth1000(int depth)
    {
        byte[] input = Utf8(ObjectAtDepth(depth));
        Exception? legacyError = Record.Exception(() => Legacy(input, Complete));
        if (depth <= 64)
            Assert.Null(legacyError);
        else
            Assert.IsAssignableFrom<JsonException>(legacyError);
        AssertLegacyParity(input, Complete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Prepare_DistinguishesInputAdmissionLFromOutputBounds(bool overLimit)
    {
        int length = LinuxSidecarJson.LogicalByteLimit + (overLimit ? 1 : 0);
        byte[] input = Enumerable.Repeat((byte)' ', length).ToArray();
        Exception? error = Record.Exception(() => Prepare(input, Complete));

        if (overLimit)
            Assert.IsType<InvalidDataException>(error);
        else
        {
            Assert.IsAssignableFrom<JsonException>(error);
            Assert.Equal(Record.Exception(() => Legacy(input, Complete))!.GetType(), error!.GetType());
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Prepare_UsesIndependentLegacyOracleAtExactOutputLAndLPlusOne(int extraByte)
    {
        int target = LinuxSidecarJson.LogicalByteLimit + extraByte;
        byte[] input = ValueInputForLegacyOutput(target);
        Assert.True(input.Length <= LinuxSidecarJson.LogicalByteLimit);
        byte[] expected = Legacy(input, Complete);
        Assert.Equal(target, expected.Length);

        if (extraByte == 0)
            Assert.Equal(expected, Prepare(input, Complete));
        else
            Assert.Throws<InvalidDataException>(() => Prepare(input, Complete));
    }

    [Fact]
    public void Prepare_ManySmallTokensCrossLogicalOutputLimitWithoutCrossingInputLimit()
    {
        const int reserve = 128 * 1024;
        const int tokenLength = 1024;
        string token = new('a', tokenLength);
        int count = (LinuxSidecarJson.LogicalByteLimit - reserve - 16) / (tokenLength + 3);
        var json = new StringBuilder(LinuxSidecarJson.LogicalByteLimit - reserve);
        json.Append("{\"items\":[");
        for (int index = 0; index < count; index++)
        {
            if (index != 0)
                json.Append(',');
            json.Append('"').Append(token).Append('"');
        }
        json.Append("]}");
        byte[] input = Utf8(json.ToString());
        Assert.True(input.Length <= LinuxSidecarJson.LogicalByteLimit);
        Assert.True(Legacy(input, Complete).Length > LinuxSidecarJson.LogicalByteLimit);
        Assert.Throws<InvalidDataException>(() => Prepare(input, Complete));
    }

    [Fact]
    public void Prepare_AcceptsAsciiPropertyName24MiBWhoseWriterHintExceedsL()
    {
        string key = new('a', 24 * 1024 * 1024);
        byte[] input = Utf8("{\"" + key + "\":0}");
        byte[] expected = Legacy(input, Complete);
        Assert.True(expected.Length < LinuxSidecarJson.LogicalByteLimit);
        Assert.Equal(expected, Prepare(input, Complete));
    }

    [Theory]
    [InlineData("ascii")]
    [InlineData("less-than-first")]
    [InlineData("less-than-last")]
    public void Prepare_AcceptsValidNearLPropertyNameAndEscapeScratchPositions(string kind)
    {
        int emptyNameOverhead = Legacy(Utf8("{\"\":0}"), Complete).Length;
        int escapeExpansion = kind == "ascii" ? 0 : 5;
        int keyLength = LinuxSidecarJson.LogicalByteLimit - emptyNameOverhead - escapeExpansion;
        char[] key = new string('a', keyLength).ToCharArray();
        if (kind == "less-than-first")
            key[0] = '<';
        if (kind == "less-than-last")
            key[^1] = '<';
        byte[] input = Utf8("{\"" + new string(key) + "\":0}");
        Assert.True(input.Length <= LinuxSidecarJson.LogicalByteLimit);
        byte[] expected = Legacy(input, Complete);
        Assert.Equal(LinuxSidecarJson.LogicalByteLimit, expected.Length);
        Assert.Equal(expected, Prepare(input, Complete));
    }

    private static byte[] Prepare(byte[]? input, Edit edit) => LinuxSidecarJson.Prepare(
        input, edit.Slot, edit.Dx, edit.Dy, edit.Dz, edit.Heading, edit.Scale);

    private static byte[] Legacy(byte[]? input, Edit edit) => Encoding.UTF8.GetBytes(
        SidecarEditsWriter.ApplySlot(
            DecodeLikeExistingStore(input),
            edit.Slot,
            edit.Dx,
            edit.Dy,
            edit.Dz,
            edit.Heading,
            edit.Scale));

    private static string? DecodeLikeExistingStore(byte[]? input)
    {
        if (input is null)
            return null;
        using var stream = new MemoryStream(input, writable: false);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 16 * 1024, leaveOpen: true);
        return reader.ReadToEnd();
    }

    private static void AssertLegacyParity(byte[]? input, Edit edit)
    {
        byte[]? expected = null;
        byte[]? actual = null;
        Exception? expectedError = Record.Exception(() => expected = Legacy(input, edit));
        Exception? actualError = Record.Exception(() => actual = Prepare(input, edit));

        if (expectedError is null)
        {
            Assert.Null(actualError);
            Assert.Equal(expected, actual);
            return;
        }

        Assert.NotNull(actualError);
        // Category checks allow parser subtypes; this oracle still requires identical concrete exceptions.
        Assert.Equal(expectedError.GetType(), actualError.GetType());
    }

    private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    private static byte[] EncodedWithPreamble(string text, string kind)
    {
        Encoding encoding = kind switch
        {
            "utf8-bom" => new UTF8Encoding(true),
            "utf16-le" => new UnicodeEncoding(false, true),
            "utf16-be" => new UnicodeEncoding(true, true),
            "utf32-le" => new UTF32Encoding(false, true),
            "utf32-be" => new UTF32Encoding(true, true),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
    }

    private static string ObjectAtDepth(int depth)
    {
        var json = new StringBuilder(depth * 8);
        for (int level = 1; level < depth; level++)
            json.Append("{\"n\":");
        json.Append("{\"leaf\":0}");
        for (int level = 1; level < depth; level++)
            json.Append('}');
        return json.ToString();
    }

    private static byte[] ValueInputForLegacyOutput(int target)
    {
        int emptyValueOverhead = Legacy(Utf8("{\"pad\":\"\"}"), Complete).Length;
        int payloadLength = checked(target - emptyValueOverhead);
        return Utf8("{\"pad\":\"" + new string('a', payloadLength) + "\"}");
    }
}
