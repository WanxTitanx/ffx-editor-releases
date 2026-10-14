using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicMatrixOperationContractTests
{
    [Theory]
    [InlineData("pppMatrixXYZ", "0x72D470")]
    [InlineData("pppMatrixXZY", "0x72D650")]
    [InlineData("pppMatrixYXZ", "0x72D830")]
    [InlineData("pppMatrixYZX", "0x72DA10")]
    [InlineData("pppMatrixZXY", "0x72DBF0")]
    [InlineData("pppMatrixZYX", "0x72DDD0")]
    [InlineData("pppMatrixLoop", "0x72D290")]
    [InlineData("pppMatrixLoc", "0x734200")]
    [InlineData("pppDrawMatrix", "0x734C40")]
    [InlineData("pppDrawMatrixLoop", "0x734C70")]
    [InlineData("pppDrawMatrixFront", "0x734CA0")]
    [InlineData("pppDrawMatrixNoRot", "0x734D20")]
    [InlineData("pppDrawMatrixWood", "0x734DC0")]
    [InlineData("pppKeDMat", "0x734F80")]
    [InlineData("pppKeDMatFr", "0x734FC0")]
    [InlineData("pppKeDMatPht", "0x735050")]
    [InlineData("pppKeDMatPhtFr", "0x7350F0")]
    [InlineData("pppKeMatPht", "0x7342B0")]
    [InlineData("pppKeOfsMatXYZ", "0x7344D0")]
    [InlineData("pppKeParMatR", "0x734950")]
    public void ProvenHostOperations_KeepTheirNativeEntryWithoutInventingPayloadFields(string family, string address)
    {
        var schema = MagicFieldMap.LoadEmbedded().Families[family];
        Assert.Equal("NATIVE_HOST_STATE_ONLY", schema.Status);
        Assert.Equal(address, schema.HandlerAddr);
        Assert.False(schema.PayloadConsumer);
        Assert.False(schema.Editable);
        Assert.Null(schema.Window);
        Assert.Empty(schema.Fields);
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 12, 4));
    }

    [Theory]
    [InlineData("pppSMatrix", "NATIVE_RESERVED_NULL", null)]
    [InlineData("pppMatrixFront", "NATIVE_RESERVED_NULL", null)]
    [InlineData("pppDrawMatrixWoodLoop", "NATIVE_NO_OP", "0x728AC0")]
    [InlineData("pppDrawMatrixFrontLoop", "NATIVE_NO_OP", "0x728AC0")]
    public void ReservedEntriesAndNoOpCallbacks_AreDistinct(string family, string status, string? address)
    {
        var schema = MagicFieldMap.LoadEmbedded().Families[family];
        Assert.Equal(status, schema.Status);
        Assert.Equal(address, schema.HandlerAddr);
        Assert.False(schema.Editable);
        Assert.Empty(schema.Fields);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("pt")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("it")]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("zh")]
    public async Task MatrixSlotUi_ExplainsNativeContractsEvenWhenTimingOrRawChildrenExist(string cultureName)
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            var vm = new MagicDllEditor_ViewModel();
            Assert.True(await vm.OpenFileAsync(Path.Combine(TestDataPaths.MagicCorpus, "magic_0003.dll")));
            var matrix = MagicTimingEditorTests.Walk(vm.RootNodes).OfType<MagicSlotNode>()
                .First(n => n.HandlerName == "pppMatrixXYZ");
            Assert.NotEmpty(matrix.Children);
            Assert.True(matrix.IsReadOnlyPpp);
            Assert.Equal("NATIVE_HOST_STATE_ONLY", matrix.NativeContractStatus);
            Assert.False(string.IsNullOrWhiteSpace(matrix.SemanticText));
            Assert.Equal(matrix.SemanticText, matrix.PppReadOnlyReason);
            Assert.Equal(matrix.SemanticText, matrix.DetailText);
            Assert.DoesNotContain("decomposed field-by-field", matrix.DetailText);
            var reserved = new MagicSlotNode(0, 0, "pppSMatrix", false, matrix.Children,
                nativeContractStatus: "NATIVE_RESERVED_NULL");
            var noOp = new MagicSlotNode(0, 0, "pppDrawMatrixWoodLoop", false, matrix.Children,
                nativeContractStatus: "NATIVE_NO_OP");
            Assert.True(reserved.IsReadOnlyPpp);
            Assert.True(noOp.IsReadOnlyPpp);
            Assert.NotEqual(matrix.SemanticText, reserved.SemanticText);
            Assert.NotEqual(reserved.SemanticText, noOp.SemanticText);
            Assert.All(MagicTimingEditorTests.Walk(matrix.Children).OfType<MagicFieldNode>(), f => Assert.False(f.IsValueEditable));
        }
        finally { CultureInfo.CurrentUICulture = original; }
    }
}
