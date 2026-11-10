using CScore.Fem.Fragment.Masks;
using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

public sealed class LiraResultSnapshotBuilderTests
{
    const double Ton = 9.80665;
    // Усилия документа — т и м, перемещения — мм.
    static readonly LiraApiUnits Units = LiraApiUnits.FromCodes(2, 0, Ton);

    static LiraRawLoad Load(int num, int[] shells) => new(num, $"ЗН {num}", [7, 9],
        [1, 2, 3, 0.1, 0.2, 0.3, 4, 5, 6, 0.4, 0.5, 0.6],
        [11], [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12],
        shells, [.. shells.SelectMany(_ => new double[] { 1, 2, 3, 4, 5, 6, 7, 8 })]);

    [Fact]
    public void UnitsToSi_AndSectionOrder()
    {
        var b = LiraResultSnapshotBuilder.Build([Load(2, [20]), Load(1, [20])], Units, 1e-3,
            new Dictionary<int, double> { [20] = 0.2 }, "doc", "t");

        Assert.Equal(0, b.ShellsWithoutThickness);
        var s = b.Snapshot;
        Assert.Equal(ImportedResultSnapshot.ProgramLira, s.Program);
        Assert.Equal([1, 2], s.Loads.Select(l => l.Num));

        // Перемещения: мм → м у X Y Z, повороты как есть.
        var d = s.Loads[0].Displacements;
        Assert.Equal([1e-3, 2e-3, 3e-3, 0.1, 0.2, 0.3], d[..6]);
        Assert.Equal(6e-3, d[8], 12);

        // Стержень: N Qy Qz в т → Н, Mx My Mz в т·м → Н·м; порядок сечений сохранён.
        var bar = s.Loads[0].BarForces;
        Assert.Equal(1 * Ton * 1e3, bar[0], 6);
        Assert.Equal(3 * Ton * 1e3, bar[2], 6);
        Assert.Equal(4 * Ton * 1e3, bar[3], 6);
        Assert.Equal(12 * Ton * 1e3, bar[11], 6);

        // Пластина: напряжения × h (т/м² → Н/м), моменты — т·м/м → Н·м/м, Q — т/м → Н/м.
        var sh = s.Loads[0].ShellForces;
        Assert.Equal([20], s.Loads[0].ShellNums);
        Assert.Equal(1 * Ton * 1e3 * 0.2, sh[0], 6);
        Assert.Equal(3 * Ton * 1e3 * 0.2, sh[2], 6);
        Assert.Equal(4 * Ton * 1e3, sh[3], 6);
        Assert.Equal(8 * Ton * 1e3, sh[7], 6);
    }

    [Fact]
    public void ShellWithoutThickness_Skipped()
    {
        var b = LiraResultSnapshotBuilder.Build([Load(1, [20, 21])], Units, 1, new Dictionary<int, double> { [21] = 0.3 }, "", "");

        Assert.Equal(1, b.ShellsWithoutThickness);
        Assert.Equal([21], b.Snapshot.Loads[0].ShellNums);
        Assert.Equal(8, b.Snapshot.Loads[0].ShellForces.Length);
    }

    [Fact]
    public void Snapshot_RoundTripsThroughCodec()
    {
        var b = LiraResultSnapshotBuilder.Build([Load(1, [20])], Units, 1e-3, new Dictionary<int, double> { [20] = 0.2 }, "doc", "t");

        var back = ImportedResultSnapshotCodec.Decode(ImportedResultSnapshotCodec.Encode(b.Snapshot));

        Assert.Equal(ImportedResultSnapshot.ProgramLira, back.Program);
        Assert.Equal(b.Snapshot.Loads[0].BarForces, back.Loads[0].BarForces);
    }
}
