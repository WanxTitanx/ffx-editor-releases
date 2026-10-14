using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicNativeReserveContractTests
{
    public static IEnumerable<object[]> ReserveFamilies => MagicFieldMap.LoadEmbedded().Families.Keys
        .Where(name => name.StartsWith("pppKeThRes", System.StringComparison.Ordinal))
        .Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(ReserveFamilies))]
    public void FixedNativeReserve_IdentifiesItsInitializerAndNeverOffersSyntheticPayload(string family)
    {
        var schema = MagicFieldMap.LoadEmbedded().Families[family];
        Assert.Equal("NATIVE_HOST_STATE_ONLY", schema.Status);
        Assert.NotNull(schema.HandlerAddr);
        Assert.False(schema.PayloadConsumer);
        Assert.False(schema.Editable);
        Assert.Null(schema.Window);
        Assert.Empty(schema.Fields);
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 160, 4));
    }

    [Theory]
    [InlineData("pppDummyFunc", "NATIVE_RESERVED_NULL")]
    [InlineData("pppFaceAp", "NATIVE_NO_OP")]
    [InlineData("pppKeAcmSolid", "NATIVE_NO_OP")]
    [InlineData("pppKeShpTail3XImm", "NATIVE_NO_OP")]
    [InlineData("pppKeThCp", "NATIVE_NO_OP")]
    [InlineData("pppKeThCpSft", "NATIVE_NO_OP")]
    [InlineData("pppKeThLz", "NATIVE_NO_OP")]
    [InlineData("pppKeMvYpEff", "NATIVE_HOST_STATE_ONLY")]
    [InlineData("pppKeShpTailLc", "NATIVE_HOST_STATE_ONLY")]
    public void ReservedNoOpAndHostCallbacks_AreDistinguishedWithoutGrantingRecordWrites(string family, string status)
    {
        var schema = MagicFieldMap.LoadEmbedded().Families[family];
        Assert.Equal(status, schema.Status);
        Assert.False(schema.PayloadConsumer);
        Assert.False(schema.Editable);
        Assert.Empty(schema.Fields);
        Assert.Null(schema.Window);
    }
}
