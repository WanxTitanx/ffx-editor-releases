// WHY: Canonical recovery inventory encoding must be finite and independent of locale/order.
// MAINT: Observations here are synthetic predicates, not native ownership or remount proof.
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using FFXProjectEditor.Modules.Common.ViewerHub;
using Xunit;
using I = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryInspection;
using L = FFXProjectEditor.Modules.Common.ViewerHub.LinuxReadFileSystem;
using O = FFXProjectEditor.Modules.Common.ViewerHub.LinuxOutputFileSystem.OutputObservation;

namespace FFXProjectEditor.Tests.ViewerHub;

public sealed class LinuxRecoveryInspectionEncodingTests
{
    internal static I.Entry Row(string leaf = ".spira-retained-a.bin", long length = 3) =>
        new(I.EntrySpace.Root, leaf, new O(new L.LinuxFileObservation(
            new L.LinuxObjectIdentity(8, 1, 300, 9), length, 1, 2, 3, 4),
            0x8180, 1234, 1));

    [Fact]
    public void Fingerprint_IsOrderedAndCountsZeroByteUnknownHistory()
    {
        I.Entry first = Row();
        I.Entry second = Row("unknown.json", 0) with { Space = I.EntrySpace.Journal };
        var forward = I.Fingerprint(new[] { first, second });
        Assert.Equal(forward, I.Fingerprint(new[] { second, first }));
        Assert.Equal(3, forward.Bytes);
        Assert.Equal(2, forward.Entries);
        Assert.Matches("^[0-9A-F]{64}$", forward.Sha256);
        Assert.NotEqual(forward.Sha256, I.Fingerprint(new[] { first }).Sha256);
        Assert.NotEqual(forward.Sha256, I.Fingerprint(Array.Empty<I.Entry>()).Sha256);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)]
    public void Fingerprint_BindsEveryVaryingSafeMetadataField(int field)
    {
        I.Entry original = Row();
        var observation = original.Observation;
        var file = observation.File;
        var identity = file.Identity;
        I.Entry changed = field switch
        {
            0 => original with { Space = I.EntrySpace.Journal },
            1 => original with { Leaf = ".spira-retained-b.bin" },
            2 => original with { Observation = observation with { File = file with
                { Identity = identity with { DeviceMajor = 9 } } } },
            3 => original with { Observation = observation with { File = file with
                { Identity = identity with { DeviceMinor = 2 } } } },
            4 => original with { Observation = observation with { File = file with
                { Identity = identity with { Inode = 301 } } } },
            5 => original with { Observation = observation with { File = file with
                { Identity = identity with { MountId = 10 } } } },
            6 => original with { Observation = observation with { File = file with { Length = 4 } } },
            7 => original with { Observation = observation with { Mode = 0x81A4 } },
            8 => original with { Observation = observation with { OwnerId = 1235 } },
            9 => original with { Observation = observation with { File = file with { ModifiedSeconds = -1 } } },
            10 => original with { Observation = observation with { File = file with { ModifiedNanoseconds = 5 } } },
            11 => original with { Observation = observation with { File = file with { ChangedSeconds = -2 } } },
            _ => original with { Observation = observation with { File = file with { ChangedNanoseconds = 6 } } },
        };
        Assert.NotEqual(I.Fingerprint(new[] { original }).Sha256, I.Fingerprint(new[] { changed }).Sha256);
    }

    [Theory]
    [InlineData("tr-TR")] [InlineData("ja-JP")]
    public void Fingerprint_UsesNoCurrentCulture(string language)
    {
        var rows = new[] { Row(), Row("other.json", 125) with { Space = I.EntrySpace.Journal } };
        var expected = I.Fingerprint(rows);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language);
            Assert.Equal(expected, I.Fingerprint(rows));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(0, "tr-TR")] [InlineData(0, "de-DE")]
    [InlineData(1, "tr-TR")] [InlineData(1, "de-DE")]
    public void Fingerprint_UsesOrdinalLeafOrderWithinEachNamespace(int space, string language)
    {
        // Independent fixed binary vectors encode z.bin BEFORE ä.bin in either namespace.
        // A culture-sensitive comparator reverses these names; namespace ordering cannot hide it.
        string expected = space == 0
            ? "D60ADC93BB3B818F39511A54886491DFAA30035A6E9E2A31A539E1F6D834A55D"
            : "0112AE98B7EA07E619843941E9C28BCBF9A566422673F226DB94CC8BBCCFF2F2";
        var first = Row("z.bin") with { Space = (I.EntrySpace)space };
        var second = Row("ä.bin") with { Space = (I.EntrySpace)space };
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language);
            Assert.Equal(expected, I.Fingerprint(new[] { first, second }).Sha256);
            Assert.Equal(expected, I.Fingerprint(new[] { second, first }).Sha256);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("empty")] [InlineData("traversal")] [InlineData("surrogate")]
    [InlineData("utf8")] [InlineData("length")] [InlineData("inode")]
    [InlineData("mount")] [InlineData("mtime")] [InlineData("ctime")]
    [InlineData("mode")] [InlineData("links")] [InlineData("space")]
    public void Fingerprint_RefusesInvalidFiniteObservations(string kind)
    {
        I.Entry row = Row();
        O o = row.Observation;
        var f = o.File;
        row = kind switch
        {
            "empty" => row with { Leaf = "" },
            "traversal" => row with { Leaf = "../file" },
            "surrogate" => row with { Leaf = "\ud800" },
            "utf8" => row with { Leaf = new string('é', 128) },
            "length" => row with { Observation = o with { File = f with { Length = -1 } } },
            "inode" => row with { Observation = o with { File = f with
                { Identity = f.Identity with { Inode = 0 } } } },
            "mount" => row with { Observation = o with { File = f with
                { Identity = f.Identity with { MountId = 0 } } } },
            "mtime" => row with { Observation = o with { File = f with { ModifiedNanoseconds = 1_000_000_000 } } },
            "ctime" => row with { Observation = o with { File = f with { ChangedNanoseconds = 1_000_000_000 } } },
            "mode" => row with { Observation = o with { Mode = 0x81B6 } },
            "links" => row with { Observation = o with { LinkCount = 2 } },
            _ => row with { Space = (I.EntrySpace)9 },
        };
        Assert.Throws<InvalidDataException>(() => I.Fingerprint(new[] { row }));
    }

    [Fact]
    public void Fingerprint_RefusesDuplicateNamesButSeparatesNamespaces()
    {
        I.Entry row = Row();
        Assert.Throws<InvalidDataException>(() => I.Fingerprint(new[] { row, row }));
        Assert.Equal(2, I.Fingerprint(new[] { row, row with { Space = I.EntrySpace.Journal } }).Entries);
    }

    [Fact]
    public void Fingerprint_RefusesOversizedEntryCountAndOversizedCallerLeaf()
    {
        var rows = Enumerable.Range(0, 16385).Select(index => Row("r-" + index + ".bin")).ToArray();
        Assert.Throws<InvalidDataException>(() => I.Fingerprint(rows));
        string huge = new('a', 1024 * 1024);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidDataException>(() => I.Fingerprint(new[] { Row(huge) }));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 1024 * 1024);
    }

    [Fact]
    public void Fingerprint_AccountsBeyondAdmissionLimitButRejectsArithmeticOverflow()
    {
        long over = LinuxRecoveryBudget.MaximumBytes + 1;
        Assert.Equal(over, I.Fingerprint(new[] { Row(length: over) }).Bytes);
        Assert.Throws<InvalidDataException>(() => I.Fingerprint(
            new[] { Row(length: long.MaxValue), Row("second", 1) }));
    }
}
