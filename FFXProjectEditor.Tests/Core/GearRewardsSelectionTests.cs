using System;
using System.Buffers.Binary;
using System.IO;
using FFXProjectEditor.Modules.BukiGetRewards;
using FFXProjectEditor.Services;
using FFXProjectEditor.Tests.Services;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

[Collection(ProjectServiceCollection.Name)]
public sealed class GearRewardsSelectionTests
{
    [Fact]
    public void LoadAndSelection_PreservePayloadAndRefreshFailureClearsTheWriter()
    {
        string root = Path.Combine(Path.GetTempPath(), "gear-selection-" + Guid.NewGuid().ToString("N"));
        string? previous = Project_Service.Instance.ProjectPath;
        BukiGetTreasureCatalog_DataModel? model = null;
        try
        {
            string path = Path.Combine(root, "jppc", "battle", "kernel", "buki_get.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            byte[] bytes = new byte[0x574];
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10), 85);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), 16);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), 0x560);
            for (int row = 0; row < 86; row++)
            {
                int p = 0x14 + row * 16;
                bytes[p + 1] = (byte)(row % 7);
                bytes[p + 5] = (byte)(20 + row);
                bytes[p + 7] = 2;
                for (int i = 8; i < 16; i += 2) bytes[p + i] = 0xFF;
            }
            File.WriteAllBytes(path, bytes);
            Project_Service.Instance.ProjectPath = root;
            model = new BukiGetTreasureCatalog_DataModel();
            Assert.Equal(86, model.DisplayedRows.Count);
            Assert.Equal(path, model.SourcePath);
            Assert.NotNull(model.EditSession);
            Assert.False(model.EditSession.HasPendingChanges);
            model.SelectedRow = model.DisplayedRows[1];
            Assert.Equal(1, model.EditOwner);
            Assert.Equal(21, model.EditPower);
            Assert.False(model.EditSession.HasPendingChanges);
            model.SelectedRow = model.DisplayedRows[0];
            Assert.Equal(0, model.EditOwner);
            Assert.Equal(20, model.EditPower);
            Assert.False(model.EditSession.HasPendingChanges);
            Assert.Equal(bytes, File.ReadAllBytes(path));

            File.Delete(path);
            model.RefreshFromDisk();
            Assert.Empty(model.DisplayedRows);
            Assert.Null(model.SelectedRow);
            Assert.Null(model.EditSession);
        }
        finally
        {
            model?.EditSession?.Dispose();
            Project_Service.Instance.ProjectPath = previous;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
