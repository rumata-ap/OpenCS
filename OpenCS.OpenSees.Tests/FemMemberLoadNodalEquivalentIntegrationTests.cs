using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Services;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using OpenCS.OpenSees.Tests.Fixtures;
using Xunit.Abstractions;

namespace OpenCS.OpenSees.Tests;

/// <summary>
/// Живые эталоны перевода нагрузок стержня в эквивалентные узловые (opt-in, OpenSees.exe). Модель
/// собирается напрямую и запускается <see cref="FemNonlinearAnalysisService"/> без резолвера: прогон с
/// <c>eleLoad</c> и прогон после <see cref="FemMemberLoadNodalEquivalent.Convert"/> + поправка.
/// </summary>
public sealed class FemMemberLoadNodalEquivalentIntegrationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task LinearTransf_FullLengthLoads_ConvertedWithCorrectionMatchesEleLoad()
    {
        // Знак поправки: при geomTransf Linear, упругом сечении и равномерных нагрузках на всю длину
        // (гладкая эпюра — квадратура forceBeamColumn точна) узловые эквиваленты + поправка воспроизводят
        // eleLoad. Трапеция на всю длину здесь намеренно не используется: OpenSees при aOverL = 0,
        // bOverL = 1 молча считает её равномерной с начальным значением (найдено 23.09.2026) — эталон
        // с eleLoad был бы неверен сам.
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        var model = InclinedCantilever(
            [
                new FemLinearDistributedLoad(1, WyStart: -800, WzStart: 300, WxStart: 150, WyEnd: -800, WzEnd: 300, WxEnd: 150, AOverL: 0, BOverL: 1),
                new FemLinearDistributedLoad(2, WyStart: 500, WzStart: -250, WxStart: -60, WyEnd: 500, WzEnd: -250, WxEnd: -60, AOverL: 0, BOverL: 1),
            ], []);

        var deviations = await CompareEleLoadWithConverted(executable, model);

        // Допуск 1e-5: recorder нелинейного генератора пишет без -precision, т.е. 6 значащих цифр.
        foreach (var (kind, relative) in deviations)
            Assert.True(relative <= 1e-5, $"{kind}: относительное расхождение {relative:G6}");
    }

    [Fact]
    public async Task LinearTransf_PartialAndPointLoads_ConvertedWithCorrectionCloseToEleLoad()
    {
        // Частичная трапеция и сосредоточенная сила дают излом эпюры внутри КЭ: 5 точек Лобатто
        // forceBeamColumn интегрируют гибкость с eleLoad неточно (порядка 1e-3), а узловые эквиваленты
        // дают точные узловые перемещения Эрмита. Поэтому допуск 1e-2 — проверка знака, не точности.
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        var model = InclinedCantilever(
            [new FemLinearDistributedLoad(2, WyStart: -200, WzStart: 300, WxStart: -60, WyEnd: 700, WzEnd: 900, WxEnd: 90, AOverL: 0.2, BOverL: 0.7)],
            [new FemLinearPointLoad(2, Py: -400, Pz: 250, Px: 120, XOverL: 0.4)]);

        var deviations = await CompareEleLoadWithConverted(executable, model);

        foreach (var (kind, relative) in deviations)
        {
            output.WriteLine($"{kind}: относительное расхождение {relative:G4}");
            Assert.True(relative <= 1e-2, $"{kind}: относительное расхождение {relative:G6}");
        }
    }

    /// <summary>Наклонная консоль из двух КЭ (узел 1 заделан), упругое сечение, β = 20°.</summary>
    static FemNonlinearModel InclinedCantilever(IReadOnlyList<FemLinearDistributedLoad> distributed, IReadOnlyList<FemLinearPointLoad> points)
    {
        var i = new FemLinearNode(1, 0, 0, 0, [true, true, true, true, true, true]);
        var m = new FemLinearNode(2, 1.5, 1.0, 0.5, new bool[6]);
        var j = new FemLinearNode(3, 3.0, 2.0, 1.0, new bool[6]);
        var vecxz = FemLocalAxis.Vecxz(i, j, 20);
        return new FemNonlinearModel
        {
            Nodes = [i, m, j],
            Sections = new Dictionary<int, OpenSeesSectionModel> { [1] = ElasticSection(0.5, 0.25, 2e8, 0.01, 1e6) },
            Elements = [new FemNonlinearElement(1, 1, 2, 1, 5, vecxz), new FemNonlinearElement(2, 2, 3, 1, 5, vecxz)],
            Stages =
            [
                new FemNonlinearStage
                {
                    Tag = "s", LoadFactorStep = 0.25, MaxLoadFactor = 1,
                    Loads = [new FemLinearNodalLoad(3, 100, 0, -200, 0, 0, 0)],
                    DistributedLoads = distributed, PointLoads = points
                }
            ],
            GeomTransfKind = "Linear",
            Policy = new NonlinearAnalysisPolicy { RefinementDivisions = 10, Tolerance = 1e-10, MaxIterations = 30 }
        };
    }

    /// <summary>Максимальные относительные расхождения по видам на всех шагах: eleLoad против узловых эквивалентов с поправкой.</summary>
    async Task<List<(string Kind, double Relative)>> CompareEleLoadWithConverted(string executable, FemNonlinearModel model)
    {
        var withEleLoad = await Run(executable, model);
        var (converted, equivalents) = FemMemberLoadNodalEquivalent.Convert(model);
        var withNodal = FemElementForceCorrection.Apply(await Run(executable, converted), equivalents, "");

        Assert.Equal(withEleLoad.Steps.Count, withNodal.Steps.Count);
        var worst = new Dictionary<string, double>();
        void Track(string kind, IEnumerable<double> expected, IEnumerable<double> actual)
        {
            var e = expected.ToArray();
            var a = actual.ToArray();
            Assert.Equal(e.Length, a.Length);
            double scale = Math.Max(e.Max(Math.Abs), 1e-12);
            double relative = e.Zip(a, (x, y) => Math.Abs(x - y)).Max() / scale;
            worst[kind] = Math.Max(worst.GetValueOrDefault(kind), relative);
        }
        for (int s = 0; s < withEleLoad.Steps.Count; s++)
        {
            var a = withEleLoad.Steps[s];
            var b = withNodal.Steps[s];
            Assert.True(a.Converged && b.Converged);
            Track("перемещения", a.Displacements.SelectMany(d => new[] { d.Ux, d.Uy, d.Uz }), b.Displacements.SelectMany(d => new[] { d.Ux, d.Uy, d.Uz }));
            Track("повороты", a.Displacements.SelectMany(d => new[] { d.Rx, d.Ry, d.Rz }), b.Displacements.SelectMany(d => new[] { d.Rx, d.Ry, d.Rz }));
            Track("концевые силы", a.ElementForces.SelectMany(Forces), b.ElementForces.SelectMany(Forces));
            Track("концевые моменты", a.ElementForces.SelectMany(Moments), b.ElementForces.SelectMany(Moments));
        }
        return worst.Select(p => (p.Key, p.Value)).ToList();
    }

    [Fact]
    public async Task Corotational_CorrectionMovesCoarseModelTowardFineReference()
    {
        // Измерение приближения, не гарантия: консоль с заметным поворотом конца, грубая модель (1 КЭ)
        // против мелкой (16 КЭ, у неё доля нагрузки внутри КЭ мала). Поправка должна приблизить грубую
        // модель к эталону по моменту в заделке.
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        const double L = 2.0, E = 2e8, c = 0.05, fiberArea = 0.01;
        double ei = E * 4 * fiberArea * c * c;
        double q = 0.15 * 6 * ei / (L * L * L);                   // θ_конца ≈ 0,15 рад по линейной теории

        var (coarse, coarseEquivalents) = FemMemberLoadNodalEquivalent.Convert(Cantilever(1, L, q, c, fiberArea, E));
        var (fine, fineEquivalents) = FemMemberLoadNodalEquivalent.Convert(Cantilever(16, L, q, c, fiberArea, E));
        var coarseRaw = await Run(executable, coarse);
        var coarseCorrected = FemElementForceCorrection.Apply(coarseRaw, coarseEquivalents, "");
        var fineCorrected = FemElementForceCorrection.Apply(await Run(executable, fine), fineEquivalents, "");

        Assert.Equal(1.0, coarseRaw.Steps[^1].LoadFactor, 9);
        Assert.Equal(1.0, fineCorrected.Steps[^1].LoadFactor, 9);
        double rotation = coarseRaw.Steps[^1].Displacements.Single(d => d.NodeTag == 2).Ry;
        double reference = RootMoment(fineCorrected);
        double eCorrected = Math.Abs(RootMoment(coarseCorrected) - reference);
        double eRaw = Math.Abs(RootMoment(coarseRaw) - reference);
        output.WriteLine($"q = {q:G6} Н/м, поворот конца {rotation:G4} рад; M_заделки: эталон {reference:G6}, " +
            $"с поправкой {RootMoment(coarseCorrected):G6} (погр. {eCorrected / Math.Abs(reference):P2}), " +
            $"без поправки {RootMoment(coarseRaw):G6} (погр. {eRaw / Math.Abs(reference):P2})");

        Assert.True(eCorrected < eRaw, $"поправка не приблизила: {eCorrected} ≥ {eRaw}");
    }

    static double RootMoment(FemNonlinearResult result) => result.Steps[^1].ElementForces.Single(f => f.ElemTag == 1).Mzi;

    /// <summary>Горизонтальная консоль вдоль X из n КЭ, заделка в узле 1, поперечная Wy по всей длине.</summary>
    static FemNonlinearModel Cantilever(int n, double length, double q, double c, double fiberArea, double e)
    {
        var nodes = Enumerable.Range(0, n + 1)
            .Select(k => new FemLinearNode(k + 1, length * k / n, 0, 0, k == 0 ? [true, true, true, true, true, true] : new bool[6]))
            .ToList();
        var vecxz = FemLocalAxis.Vecxz(nodes[0], nodes[^1]);
        return new FemNonlinearModel
        {
            Nodes = nodes,
            Sections = new Dictionary<int, OpenSeesSectionModel> { [1] = ElasticSection(c, fiberArea, e, 0.1, 1e4) },
            Elements = Enumerable.Range(1, n).Select(k => new FemNonlinearElement(k, k, k + 1, 1, 5, vecxz)).ToList(),
            Stages =
            [
                new FemNonlinearStage
                {
                    Tag = "q", LoadFactorStep = 0.05, MaxLoadFactor = 1,
                    DistributedLoads = Enumerable.Range(1, n).Select(k => new FemLinearDistributedLoad(k, q, 0, 0, q, 0, 0, 0, 1)).ToList()
                }
            ],
            GeomTransfKind = "Corotational",
            Policy = new NonlinearAnalysisPolicy { RefinementDivisions = 10, Tolerance = 1e-10, MaxIterations = 50 }
        };
    }

    /// <summary>Упругое сечение из 4 угловых фибр (±c, ±c) площадью area; огибающая до деформации limit.</summary>
    static OpenSeesSectionModel ElasticSection(double c, double area, double e, double limit, double gj) => new()
    {
        Materials =
        [
            new OpenSeesMaterialDefinition
            {
                Tag = 1,
                PositiveEnvelope = [new EnvelopePoint(0, 0), new EnvelopePoint(limit, e * limit)],
                NegativeEnvelope = [new EnvelopePoint(-limit, -e * limit), new EnvelopePoint(0, 0)]
            }
        ],
        Fibers = [new(-c, -c, area, 1), new(-c, c, area, 1), new(c, -c, area, 1), new(c, c, area, 1)],
        GJ = gj
    };

    static IEnumerable<double> Forces(FemElementEndForces f) => [f.Ni, f.Qyi, f.Qzi, f.Nj, f.Qyj, f.Qzj];
    static IEnumerable<double> Moments(FemElementEndForces f) => [f.Mxi, f.Myi, f.Mzi, f.Mxj, f.Myj, f.Mzj];

    static async Task<FemNonlinearResult> Run(string executable, FemNonlinearModel model)
    {
        string root = Path.Combine(Path.GetTempPath(), "opencs-nodal-equivalent", Guid.NewGuid().ToString("N"));
        try
        {
            var result = await new FemNonlinearAnalysisService(new FemNonlinearTclGenerator(), new OpenSeesProcessRunner(),
                    new OpenSeesArtifactStore(root), new FemNonlinearResultParser())
                .RunAsync(model, new OpenSeesRunRequest
                {
                    ExecutablePath = executable, WorkingDirectory = Path.GetTempPath(), Timeout = TimeSpan.FromSeconds(60)
                }, CancellationToken.None);
            Assert.True(result.Status == "ok", $"status={result.Status}; {string.Join(" | ", result.Diagnostics)}");
            return result;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
