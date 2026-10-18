using CScore;
using CScore.Sp63.CrackWidth;
using CScore.Sp63.Normal;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using Xunit;

namespace OpenCS.Tests.FemResult;

/// <summary>
/// Ширина раскрытия трещин стержней по результату CSfea (спека НДС сечений, срез 4): упрощённая проверка стержней в
/// точках i, ½, j — по плоскостям изгиба, длительная часть долей или своими усилиями.
/// </summary>
public sealed class FemCsfeaBarCrackWidthsTests
{
    static MaterialChars Concrete(CalcType ct) => new()
    {
        Type = MatType.Concrete, TypeCalc = ct, Fc = -18500, Ft = 1550, E = 30_000_000,
        Ec0 = -0.002, Ec1 = -0.00029, Ec1Red = -0.0015, Ec2 = -0.0035,
        Et0 = 0.0001, Et1 = 0.000021, Et1Red = 0.00008, Et2 = 0.00015,
    };

    static MaterialChars Rebar(CalcType ct) => new()
    {
        Type = MatType.ReSteelF, TypeCalc = ct, Fc = -500000, Ft = 500000, E = 200_000_000, Ec2 = -0.0035, Et2 = 0.025,
    };

    static readonly CalcType[] Calcs = [CalcType.C, CalcType.CL, CalcType.N, CalcType.NL];

    /// <summary>Прямоугольник b × h (X — ширина, Y — высота) из B25, по стержню A500 ⌀20 в каждом углу.</summary>
    static CrossSection Rect(double b, double h, double a)
    {
        var concreteMat = new Material { Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000, MaterialChars = Calcs.Select(Concrete).ToList() };
        var rebarMat = new Material { Id = 2, Tag = "A500", Type = MatType.ReSteelF, E = 200_000_000, MaterialChars = Calcs.Select(Rebar).ToList() };
        var concrete = new MaterialArea
        {
            Id = 1, Tag = "бетон", Category = AreaCategory.Region, Material = concreteMat, MaterialId = 1, DiagrammType = DiagrammType.L3,
            Hull = new Contour([-b / 2, b / 2, b / 2, -b / 2, -b / 2], [-h / 2, -h / 2, h / 2, h / 2, -h / 2], "hull"),
        };
        concrete.SetWKT();
        concrete.SliceXY(nx: 10, ny: 20);
        var bars = new MaterialArea
        {
            Id = 2, Tag = "арматура", Category = AreaCategory.RebarGroup, Material = rebarMat, MaterialId = 2,
            DiagrammType = DiagrammType.L2, HostArea = concrete, HostAreaId = 1,
        };
        foreach (double x in new[] { -b / 2 + a, b / 2 - a })
            foreach (double y in new[] { -h / 2 + a, h / 2 - a })
                bars.Fibers.Add(Fiber.CreatePoint(0.02, x, y));
        var section = new CrossSection { Id = 1, Tag = "ЖБ", Areas = [concrete, bars] };
        section.ResolveAndBuildDiagramms(rebarDifferentialDiagram: false);
        return section;
    }

