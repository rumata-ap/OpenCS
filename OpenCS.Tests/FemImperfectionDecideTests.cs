using System.Text.Json;
using CScore.Fem;
using CScore.Fem.Imperfections;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Второй этап несовершенств в подготовке CSfea (<see cref="FemCsfeaRunner.DecideImperfections"/>, срез 2): предварительный
/// расчёт → знак «авто» и п. 5.3.2(6) → несовершенства модели адаптера заменены, строки отчёта — сведениями адаптера.
/// </summary>
public sealed class FemImperfectionDecideTests
{
    const double E = 30e9, G = 12.5e9, A = 0.16, I = 0.4 * 0.4 * 0.4 * 0.4 / 12, L = 6.0;
    const int N = 8;

    sealed class Props : IFemElementStiffnessSource
    {
        public FemShellStiffness? Shell(FemElement e) => null;
        public FemBarStiffness? Bar(FemElement e) => new(E, G, A, I, I, 2 * I);
        public double? UnitWeight(FemElement e) => null;
        public bool IsWithoutStiffness(FemElement e) => false;
        public double? BarArea(FemElement e) => A;
    }

    static FemRcModelInput Input(double p, double lateral)
    {
        var nodes = Enumerable.Range(0, N + 1).Select(i => new FemMeshNode { NodeTag = (i + 1).ToString(), Z = L * i / N }).ToList();
        var elements = Enumerable.Range(1, N).Select(i => new FemElement
        {
            ElemTag = i.ToString(), ElemType = "beam", NodeIdsJson = JsonSerializer.Serialize(new[] { i, i + 1 }), SourceMemberTag = "C",
        }).ToList();
        var members = new List<FemMember> { new() { ElemTag = "C" } };
        var bows = new[]
        {
            (new FemImperfection { Id = 1, Kind = FemImperfectionKinds.Bow, Tag = "y", ParamsJson = new FemBowParams { MemberTags = ["C"], LOverE0 = 250 }.ToJson() }, 1.0),
            (new FemImperfection { Id = 2, Kind = FemImperfectionKinds.Bow, Tag = "z", ParamsJson = new FemBowParams { MemberTags = ["C"], Plane = "z", LOverE0 = 250 }.ToJson() }, 1.0),
        };
        var resolved = FemImperfectionResolver.Resolve(bows, elements, nodes, members);
        Assert.False(resolved.HasErrors);
        return new FemRcModelInput
        {
            MeshNodes = nodes, MeshElements = elements, Members = members, Properties = new Props(),
            Supports = [new FemMeshNodeSupport { NodeTag = "1", Mask = 0x3F }, new FemMeshNodeSupport { NodeTag = (N + 1).ToString(), Mask = 0b100011 }],
            LoadCases = [new FemLoadCase { Id = 1, Tag = "P" }, new FemLoadCase { Id = 9, Tag = "Imp", Sp20Type = FemSp20Types.Imperfection }],
            MeshNodeLoads =
            [
                new FemMeshNodeLoad { LoadCaseId = 1, MeshNodeTag = (N + 1).ToString(), Fz = -p },
                new FemMeshNodeLoad { LoadCaseId = 1, MeshNodeTag = (N / 2 + 1).ToString(), Fx = lateral },
            ],
            Stages = [new FemRcStage("S", [(1, 1.0)])], Imperfections = resolved.Items,
        };
    }

    static RcSecantOptions Options() => new()
    {
        PhysicallyLinear = true,
        Solver = new CSfea.Core.SecantPicardOptions { Geometric = true, GeometricSecondOrder = true },
    };

    [Fact]
    public void DecideImperfections_ReplacesModelAndReports()
    {
        double ncr = Math.PI * Math.PI * E * I / (L * L);
        var input = Input(0.3 * ncr, -1e3);
        var adapted = FemRcModelAdapter.Adapt(input);
        Assert.False(adapted.HasErrors);
        Assert.Equal(2, adapted.Model.Imperfections.Count);

        var (decided, report) = FemCsfeaRunner.DecideImperfections(input, adapted, Options());
        Assert.True(report.FirstOrder);
        Assert.Equal(2, report.Bows.Count);
        // Обе плоскости: заделка внизу — конец с моментом, N_Ed = 0,3·N_cr > 0,25·N_cr — активны.
        var bows = decided.Model.Imperfections.OfType<RcBow>().ToList();
        Assert.Equal(2, bows.Count);
        Assert.True(bows[0].E0 < 0);                                   // плоскость y — прогиб к −X
        Assert.True(bows[1].E0 > 0);                                   // плоскость z — прогиба нет, «+»
        Assert.Single(decided.Diagnostics, d => d.Code == "imperfections");
        Assert.Contains(decided.Diagnostics, d => d.Code == "imperfection_report" && d.Message.Contains("α_cr"));
        Assert.DoesNotContain(decided.Diagnostics, d => d.IsError);

        var low = Input(0.2 * ncr, -1e3);
        var (lowDecided, lowReport) = FemCsfeaRunner.DecideImperfections(low, FemRcModelAdapter.Adapt(low), Options());
        Assert.Empty(lowDecided.Model.Imperfections);
        Assert.All(lowReport.Bows, b => Assert.False(b.Active));
        Assert.Contains(lowDecided.Diagnostics, d => d.Code == "imperfection_bow_inactive");
        Assert.DoesNotContain(lowDecided.Diagnostics, d => d.Code == "imperfections");
    }
}
