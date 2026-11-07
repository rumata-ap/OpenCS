using System.Text.Json;
using CScore.Fem;
using CScore.Fem.Imperfections;
using Xunit;

namespace CScore.Tests;

/// <summary>
/// Срез 5 несовершенств: элемент «Формы» (<see cref="FemModesParams"/>) — привязка через поиск форм контекста, сумма
/// u₀ = Σ aᵢ·φᵢ с коэффициентом загружения, ошибки (нет постановки, формы устарели, нет формы, смешение с креном —
/// <c>imperfection_modes_mixed</c>), отчёт (строки форм, max|u₀|, α_cr), отпечаток геометрии.
/// </summary>
public class FemImperfectionModesTests
{
    static readonly List<FemMeshNode> Nodes =
    [
        new() { NodeTag = "1", Z = 0 }, new() { NodeTag = "2", Z = 3 }, new() { NodeTag = "3", Z = 6 },
    ];

    static FemElement Beam(int tag, int i, int j) => new()
    {
        ElemTag = tag.ToString(), ElemType = "beam", NodeIdsJson = JsonSerializer.Serialize(new[] { i, j }), SourceMemberTag = "C",
    };

    static readonly List<FemElement> Elements = [Beam(10, 1, 2), Beam(11, 2, 3)];

    /// <summary>Две формы: 1 — полуволна (середина 1), 2 — точка дробления КЭ 10.</summary>
    static FemModesLookup Found(FemModesParams p) => new()
    {
        AnalysisTag = "Длины", CaseTag = "C1", Lambda = [2.5, 7.0],
        Shapes = new Dictionary<int, FemModeShape>
        {
            [1] = new(new Dictionary<int, double[]> { [1] = [0, 0, 0, 0, 0.5, 0], [2] = [1, 0, 0, 0, 0, 0], [3] = [0, 0, 0, 0, -0.5, 0] },
                new Dictionary<int, double[][]> { [10] = [[0.7, 0, 0]] }),
            [2] = new(new Dictionary<int, double[]> { [2] = [0, 0, 0, 0, 1, 0] }, new Dictionary<int, double[][]> { [10] = [[-1, 0, 0]] }),
        }.Where(kv => p.Modes.Any(m => m.Mode == kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value),
    };

    static FemImperfection Modes(params (int Mode, double Mm)[] modes) => new()
    {
        Id = 5, Kind = FemImperfectionKinds.Modes, Tag = "Ф",
        ParamsJson = new FemModesParams { AnalysisId = 7, Modes = modes.Select(m => new FemModeAmplitude { Mode = m.Mode, AmplitudeMm = m.Mm }).ToList() }.ToJson(),
    };

    static FemImperfectionResolution Resolve(IReadOnlyList<(FemImperfection, double)> items, Func<FemModesParams, FemModesLookup>? lookup) =>
        FemImperfectionResolver.Resolve(items, Elements, Nodes, [], new FemImperfectionContext { Modes = lookup });

    [Fact]
    public void Params_RoundTrip()
    {
        var p = new FemModesParams { AnalysisId = 3, CaseIndex = 1, Modes = [new() { Mode = 2, AmplitudeMm = -12.5 }] };
        var back = FemModesParams.Parse(p.ToJson());
        Assert.Equal(3, back.AnalysisId);
        Assert.Equal(1, back.CaseIndex);
        Assert.Equal(-12.5, Assert.Single(back.Modes).AmplitudeMm);
        Assert.Equal("Формы #5", new FemImperfection { Id = 5, Kind = FemImperfectionKinds.Modes }.Label);
    }

    [Fact]
    public void Resolve_SumsModesWithFactor()
    {
        var r = Resolve([(Modes((1, 20), (2, 10)), -0.5)], Found);
        Assert.False(r.HasErrors, string.Join("; ", r.Diagnostics.Select(d => d.Message)));
        var m = Assert.IsType<FemResolvedModes>(Assert.Single(r.Items));
        Assert.Equal(-0.010, m.Nodes[2][0], 12);                       // −0,5·20 мм·1
        Assert.Equal(-0.005, m.Nodes[2][4], 12);                       // поворот формы 2
        Assert.Equal(-0.005, m.Nodes[1][4], 12);                       // поворот формы 1 — тем же множителем
        Assert.Equal(-0.010 * 0.7 + -0.005 * -1, m.BarPoints[10][0][0], 12);
        Assert.Equal(2.5, m.AlphaCr);
        Assert.Equal([1, 2], m.Modes.Select(x => x.Mode));
        Assert.Equal(7.0, m.Modes[1].Lambda);
        Assert.Equal(0.010, m.MaxU0, 12);
    }

    [Fact]
    public void Resolve_Errors()
    {
        Assert.Contains(Resolve([(Modes((1, 20)), 1)], null).Diagnostics, d => d.Code == "imperfection_modes_source");
        Assert.Contains(Resolve([(Modes(), 1)], Found).Diagnostics, d => d.Code == "imperfection_modes_empty");
        Assert.Contains(Resolve([(Modes((3, 20)), 1)], Found).Diagnostics, d => d.Code == "imperfection_modes_missing");
        Assert.Contains(Resolve([(Modes((0, 20)), 1)], Found).Diagnostics, d => d.Code == "imperfection_modes_invalid");
        var stale = Resolve([(Modes((1, 20)), 1)], _ => FemModesLookup.Fail("imperfection_modes_stale", "формы устарели"));
        var d = Assert.Single(stale.Diagnostics);
        Assert.Equal("imperfection_modes_stale", d.Code);
        Assert.True(d.IsError);
        Assert.Contains("«Ф»", d.Message);
    }

    [Fact]
    public void Resolve_ModesWithSway_IsMixedError()
    {
        var sway = new FemImperfection
        {
            Id = 1, Kind = FemImperfectionKinds.Sway, Tag = "Крен",
            ParamsJson = new FemSwayParams { PhiMode = FemSwayParams.PhiManual, PhiInv = 200 }.ToJson(),
        };
        var r = Resolve([(sway, 1), (Modes((1, 20)), 1)], Found);
        Assert.Contains(r.Diagnostics, d => d.Code == "imperfection_modes_mixed" && d.IsError);
        Assert.DoesNotContain(Resolve([(sway, 1)], Found).Diagnostics, d => d.Code == "imperfection_modes_mixed");
    }

    [Fact]
    public void Decide_ReportsModesAndAlphaCr()
    {
        var items = Resolve([(Modes((1, 20), (2, -5)), 1)], Found).Items;
        var decision = FemImperfectionResolver.Decide(items, null, Elements, Nodes);
        var report = decision.Report;
        Assert.Equal(2, report.Modes.Count);
        Assert.Equal(-0.005, report.Modes[1].Amplitude, 12);
        Assert.Equal(0.020, report.MaxU0, 12);
        Assert.Contains("2,5", report.AlphaCr.Replace('.', ','));
        Assert.Contains("< 10", report.AlphaCr);
        Assert.Contains(report.Lines(), l => l.StartsWith("Формы «Ф»", StringComparison.Ordinal) && l.Contains("форма 2"));
        Assert.Empty(decision.Diagnostics);
        Assert.Single(decision.Items);
        Assert.NotNull(report.Apply(items));

        // Без форм: α_cr — переданная строка, иначе «не посчитан».
        Assert.Equal(FemImperfectionResolver.AlphaCrNotComputed, FemImperfectionResolver.Decide([], null, Elements, Nodes).Report.AlphaCr);
        Assert.Equal("α", FemImperfectionResolver.Decide([], null, Elements, Nodes, alphaCr: "α").Report.AlphaCr);
        Assert.Contains("не обязателен", FemImperfectionResolver.AlphaCrLine(12, "Длины", "C1"));
    }

    [Fact]
    public void GeometryFingerprint_IgnoresOrderAndLoads_TracksGeometry()
    {
        var supports = new List<FemMeshNodeSupport> { new() { NodeTag = "1", Mask = 63 } };
        string a = FemGeometryFingerprint.Compute(Nodes, Elements, supports, [], []);
        Assert.Equal(a, FemGeometryFingerprint.Compute(Nodes.AsEnumerable().Reverse().ToList(), Elements.AsEnumerable().Reverse().ToList(), supports, [], []));
        var moved = Nodes.Select(n => new FemMeshNode { NodeTag = n.NodeTag, Z = n.Z, X = n.NodeTag == "2" ? 0.01 : 0 }).ToList();
        Assert.NotEqual(a, FemGeometryFingerprint.Compute(moved, Elements, supports, [], []));
        var hinged = new List<FemElement> { Beam(10, 1, 2), Beam(11, 2, 3) };
        hinged[1].ReleaseJ = 0b110000;
        Assert.NotEqual(a, FemGeometryFingerprint.Compute(Nodes, hinged, supports, [], []));
        Assert.NotEqual(a, FemGeometryFingerprint.Compute(Nodes, Elements, [new() { NodeTag = "1", Mask = 7 }], [], []));
        var axes = new List<FemElement> { Beam(10, 1, 2), Beam(11, 2, 3) };
        axes[0].LocalAxisAngleDeg = 30;                                 // оси выдачи усилий — не геометрия
        Assert.Equal(a, FemGeometryFingerprint.Compute(Nodes, axes, supports, [], []));
    }
}
