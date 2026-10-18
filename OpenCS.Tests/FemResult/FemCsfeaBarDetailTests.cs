using CScore;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using Xunit;

namespace OpenCS.Tests.FemResult;

/// <summary>
/// Детальные эпюры вдоль стержня по результату CSfea (спека НДС сечений, срез 5): усилия точек по равновесию КЭ с
/// равномерной пролётной нагрузкой, точки Лобатто — по записанной плоскости, промежуточные — досчёт сечения.
/// </summary>
public sealed class FemCsfeaBarDetailTests
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
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void UniformLoad_ForcesFollowStaticsAndPointsMatchSectionState(bool yAlongLocalY, bool psi)
    {
        const double l = 6.0, q = 8e3;
        const int n = 12, points = 11;
        var section = Rect(1.0, 0.2, 0.03, 7.85e-4, 2.5e-4);
        var rs = new RcBeamSection("ЖБ") { Cross = section, Calc = CalcType.N, TorsionGJ = 1e7, CrossYAlongLocalY = yAlongLocalY };
        var m = new RcStructuralModel();
        for (int i = 0; i <= n; i++) m.Nodes.Add(new RcNode(i, l * i / n, 0, 0));
        double[] refVec = yAlongLocalY ? [0.0, 0.0, 1.0] : [0.0, 1.0, 0.0];
        for (int i = 0; i < n; i++) m.Beams.Add(new RcBeam(10 + i, i, i + 1, rs, refVec));
        m.Supports.Add(new RcSupport(0, 0b001111));
        m.Supports.Add(new RcSupport(n, 0b000110));
        var lc = new RcLoadCase(1, "q");
        for (int i = 0; i < n; i++) lc.Beams.Add(new RcBeamLoad(10 + i, [0, 0, -q]));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("q", [(1, 1.0)], 5));

        var run = RcSecantAnalysis.Run(m, new RcSecantOptions { Psi = psi, BeamShear = false });
        var f = run.LastConvergedFields();
        Assert.True(run.Result.Completed, run.Result.Message);
        Assert.NotNull(f);
        Assert.True(f!.HasBeamStrains);

        double le = l / n, mMax = q * l * l / 8 / 1e3;
        int cracked = 0, solved = 0;
        for (int e = 0; e < n; e++)
        {
            var detail = FemCsfeaBarDetail.Compute(section, CalcType.N, tension: true, psi, yAlongLocalY, f, e, le, points);
            Assert.Equal(points, detail.Count);
            // Концы — записанные силы концов КЭ.
            var i0 = FemCsfeaBarSectionState.ToSection(FemCsfeaBarSectionState.ElementForces(f, e, 0), yAlongLocalY);
            var j0 = FemCsfeaBarSectionState.ToSection(FemCsfeaBarSectionState.ElementForces(f, e, 2), yAlongLocalY);
            Assert.Equal(i0.Mx, detail[0].Mx, 9);
            Assert.Equal(j0.Mx, detail[^1].Mx, 9);
            foreach (var p in detail)
            {
                // |M| = q·x·(L − x)/2 — с допуском 1 % наибольшего (силы концов — с допуском сходимости).
                double x = le * (e + p.T);
                Assert.True(Math.Abs(q * x * (l - x) / 2 / 1e3 - Math.Abs(p.Mx)) < 0.01 * mMax, $"КЭ {10 + e}, t = {p.T:0.##}: Mx = {p.Mx:0.###}");
                // Продольная сила — невязка сходимости секущего расчёта в силах концов (до 2–3 % реакции).
                Assert.True(Math.Abs(p.My) < 1e-6 && Math.Abs(p.N) < 0.05 * q * l / 2 / 1e3, $"N = {p.N:0.####}, My = {p.My:0.####}");
                Assert.NotNull(p.State);
                var s = p.State!;
                if (p.Recorded)
                {
                    // Точка Лобатто — то же состояние, что в окне НДС сечения (по записанной плоскости).
                    int so = e * RcSecantStepFields.BeamStrainComponents + 3 * p.LobattoPoint!.Value;
                    var plane = FemCsfeaBarSectionState.Plane(f.BeamStrains[so], f.BeamStrains[so + 1], f.BeamStrains[so + 2], yAlongLocalY);
                    Assert.Equal(plane.ky, s.Plane.ky, 12);
                    Assert.Equal(plane.kz, s.Plane.kz, 12);
                }
                else
                {
                    // Досчёт: интеграл фибр = усилия точки.
                    solved++;
                    Assert.True(s.Discrepancy < 0.01, $"КЭ {10 + e}, t = {p.T:0.##}: Mx {s.Mx:0.###}/{s.RecordedMx:0.###}");
                }
                if (s.Cracked) cracked++;
            }
            // Между соседними точками εs арматуры треснувшего участка меняется плавно: досчёт около записанной середины
            // не отличается от неё больше чем на треть (секущая КЭ — средняя по трём точкам).
            var mid = detail[points / 2].State!;
            foreach (var p in new[] { detail[points / 2 - 1], detail[points / 2 + 1] })
                if (mid.Cracked && p.State!.Cracked && mid.EpsS > 1e-4)
                    Assert.True(Math.Abs(p.State.EpsS - mid.EpsS) < mid.EpsS / 3, $"КЭ {10 + e}: εs {p.State.EpsS:0.#####} против {mid.EpsS:0.#####}");
        }
        Assert.True(cracked > 0 && solved == n * (points - 3));
    }

    /// <summary>
    /// Окно эпюр: цепочка из двух КЭ (второй направлен против обхода) — участки между соседними точками без перемычки
    /// на стыке, пропуск у точки без состояния, маркеры точек Лобатто и несошедшейся точки, пересчёт при смене числа точек.
    /// </summary>
    [Fact]
    public void DetailVm_SegmentsMarkersAndRows()
    {
        var chain = new CScore.Fem.BarChain([new CScore.Fem.BarChainElement(1, 0, 2), new CScore.Fem.BarChainElement(2, 4, 2)], 4);
        var state = new FemCsfeaBarSectionPoint(new Kurvature(), 0, 0, 0, 0, 0, 0, 0.05, -0.001, -10, 0.002, 400, -0.001, 0.002, []);
        var calls = new List<(int, int)>();
        IReadOnlyList<FemCsfeaBarDetailPoint> Compute(int elem, int points)
        {
            calls.Add((elem, points));
            return Enumerable.Range(0, points).Select(k =>
            {
                double t = (double)k / (points - 1);
                bool recorded = k == 0 || k == points / 2 || k == points - 1;
                // У второго КЭ вторая промежуточная точка не сошлась.
                var s = elem == 2 && k == 2 ? null : state with { SigmaS = 100 * elem + 10 * t };
                return new FemCsfeaBarDetailPoint(t, 0, elem * t, 0, recorded, s);
            }).ToList();
        }

        var vm = new OpenCS.ViewModels.FemCsfeaBarDetailVM([chain], Compute, psi: false);
        Assert.DoesNotContain(vm.Quantities, q => q.Quantity == OpenCS.ViewModels.FemCsfeaBarDetailQuantity.Psi);
        Assert.Equal(OpenCS.ViewModels.FemCsfeaBarDetailQuantity.SigmaS, vm.SelectedQuantity.Quantity);
        // 11 точек: у первого КЭ 10 участков, у второго — 10 без двух у несошедшейся точки.
        Assert.Equal(18, vm.Segments.Count);
        Assert.All(vm.Segments, s => Assert.True(s.S0 <= s.S1));
        Assert.DoesNotContain(vm.Segments, s => s.S0 < 2 && s.S1 > 2);
        // Второй КЭ против обхода: его узел i — в s = 4, σs = 200 там и 210 в s = 2.
        Assert.Contains(vm.Segments, s => Math.Abs(s.S1 - 4) < 1e-12 && Math.Abs(s.V1 - 200) < 1e-9);
        Assert.Equal(6, vm.Markers.Count(m => !m.Failed));
        var failed = Assert.Single(vm.Markers, m => m.Failed);
        Assert.Equal((2, 3.6), (failed.ElemNum, Math.Round(failed.S, 9)));
        Assert.Equal(22, vm.Rows.Count);
        Assert.Equal("i", vm.Rows[0].Point);

        vm.Points = 5;
        Assert.Equal(10, vm.Rows.Count);
        vm.Points = 11;
        Assert.Equal(4, calls.Count);   // 11 и 5 точек по двум КЭ; повтор 11 — из кеша
    }

    [Fact]
    public void Forces_EndsEqualRecordedAndUniformLoadInSpan()
    {
        // КЭ длиной 2 м под равномерной нагрузкой qz = −1 кН/м (местные оси) как часть балки: силы концов в равновесии.
        const double len = 2, qz = -1000;
        double mi = 3000, vi = 1500;   // усилия сечения у i: My, Qz
        // Силы конца i (на КЭ) — усилия сечения с обратным знаком; конец j — из равновесия КЭ.
        double fzi = -vi, fyi = 0, myi = -mi;
        double fzj = -fzi - qz * len;
        double myj = -myi - len * fzi - len * len / 2 * qz;
        var f = new RcSecantStepFields
        {
            NodeIds = [], Displacements = [], ShellIds = [], ShellForces = [], ShellStates = [], ShellFlags = [],
            BeamIds = [1], BeamForces = [-10, fyi, fzi, 0, myi, 0, 10, 0, fzj, 0, myj, 0], BeamFlags = [0],
        };
        Assert.Equal(FemCsfeaBarSectionState.ElementForces(f, 0, 0), FemCsfeaBarDetail.Forces(f, 0, len, 0));
        var end = FemCsfeaBarDetail.Forces(f, 0, len, 1);
        Assert.Equal(myj, end.My, 9);
        // В середине — от левого конца My = mi + vi·x − qz·x²/2 (момент силы f_z относительно оси y: x·f_z), то есть
        // среднее концов плюс qz·L²/8.
        var mid = FemCsfeaBarDetail.Forces(f, 0, len, 0.5);
        Assert.Equal(mi + vi * 1 - qz * 1 * 1 / 2, mid.My, 9);
        Assert.Equal((mi + myj) / 2 + qz * len * len / 8, mid.My, 9);
        Assert.Equal(10, mid.N, 9);
    }
}
