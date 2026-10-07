using CScore.Fem;
using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

public sealed class ScadLoadTransferTests
{
    static readonly Dictionary<string, string> Types = new()
    {
        ["1"] = "shell", ["2"] = "shell", ["3"] = "shell", ["10"] = "beam", ["11"] = "beam",
    };

    static ScadLoadRecord R(int qw, int qn, double[] data, params int[] ids) => new(qw, qn, data, ids);

    /// <summary>Единицы проекта: тс и м (1 т = 9,81 кН), чтобы проверить пересчёт.</summary>
    static ScadAnalysisModel Model(params ScadLoadCase[] cases) => new() { LoadCases = [.. cases], ForceUnitN = 9810, LengthUnitM = 1 };

    static int _next;
    static ScadLoadTransferResult Run(ScadAnalysisModel model, IReadOnlyList<FemLoadCase>? cases = null,
        IReadOnlyList<FemElementLoad>? loads = null, IReadOnlyList<FemMeshNodeLoad>? nodeLoads = null)
    {
        _next = 0;
        return ScadLoadTransfer.Transfer(model, Types, cases ?? [], loads ?? [], nodeLoads ?? [], () => --_next);
    }

    [Fact]
    public void MapsKindsDirectionsAndUnits()
    {
        var model = Model(new ScadLoadCase(1, "Нагрузки", [R(0, 3, [-2], 5, 6), R(0, 5, [1], 7)],
        [
            R(16, 3, [0.5], 1, 2),           // давление по Z на пластины
            R(16, 3, [0.5], 3),              // то же значение — сливается с предыдущей записью
            R(56, 3, [-1], 10),              // равномерная на стержень
            R(7, 3, [1, 2, 3, 4], 1),        // по узлам, местная z
            R(15, 1, [-0.2, 0.1, 0.3], 2),   // сосредоточенная на пластину по X
            R(15, 3, [-3, 1.5], 11),         // сосредоточенная на стержень
            R(96, 3, [1.1], 1, 2, 3, 10),    // собственный вес
        ], []));
        var r = Run(model);

        var lc = Assert.Single(r.LoadCases);
        Assert.Equal(("Нагрузки", ScadLoadTransfer.Origin, 1, -1), (lc.Tag, lc.Origin, lc.SourceLoadNum, lc.Id));
        Assert.Equal(0, r.SkippedRecords);

        // Правило знаков SCAD: положительная сила — против оси.
        var pressure = r.ElementLoads.Single(l => l.LoadKind == "uniform" && l.Values[0] < 0);
        Assert.Equal(["1", "2", "3"], pressure.TargetTags);
        Assert.Equal(-0.5 * 9810, pressure.Values[0], 9);
        Assert.Equal(("global", "z"), (pressure.CoordinateSystem, pressure.Axis));

        Assert.Equal(9810, r.ElementLoads.Single(l => l.LoadKind == "uniform" && l.Values[0] > 0).Values[0], 9);
        var nodal = r.ElementLoads.Single(l => l.LoadKind == "nodal");
        Assert.Equal(("local", "z"), (nodal.CoordinateSystem, nodal.Axis));
        Assert.Equal([-9810, -19620, -29430, -39240], nodal.Values.Select(v => Math.Round(v, 6)));
        var shellPoint = r.ElementLoads.Single(l => l.LoadKind == "point" && l.Values.Count == 3);
        Assert.Equal("x", shellPoint.Axis);
        Assert.Equal(0.2 * 9810, shellPoint.Values[0], 9);
        Assert.Equal(1.5, r.ElementLoads.Single(l => l.LoadKind == "point" && l.Values.Count == 2).Values[1], 12);
        var weight = r.ElementLoads.Single(l => l.LoadKind == "self_weight");
        Assert.Equal(1.1, weight.Values[0]);
        Assert.Equal(["1", "2", "3", "10"], weight.TargetTags);

        Assert.Equal(3, r.MeshNodeLoads.Count);
        Assert.Equal(2 * 9810, r.MeshNodeLoads.Single(l => l.MeshNodeTag == "5").Fz, 9);
        Assert.Equal(-9810, r.MeshNodeLoads.Single(l => l.MeshNodeTag == "7").My, 9);
        Assert.All(r.ElementLoads, l => Assert.Equal(lc.Id, l.LoadCaseId));
    }

    [Fact]
    public void UnsupportedKindsGoToReportNotSilently()
    {
        var model = Model(new ScadLoadCase(2, "", [], [R(18, 0, [10, 10, 10, 10], 1), R(55, 3, [1, 0.5], 10),
            R(16, 4, [1], 1), R(16, 3, [1], 99)], [R(216, 3, [1], 1, 2, 3)]));
        var r = Run(model);
        Assert.Equal("L2", r.LoadCases.Single().Tag);
        Assert.Empty(r.ElementLoads);
        Assert.Equal(5, r.SkippedRecords);
        string all = string.Join("\n", r.Report);
        Assert.Contains("температура Qw 18", all);
        Assert.Contains("в долях длины Qw 55", all);
        Assert.Contains("моменты на КЭ", all);
        Assert.Contains("нет в сетке", all);
        Assert.Contains("штамп", all);
    }

    [Fact]
    public void RepeatedTransfer_ReplacesImportedKeepsManualAndCaseIds()
    {
        var model = Model(new ScadLoadCase(1, "СВ", [], [R(96, 3, [1], 1)], []));
        var manualCase = new FemLoadCase { Id = 5, Tag = "СВ" };
        var manualLoad = new FemElementLoad { Id = 1, LoadCaseId = 5 };
        var first = Run(model, [manualCase], [manualLoad]);
        var imported = first.LoadCases.Single(c => c.Origin == ScadLoadTransfer.Origin);
        Assert.Equal("СВ (2)", imported.Tag);
        imported.Id = 8; // сохранено
        foreach (var l in first.ElementLoads.Where(l => l.Origin == ScadLoadTransfer.Origin)) l.LoadCaseId = 8;

        var second = Run(Model(new ScadLoadCase(1, "СВ", [], [R(96, 3, [1.2], 1)], [])), first.LoadCases, first.ElementLoads);
        Assert.Equal(2, second.LoadCases.Count);
        Assert.Equal(0, second.CasesCreated);
        Assert.Contains(manualLoad, second.ElementLoads);
        var weight = second.ElementLoads.Single(l => l.Origin == ScadLoadTransfer.Origin);
        Assert.Equal((8, 1.2), (weight.LoadCaseId, weight.Values[0]));

        var third = Run(Model(), second.LoadCases, second.ElementLoads);
        Assert.Equal([manualLoad], third.ElementLoads);
        Assert.Contains(third.Report, line => line.Contains("в SCAD отсутствует"));
    }
}
