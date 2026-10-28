using CScore.Fem.Fragment.Masks;
using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

public sealed class ScadResultSnapshotBuilderTests
{
    const double Ton = 9.80665;
    static readonly byte[] BarTypes = [ScadApiForceMapper.BarN, ScadApiForceMapper.BarMk, ScadApiForceMapper.BarMy,
        ScadApiForceMapper.BarQz, ScadApiForceMapper.BarMz, ScadApiForceMapper.BarQy];
    static readonly byte[] ShellTypes = [ScadApiForceMapper.ShellNx, ScadApiForceMapper.ShellNy, ScadApiForceMapper.ShellTxy,
        ScadApiForceMapper.ShellMx, ScadApiForceMapper.ShellMy, ScadApiForceMapper.ShellMxy, ScadApiForceMapper.ShellQx,
        ScadApiForceMapper.ShellQy];
    static readonly ScadResultCatalog Catalog = new(["Постоянная", "Снег"], []);

    /// <summary>Стержень: 3 сечения × 2 загружения, значения «сечение·10 + загружение + код/100».</summary>
    static ScadElementForces Bar(int id, int points = 3, int loads = 2)
    {
        var v = new List<double>();
        for (int p = 0; p < points; p++)
            for (int l = 0; l < loads; l++)
                v.AddRange(BarTypes.Select(t => p * 10 + l + t / 100.0));
        return new ScadElementForces(id, ScadElementKind.Beam, BarTypes, points, [.. v], loads, [], 0);
    }

    static ScadElementForces Shell(int id, int loads = 2)
    {
        var v = new List<double>();
        for (int l = 0; l < loads; l++) v.AddRange(ShellTypes.Select(t => l + 1 + t / 100.0));
        return new ScadElementForces(id, ScadElementKind.Shell, ShellTypes, 1, [.. v], loads, [], 0);
    }

    static ScadDisplacementSet Linear() => new()
    {
        Rows =
        [
            new ScadDisplacementRow(1, 1, "П", 0, [1, 2], [0, 0, -1e-3, 0, 0, 0, 0, 0, -2e-3, 0, 0, 0]),
            new ScadDisplacementRow(2, 1, "С", 0, [1, 2], [0, 0, -3e-3, 0, 0, 0, 0, 0, -4e-3, 0, 0, 0]),
        ],
    };

    [Fact]
    public void Linear_UnitsSectionsAndThickness()
    {
        var b = ScadResultSnapshotBuilder.Build(Linear(), [Bar(5), Shell(7), Shell(8), Bar(9, points: 1)], Catalog,
            new Dictionary<int, double> { [7] = 0.2 }, Ton, "a.SPR", "t");

        Assert.Equal((1, 1, 0), (b.ShortBars, b.ShellsWithoutThickness, b.LoadMismatch));
        var s = b.Snapshot;
        Assert.Equal(ImportedResultSnapshot.ProgramScad, s.Program);
        Assert.Equal([1, 2], s.Loads.Select(l => l.Num));
        Assert.Equal("Снег", s.Loads[1].Name);
        Assert.Equal(0, s.Loads[1].Step);
        Assert.Equal(-4e-3, s.Loads[1].Displacements[8]);

        // Стержень: загружение 2 (индекс 1), сечения 0 и 2; порядок N Qy Qz Mk My Mz; т → Н.
        var bar = s.Loads[1].BarForces;
        Assert.Equal([5], s.Loads[1].BarNums);
        double tf = Ton * 1e3;
        Assert.Equal((0 + 1 + ScadApiForceMapper.BarN / 100.0) * tf, bar[0], 6);
        Assert.Equal((0 + 1 + ScadApiForceMapper.BarQy / 100.0) * tf, bar[1], 6);
        Assert.Equal((0 + 1 + ScadApiForceMapper.BarMk / 100.0) * tf, bar[3], 6);
        Assert.Equal((20 + 1 + ScadApiForceMapper.BarN / 100.0) * tf, bar[6], 6);
        Assert.Equal((20 + 1 + ScadApiForceMapper.BarMz / 100.0) * tf, bar[11], 6);

        // Пластина: напряжения × h, моменты и Q — как есть (т → Н).
        Assert.Equal([7], s.Loads[0].ShellNums);
        var sh = s.Loads[0].ShellForces;
        Assert.Equal((1 + ScadApiForceMapper.ShellNx / 100.0) * 0.2 * tf, sh[0], 6);
        Assert.Equal((1 + ScadApiForceMapper.ShellMx / 100.0) * tf, sh[3], 6);
        Assert.Equal((1 + ScadApiForceMapper.ShellQy / 100.0) * tf, sh[7], 6);
    }

    /// <summary>Нелинейное загружение 2 из трёх шагов: перемещения и усилия последнего шага (строки в ApiGetEffors).</summary>
    [Fact]
    public void Nonlinear_LastStep()
    {
        var d = new ScadDisplacementSet
        {
            Rows =
            [
                new ScadDisplacementRow(1, 1, "П", 0, [1], [0, 0, -1e-3, 0, 0, 0]),
                new ScadDisplacementRow(2, 1, "Н", 1, [1], [0, 0, -1e-3, 0, 0, 0]),
                new ScadDisplacementRow(2, 2, "Н", 2, [1], [0, 0, -2e-3, 0, 0, 0]),
                new ScadDisplacementRow(2, 3, "Н", 5, [1], [0, 0, -5e-3, 0, 0, 0]),
            ],
        };
        var b = ScadResultSnapshotBuilder.Build(d, [Bar(5, points: 2, loads: 4), Bar(6, points: 2, loads: 3)], Catalog,
            new Dictionary<int, double>(), Ton, "", "");

        Assert.Equal(1, b.LoadMismatch);
        var l2 = b.Snapshot.Loads[1];
        Assert.Equal(5, l2.Step);
        Assert.Equal(-5e-3, l2.Displacements[2]);
        Assert.Equal([5], l2.BarNums);
        Assert.Equal((0 + 3 + ScadApiForceMapper.BarN / 100.0) * Ton * 1e3, l2.BarForces[0], 6);
        Assert.Equal((0 + 0 + ScadApiForceMapper.BarN / 100.0) * Ton * 1e3, b.Snapshot.Loads[0].BarForces[0], 6);
    }

    [Fact]
    public void NoDisplacements_LoadsFromForces()
    {
        var b = ScadResultSnapshotBuilder.Build(new ScadDisplacementSet(), [Bar(5)], Catalog,
            new Dictionary<int, double>(), Ton, "", "");

        Assert.Equal([1, 2], b.Snapshot.Loads.Select(l => l.Num));
        Assert.All(b.Snapshot.Loads, l => Assert.Empty(l.NodeNums));
        Assert.All(b.Snapshot.Loads, l => Assert.Equal([5], l.BarNums));
    }
}
