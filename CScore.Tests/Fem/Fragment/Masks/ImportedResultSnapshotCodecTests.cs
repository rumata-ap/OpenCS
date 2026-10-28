using CScore.Fem.Fragment.Masks;
using Xunit;

namespace CScore.Tests.Fem.Fragment.Masks;

public sealed class ImportedResultSnapshotCodecTests
{
    static ImportedResultSnapshot Sample() => new()
    {
        Program = ImportedResultSnapshot.ProgramScad,
        SourceName = "этаж.SPR",
        TakenAt = "2026-10-28T12:00:00",
        Loads =
        [
            new ImportedResultLoad
            {
                Num = 1, Name = "Постоянная", NodeNums = [10, 11],
                Displacements = [1e-3, 0, -2e-3, 1e-5, 0, 0, 0, 0, -1.5e-3, 0, 2e-5, 0],
                BarNums = [5], BarForces = Enumerable.Range(1, 12).Select(i => i * 100.0).ToArray(),
                ShellNums = [7, 8], ShellForces = Enumerable.Range(1, 16).Select(i => -i * 10.0).ToArray(),
            },
            new ImportedResultLoad { Num = 3, Name = "", Step = 12 },
        ],
    };

    [Fact]
    public void RoundTrip()
    {
        var a = Sample();
        var b = ImportedResultSnapshotCodec.Decode(ImportedResultSnapshotCodec.Encode(a));

        Assert.Equal(a.Program, b.Program);
        Assert.Equal(a.SourceName, b.SourceName);
        Assert.Equal(a.TakenAt, b.TakenAt);
        Assert.Equal(2, b.Loads.Count);
        var (x, y) = (a.Loads[0], b.Loads[0]);
        Assert.Equal((x.Num, x.Name, x.Step), (y.Num, y.Name, y.Step));
        Assert.Equal(x.NodeNums, y.NodeNums);
        Assert.Equal(x.Displacements, y.Displacements);
        Assert.Equal(x.BarNums, y.BarNums);
        Assert.Equal(x.BarForces, y.BarForces);
        Assert.Equal(x.ShellNums, y.ShellNums);
        Assert.Equal(x.ShellForces, y.ShellForces);
        // Пустые разделы остаются пустыми (данных нет, а не нули).
        Assert.Equal(12, b.Loads[1].Step);
        Assert.Empty(b.Loads[1].NodeNums);
        Assert.Empty(b.Loads[1].Displacements);
        Assert.Empty(b.Loads[1].BarNums);
        Assert.Empty(b.Loads[1].ShellNums);
    }

    [Fact]
    public void Fingerprint_ChangesWithData()
    {
        var a = ImportedResultSnapshotCodec.Encode(Sample());
        var s = Sample();
        s.Loads[0].Displacements[2] = -2.1e-3;
        var b = ImportedResultSnapshotCodec.Encode(s);

        Assert.Equal(ImportedResultSnapshotCodec.Fingerprint(a), ImportedResultSnapshotCodec.Fingerprint(ImportedResultSnapshotCodec.Encode(Sample())));
        Assert.NotEqual(ImportedResultSnapshotCodec.Fingerprint(a), ImportedResultSnapshotCodec.Fingerprint(b));
    }

    [Fact]
    public void MismatchedLengths_Throw()
    {
        var s = new ImportedResultSnapshot
        {
            Program = ImportedResultSnapshot.ProgramLira,
            Loads = [new ImportedResultLoad { Num = 1, NodeNums = [1], Displacements = [0, 0, 0] }],
        };
        Assert.Throws<ArgumentException>(() => ImportedResultSnapshotCodec.Encode(s));
    }

    [Fact]
    public void Garbage_ThrowsInvalidData()
    {
        var data = ImportedResultSnapshotCodec.Encode(Sample());
        Assert.ThrowsAny<InvalidDataException>(() => ImportedResultSnapshotCodec.Decode(data[..(data.Length / 2)]));
        Assert.ThrowsAny<InvalidDataException>(() => ImportedResultSnapshotCodec.Decode([1, 2, 3, 4]));
    }
}