    static (double Long, double Short) Direct(CrossSection s, LoadItem load, Sp63NormalAxis axis, double share)
    {
        var r = Sp63CrackWidthChecker.Check(s, load, CalcType.N, new Sp63CrackWidthOptions(Sp63NormalShapeKind.Rectangular, axis,
            1.0, 0.5, 0.3, Mode: Sp63CrackWidthMode.LongAndShort, LongTermShare: share));
        Assert.Equal(Sp63CrackWidthStatus.Calculated, r.Status);
        return (r.Variables["acrc1"], r.Details[1].Applied);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Uniaxial_MatchesSimplifiedBarCheck(bool yAlongLocalY)
    {
        var s = Rect(0.3, 0.5, 0.04);
        var e = new FemCsfeaBarCrackElement(1, s, yAlongLocalY);
        // Момент по высоте сечения (Y CScore): при Y по местной y — Mz КЭ (ось сечения Mx = −Mz), иначе My.
        var forces = yAlongLocalY ? (N: 30e3, My: 0.0, Mz: -150e3) : (N: 30e3, My: 150e3, Mz: 0.0);

        var (aLong, aShort) = FemCsfeaBarCrackWidths.Compute(e, forces, 0.7);

        var expected = Direct(s, new LoadItem { N = 30, Mx = 150 }, Sp63NormalAxis.Mx, 0.7);
        Assert.True(expected.Long > 0);
        Assert.Equal(expected.Long, aLong!.Value, 9);
        Assert.Equal(expected.Short, aShort!.Value, 9);
        Assert.True(aShort > aLong * 0.5);
    }

    [Fact]
    public void Biaxial_LargestOfBendingPlanes()
    {
        var s = Rect(0.3, 0.5, 0.04);
        var e = new FemCsfeaBarCrackElement(1, s, YAlongLocalY: false);

        var (aLong, aShort) = FemCsfeaBarCrackWidths.Compute(e, (0.0, 150e3, 60e3), 0.5);

        var x = Direct(s, new LoadItem { Mx = 150 }, Sp63NormalAxis.Mx, 0.5);
        var y = Direct(s, new LoadItem { My = 60 }, Sp63NormalAxis.My, 0.5);
        Assert.True(x.Long > 0 && y.Long > 0);
        Assert.Equal(Math.Max(x.Long, y.Long), aLong!.Value, 9);
        Assert.Equal(Math.Max(x.Short, y.Short), aShort!.Value, 9);
    }

    [Fact]
    public void LongForces_ProportionalToTotal_EqualShare()
    {
        var e = new FemCsfeaBarCrackElement(1, Rect(0.3, 0.5, 0.04), YAlongLocalY: false);
        var total = (N: 20e3, My: 140e3, Mz: 40e3);

        var byShare = FemCsfeaBarCrackWidths.Compute(e, total, 0.6);
        var byPair = FemCsfeaBarCrackWidths.Compute(e, total, 1.0, (0.6 * total.N, 0.6 * total.My, 0.6 * total.Mz));

        Assert.Equal(byShare.Long!.Value, byPair.Long!.Value, 9);
        Assert.Equal(byShare.Short!.Value, byPair.Short!.Value, 9);
    }

    [Fact]
    public void NoBending_NoValue()
    {
        var e = new FemCsfeaBarCrackElement(1, Rect(0.3, 0.5, 0.04), YAlongLocalY: false);
        var (aLong, aShort) = FemCsfeaBarCrackWidths.Compute(e, (50e3, 0.0, 0.0), 1.0);
        Assert.Null(aLong);
        Assert.Null(aShort);
    }

    /// <summary>
    /// Свободно опёртая плита-балка 1 × 0,2 м с силой в среднем узле, 12 КЭ, 5 шагов (секущий расчёт, вид N; при 6 КЭ и
    /// 4 шагах не сходится — к находке среза 2 о несходимости балки 0,3 × 0,5): ширина раскрытия по полям шага — та же,
    /// что по усилиям точек КЭ; наибольшая — у середины пролёта, у опор трещин нет.
    /// </summary>
    [Fact]
    public void SimpleBeam_FieldsMatchPointForces()
    {
        const double l = 6.0, p = 20e3;
        const int n = 12;
        var rs = new RcBeamSection("ЖБ") { Cross = Rect(1.0, 0.2, 0.03), Calc = CalcType.N, TorsionGJ = 1e7 };
        var m = new RcStructuralModel();
        for (int i = 0; i <= n; i++) m.Nodes.Add(new RcNode(i, l * i / n, 0, 0));
        for (int i = 0; i < n; i++) m.Beams.Add(new RcBeam(10 + i, i, i + 1, rs, [0.0, 1.0, 0.0]));
        m.Supports.Add(new RcSupport(0, 0b001111));
        m.Supports.Add(new RcSupport(n, 0b000110));
        var lc = new RcLoadCase(1, "P");
        lc.Nodal.Add(new RcNodalLoad(n / 2, [0, 0, -p, 0, 0, 0]));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("P", [(1, 1.0)], 5));
        var run = RcSecantAnalysis.Run(m, new RcSecantOptions { BeamShear = false });
        Assert.True(run.Result.Completed, run.Result.Message);
        var f = run.LastConvergedFields()!;

        var elements = FemCsfeaBarCrackWidths.Elements(new FemRcModelResult(m, [], []));
        Assert.Equal(n, elements.Count);
        var widths = FemCsfeaBarCrackWidths.Compute(elements, f, 0.9);

        for (int k = 0; k < f.BeamIds.Length; k++)
        {
            var w = widths[f.BeamIds[k]];
            for (int pt = 0; pt < 3; pt++)
            {
                var direct = FemCsfeaBarCrackWidths.Compute(elements[k], FemCsfeaBarSectionState.ElementForces(f, k, pt), 0.9);
                Assert.Equal(direct.Long, w.Long[pt]);
                Assert.Equal(direct.Short, w.Short[pt]);
            }
        }
        double mid = FemCsfeaBarCrackWidth.Max(widths[10 + n / 2].Short)!.Value;
        Assert.True(mid > 0.05, $"a_crc у середины = {mid}");
        Assert.Equal(0.0, widths[10].Short[0] ?? 0.0, 9);   // у опоры момента нет
        Assert.True(FemCsfeaBarCrackWidth.Max(widths[10].Short)!.Value < mid);

        // Пара длительной части, совпадающая с полной, — как доля 1.
        var pair = FemCsfeaBarCrackWidths.Compute(elements, f, f);
        var full = FemCsfeaBarCrackWidths.Compute(elements, f, 1.0);
        Assert.Equal(full[13].Long[1]!.Value, pair[13].Long[1]!.Value, 9);
    }
}
