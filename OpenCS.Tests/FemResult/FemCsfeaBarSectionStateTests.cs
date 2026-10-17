using CScore;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using Xunit;

namespace OpenCS.Tests.FemResult;

/// <summary>
/// Состояние фибрового сечения стержня по записанным деформациям CSfea (спека НДС сечений, срез 2): усилия интеграла
/// фибр против записанных сил концов КЭ — у свободно опёртой балки с силой в среднем узле (нет пролётной нагрузки,
/// эпюра M по КЭ линейна).
/// </summary>
public sealed class FemCsfeaBarSectionStateTests
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

    /// <summary>Прямоугольник b × h (X — ширина, Y — высота) из B25, по три стержня A500 внизу и вверху.</summary>
    static CrossSection Rect(double b, double h, double a, double asBot, double asTop)
    {
        var concreteMat = new Material { Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000, MaterialChars = Calcs.Select(Concrete).ToList() };
        var rebarMat = new Material { Id = 2, Tag = "A500", Type = MatType.ReSteelF, E = 200_000_000, MaterialChars = Calcs.Select(Rebar).ToList() };
        var concrete = new MaterialArea
        {
            Id = 1, Tag = "бетон", Category = AreaCategory.Region, Material = concreteMat, MaterialId = 1, DiagrammType = DiagrammType.L3,
            Hull = new Contour([-b / 2, b / 2, b / 2, -b / 2, -b / 2], [-h / 2, -h / 2, h / 2, h / 2, -h / 2], "hull"),
        };
        concrete.SetWKT();
        concrete.SliceXY(nx: 6, ny: 40);
        MaterialArea Bars(int id, double area, double y)
        {
            var r = new MaterialArea
            {
                Id = id, Tag = $"арматура {id}", Category = AreaCategory.RebarGroup, Material = rebarMat, MaterialId = 2,
                DiagrammType = DiagrammType.L2, HostArea = concrete, HostAreaId = 1,
            };
            for (int i = 0; i < 3; i++)
            {
                var bar = Fiber.CreatePoint(0.02, -b / 2 + a + i * (b - 2 * a) / 2, y);
                bar.Area = area / 3;
                r.Fibers.Add(bar);
            }
            return r;
        }
        var section = new CrossSection { Id = 1, Tag = "ЖБ", Areas = [concrete, Bars(2, asBot, -h / 2 + a), Bars(3, asTop, h / 2 - a)] };
        section.ResolveAndBuildDiagramms(rebarDifferentialDiagram: false);
        return section;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SimpleBeam_FiberForcesMatchRecordedEndForces(bool yAlongLocalY)
    {
        const double l = 6.0, p = 20e3;
        const int n = 12;
        var section = Rect(1.0, 0.2, 0.03, 7.85e-4, 2.5e-4);
        var rs = new RcBeamSection("ЖБ") { Cross = section, Calc = CalcType.N, TorsionGJ = 1e7, CrossYAlongLocalY = yAlongLocalY };
        var m = new RcStructuralModel();
        for (int i = 0; i <= n; i++) m.Nodes.Add(new RcNode(i, l * i / n, 0, 0));
        // Местная ось z КЭ — по высоте сечения (Y CScore) по умолчанию; при Y по местной y — ось y вертикальна.
        double[] refVec = yAlongLocalY ? [0.0, 0.0, 1.0] : [0.0, 1.0, 0.0];
        for (int i = 0; i < n; i++) m.Beams.Add(new RcBeam(10 + i, i, i + 1, rs, refVec));
        m.Supports.Add(new RcSupport(0, 0b001111));
        m.Supports.Add(new RcSupport(n, 0b000110));
        var lc = new RcLoadCase(1, "P");
        lc.Nodal.Add(new RcNodalLoad(n / 2, [0, 0, -p, 0, 0, 0]));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("P", [(1, 1.0)], 5));

        // Без ψs (εs,crc не записываются): состояние окна (трещина — по показанной плоскости) совпадает с расчётом.
        var run = RcSecantAnalysis.Run(m, new RcSecantOptions { RecordSectionStrains = true, Psi = false, BeamShear = false });
        var f = run.LastConvergedFields();
        Assert.True(run.Result.Completed, run.Result.Message);
        Assert.NotNull(f);
        Assert.True(f!.HasStrains);

        Assert.True(f.HasMatrices);
        int checkedCracked = 0;
        for (int e = 0; e < n; e++)
        {
            var points = Enumerable.Range(0, 3)
                .Select(pt => FemCsfeaBarSectionState.Compute(section, CalcType.N, tension: true, yAlongLocalY, f, e, pt)).ToArray();
            for (int pt = 0; pt < 3; pt++)
            {
                var s = points[pt];
                // Оси и знаки: интеграл фибр по записанной плоскости = секущая точки S_p·ε (полная матрица конечного
                // состояния, оси КЭ), переведённая в оси сечения. Секущая точки — из последней оценки (деформации до
                // последнего обновления матриц), поэтому допуск — 2 % от |M| точки, не меньше 10 % наибольшего момента.
                int mo = e * RcSecantStepFields.BeamMatrixComponents + 11 * pt, so = e * RcSecantStepFields.BeamStrainComponents + 3 * pt;
                var k = f.BeamMatrices;
                double[] eps = [f.BeamStrains[so], f.BeamStrains[so + 1], f.BeamStrains[so + 2]];
                double Row(int r) => k[mo + 3 * r] * eps[0] + k[mo + 3 * r + 1] * eps[1] + k[mo + 3 * r + 2] * eps[2];
                var secant = FemCsfeaBarSectionState.ToSection((Row(0), Row(1), Row(2)), yAlongLocalY);
                double scale = 10 * Math.Max(Math.Abs(s.Mx), 0.1 * p * l / 4 / 1e3);
                Assert.True(Math.Abs(secant.N - s.N) * s.Size < 2e-3 * scale && Math.Abs(secant.Mx - s.Mx) < 2e-3 * scale
                    && Math.Abs(secant.My - s.My) < 2e-3 * scale,
                    $"КЭ {10 + e}, точка {pt}: S·ε = ({secant.N:0.###}; {secant.Mx:0.###}; {secant.My:0.###}), фибры ({s.N:0.###}; {s.Mx:0.###}; {s.My:0.###})");
                // Момент от силы в середине |M| = P/2·min(x, L − x) — Mx сечения (ось Y сечения по высоте в обоих случаях).
                // Силы концов у малонагруженных КЭ отличаются от статики в пределах допуска сходимости (мера работы
                // отнесена к наибольшей по схеме) — сравнение с допуском 1 % наибольшего момента.
                double x = l * (e + pt / 2.0) / n;
                Assert.True(Math.Abs(p / 2 * Math.Min(x, l - x) / 1e3 - Math.Abs(s.RecordedMx)) < 0.01 * p * l / 4 / 1e3,
                    $"КЭ {10 + e}, точка {pt}: |Mx| = {s.RecordedMx:0.###}");
                Assert.True(Math.Abs(s.RecordedMy) < 1e-6);
            }
            // Равновесие с записанными силами — у КЭ, где все точки треснули и сечения близки: секущая КЭ — средняя
            // податливость трёх точек, у КЭ на границе трещин или у порога трещинообразования сечение точки мягче или
            // жёстче среднего, и интеграл фибр отличается от S·ε КЭ (это окно и показывает).
            if (!points.All(s => s.Cracked) || Math.Abs(points[0].RecordedMx) < 20) continue;
            checkedCracked++;
            foreach (var (s, pt) in points.Select((s, i) => (s, i)))
                Assert.True(s.Discrepancy < 0.05, $"КЭ {10 + e}, точка {pt}: N {s.N:0.##}/{s.RecordedN:0.##}, Mx {s.Mx:0.##}/{s.RecordedMx:0.##}, My {s.My:0.##}/{s.RecordedMy:0.##}");
        }
        Assert.True(checkedCracked > 0);
        // В середине пролёта трещина: растянута арматура, сжатая зона — меньше половины высоты, σs < Rs.
        var mid = FemCsfeaBarSectionState.Compute(section, CalcType.N, true, yAlongLocalY, f, n / 2 - 1, 2);
        Assert.True(mid.Cracked);
        Assert.False(mid.Tension);
        Assert.True(mid.EpsS > 0 && mid.SigmaS is > 0 and < 500);
        Assert.True(mid.EpsB < 0 && mid.SigmaB < 0);
        Assert.InRange(mid.CompressedHeight, 0.03, 0.25);
        Assert.Equal(6, mid.Rebar.Count);
        // У опоры трещины нет — растянутый бетон работает.
        Assert.False(FemCsfeaBarSectionState.Compute(section, CalcType.N, true, yAlongLocalY, f, 0, 0).Cracked);
    }
    [Fact]
    public void ElementForces_EndsAndMiddle()
    {
        var f = new RcSecantStepFields
        {
            NodeIds = [], Displacements = [], ShellIds = [], ShellForces = [], ShellStates = [], ShellFlags = [],
            BeamIds = [1], BeamForces = [-10, 0, 0, 0, 4, -6, 10, 0, 0, 0, 2, 8], BeamFlags = [0],
        };
        Assert.Equal((10.0, -4.0, 6.0), FemCsfeaBarSectionState.ElementForces(f, 0, 0));
        Assert.Equal((10.0, 2.0, 8.0), FemCsfeaBarSectionState.ElementForces(f, 0, 2));
        Assert.Equal((10.0, -1.0, 7.0), FemCsfeaBarSectionState.ElementForces(f, 0, 1));
        Assert.Equal((0.01, 0.002, 0.008), FemCsfeaBarSectionState.ToSection((10, 2, 8), false));
        Assert.Equal((0.01, -0.008, 0.002), FemCsfeaBarSectionState.ToSection((10, 2, 8), true));
    }
}
