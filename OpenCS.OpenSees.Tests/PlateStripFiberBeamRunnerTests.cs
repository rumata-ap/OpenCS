using CScore;
using CScore.Planar;
using CScore.PlateRebar;
using CScore.PlateStrip;
using OpenCS.OpenSees.CScore.Fragments;
using OpenCS.OpenSees.Tests.Fixtures;
using Xunit;

namespace OpenCS.OpenSees.Tests;

/// <summary>
/// Срез 8c: фибровый стержень OpenSees (сечение из PlateSection) против собственного Ньютона CScore
/// (ConstitutiveIntegration по PlateSection). Без gmsh/shell: сверяются два решения одной и той же
/// балочной постановки.
/// </summary>
public sealed class PlateStripFiberBeamRunnerTests(Xunit.Abstractions.ITestOutputHelper output)
{
    const double LengthM = 6.0;
    const double WidthM = 1.0;
    const double ThicknessM = 0.3;
    static readonly double[] Stations = [0.0, 0.125, 0.25, 0.375, 0.5, 0.625, 0.75, 0.875, 1.0];

    static Material Bilinear(int id, double e, double ry, double eu)
    {
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            E = e, Ry = ry, Ru = ry * 1.1, Ft = ry, Fc = -ry,
            Ec2 = -eu, Et2 = eu, Type = MatType.ReSteelF,
        };
        var m = new Material { Id = id, E = e, Type = MatType.ReSteelF, Tag = $"m{id}" };
        m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
        return m;
    }

    sealed record Fixture(
        PlateSection Section, Dictionary<int, Material> Materials,
        IReadOnlyList<IPlateSectionResponse> Sources, PlateStripBeamAnalogy Analogy);

    static Fixture MakeFixture(double asx = 0.0015)
    {
        var concrete = Bilinear(1, 30_000_000.0, 20_000.0, 0.01);
        var steel = Bilinear(2, 200_000_000.0, 400_000.0, 0.02);
        var section = new PlateSection
        {
            H = ThicknessM, NLayers = 12, TensionConcrete = true, PlateModel = "layered",
            ConcreteMaterialId = 1, RebarMaterialId = 2,
            RebarLayers =
            [
                new PlateRebarLayer { InputMode = "direct", Asx = asx, Zsx = -0.12 },
                new PlateRebarLayer { InputMode = "direct", Asx = asx / 2, Zsx = 0.12 },
            ]
        };
        var cDiagram = concrete.GetDiagramms(DiagrammType.L2)![CalcType.C];
        var sDiagram = steel.GetDiagramms(DiagrammType.L2)![CalcType.C];
        var source = new PlateSectionLiveResponse(section, cDiagram, sDiagram);
        var analogy = new PlateStripBeamAnalogy
        {
            Id = "fiber", SourceRegionId = 1, ExplicitWidthM = WidthM, Fingerprint = "fiber",
            Geometry = new PlateStripGeometry { LengthM = LengthM }
        };
        return new(section, new() { [1] = concrete, [2] = steel }, [source, source], analogy);
    }

    static StripLoad Udl(double qKnPerM) => new()
    {
        SourceTag = "q", Kind = StripLoadKind.DistributedUniform, QzKnM = qKnPerM
    };

    async Task<(StripBeamNonlinearSolveResult Own, PlateStripFiberBeamResult Fiber)> RunBothAsync(
        Fixture f, StripBeamSupportScheme scheme, IReadOnlyList<StripLoad> loads,
        KnownEndActions? endActions = null, IReadOnlyList<StripPrescribedDisplacement>? prescribed = null)
    {
        var newton = new StripNewtonOptions(LoadSteps: 8, MaxIterations: 60);
        prescribed ??= [];
        var own = StripBeamNonlinearModel.Solve(
            StripSectionSourceGrid.Uniform(f.Sources, Stations.Length - 1), WidthM, LengthM, Stations, scheme,
            new StripLoadSet(loads), endActions, newton, prescribed);
        Assert.True(own.IsCalculable, string.Join("; ", own.Diagnostics.Select(d => d.Message)));

        var fiber = await PlateStripFiberBeamRunner.RunAsync(
            f.Section, new PlateRebarField([], []), f.Analogy, Stations, f.Sources.Count, scheme, loads,
            endActions, prescribed, newton.LoadSteps,
            new PlateStripFiberCheck { Materials = f.Materials },
            OpenSeesTestExecutable.ResolveOrSkip(), CancellationToken.None);
        Assert.True(fiber.IsConverged, string.Join("; ", fiber.Diagnostics.Select(d => d.Message)));
        output.WriteLine(
            $"M_own={string.Join(", ", own.StationResultants.Select(r => r[1].ToString("F2")))}");
        output.WriteLine(
            $"M_fib={string.Join(", ", fiber.StationResultants.Select(r => r[1].ToString("F2")))}");
        output.WriteLine($"w_own={MaxDeflection(own.Displacements):E4}  w_fib={fiber.MaxDeflectionM:E4}");
        return (own, fiber);
    }

    static double MaxMomentMismatch(IReadOnlyList<double[]> a, IReadOnlyList<double[]> b)
    {
        double scale = a.Concat(b).Max(r => Math.Abs(r[1]));
        return a.Zip(b, (x, y) => Math.Abs(x[1] - y[1]) / scale).Max();
    }

    static double MaxDeflection(double[] displacements)
    {
        double max = 0.0;
        for (int dof = 2; dof < displacements.Length; dof += StripBeamElement.DofPerNode)
            max = Math.Max(max, Math.Abs(displacements[dof]));
        return max;
    }

    [Fact]
    public async Task SimplySupported_Elastic_MomentSignAndMagnitudeMatchOwnSolver()
    {
        var f = MakeFixture();
        const double q = -10.0;
        var (own, fiber) = await RunBothAsync(f, StripBeamSupportScheme.SimplySupported, [Udl(q)]);

        int mid = Stations.Length / 2;
        double analytic = Math.Abs(q) * LengthM * LengthM / 8.0;
        Assert.Equal(analytic, Math.Abs(own.StationResultants[mid][1]), 0);
        // Знак — часть контракта: фибровая эпюра совпадает с эпюрой собственной балки, не по модулю.
        Assert.Equal(own.StationResultants[mid][1], fiber.StationResultants[mid][1], 0);
        Assert.True(MaxMomentMismatch(own.StationResultants, fiber.StationResultants) < 0.01);
        Assert.InRange(fiber.MaxDeflectionM / MaxDeflection(own.Displacements), 0.99, 1.01);
        // Прогиб вниз при q < 0: знак перемещения согласован.
        Assert.True(fiber.StationDeflections[mid] < 0.0);
        Assert.True(own.Displacements[mid * StripBeamElement.DofPerNode + 2] < 0.0);
    }

    [Fact]
    public async Task SimplySupported_BeyondCracking_AgreesWithOwnSolver()
    {
        var f = MakeFixture();
        // M = q·L²/8 = 288 кН·м при q = 64: заметно выше момента начала текучести бетона (~300 кН·м
        // для b·h²/6·Ry с учётом арматуры) — работает нелинейная ветвь диаграмм.
        var (own, fiber) = await RunBothAsync(f, StripBeamSupportScheme.SimplySupported, [Udl(-120.0)]);

        Assert.True(MaxMomentMismatch(own.StationResultants, fiber.StationResultants) < 0.02,
            $"расхождение эпюр {MaxMomentMismatch(own.StationResultants, fiber.StationResultants):P2}");
        double ratio = fiber.MaxDeflectionM / MaxDeflection(own.Displacements);
        Assert.InRange(ratio, 0.97, 1.03);

        // Нелинейность реально включена: прогиб больше упругого линейного пересчёта.
        var (_, elastic) = await RunBothAsync(f, StripBeamSupportScheme.SimplySupported, [Udl(-12.0)]);
        Assert.True(fiber.MaxDeflectionM > 10.0 * elastic.MaxDeflectionM * 1.02,
            "при десятикратной нагрузке прогиб обязан расти быстрее, чем линейно");
    }

    [Fact]
    public async Task Fixed_BothEnds_MomentRedistributionAgreesWithOwnSolver()
    {
        var f = MakeFixture();
        var scheme = new StripBeamSupportScheme(
            StripBeamEndCondition.Fixed, StripBeamEndCondition.Fixed, StripAxialRestraint.StartOnly);
        var (own, fiber) = await RunBothAsync(f, scheme, [Udl(-170.0)]);

        Assert.True(MaxMomentMismatch(own.StationResultants, fiber.StationResultants) < 0.03,
            $"расхождение эпюр {MaxMomentMismatch(own.StationResultants, fiber.StationResultants):P2}");
        Assert.InRange(fiber.MaxDeflectionM / MaxDeflection(own.Displacements), 0.95, 1.05);
    }

    [Fact]
    public async Task EndActionsAndSupportSettlement_AreCarriedToFiberModel()
    {
        var f = MakeFixture();
        var endActions = new KnownEndActions(StartMy: 20.0, EndMy: 20.0);
        var settlement = new[] { new StripPrescribedDisplacement(NodeIndex: Stations.Length - 1, Dof: 2, Value: -0.01) };
        var (own, fiber) = await RunBothAsync(
            f, StripBeamSupportScheme.SimplySupported, [Udl(-20.0)], endActions, settlement);

        Assert.True(MaxMomentMismatch(own.StationResultants, fiber.StationResultants) < 0.03);
    }

    [Fact]
    public async Task PartialAndPointLoads_AreMappedToElements()
    {
        var f = MakeFixture();
        StripLoad[] loads =
        [
            new() { SourceTag = "patch", Kind = StripLoadKind.DistributedUniform,
                    StationStartFraction = 0.2, StationEndFraction = 0.6, QzKnM = -30.0 },
            new() { SourceTag = "p-mid-element", Kind = StripLoadKind.Point, StationFraction = 0.8, PzKn = -40.0 },
            new() { SourceTag = "p-at-station", Kind = StripLoadKind.Point, StationFraction = 0.375, PzKn = -25.0 },
        ];
        var (own, fiber) = await RunBothAsync(f, StripBeamSupportScheme.SimplySupported, loads);

        Assert.True(MaxMomentMismatch(own.StationResultants, fiber.StationResultants) < 0.03,
            $"расхождение эпюр {MaxMomentMismatch(own.StationResultants, fiber.StationResultants):P2}");
        Assert.InRange(fiber.MaxDeflectionM / MaxDeflection(own.Displacements), 0.97, 1.03);
    }

    /// <summary>Переменное армирование по пролёту: у зоны на правой половине втрое больше арматуры.
    /// Собственная балка получает поэлементные источники, фибровая — те же сечения из поля.</summary>
    [Fact]
    public async Task ZonedRebar_DifferentSectionsPerElement_AgreesWithOwnSolver()
    {
        var f = MakeFixture(asx: 0.001);
        var analogy = new PlateStripBeamAnalogy
        {
            Id = "zoned", SourceRegionId = 1, ExplicitWidthM = WidthM, Fingerprint = "zoned",
            Geometry = new PlateStripGeometry
            {
                CenterLine = [new(0, 0), new(LengthM, 0)],
                LeftBoundary = [new(0, WidthM / 2), new(LengthM, WidthM / 2)],
                RightBoundary = [new(0, -WidthM / 2), new(LengthM, -WidthM / 2)],
                Polygon = [new(0, -WidthM / 2), new(LengthM, -WidthM / 2), new(LengthM, WidthM / 2), new(0, WidthM / 2)],
                LengthM = LengthM
            }
        };
        var zone = new RebarZone
        {
            Name = "right-half", Face = RebarFace.MinusN, Operation = RebarZoneOperation.Replace, Priority = 1,
            Polygon =
            [
                new() { U = LengthM / 2, V = -2 }, new() { U = LengthM + 1, V = -2 },
                new() { U = LengthM + 1, V = 2 }, new() { U = LengthM / 2, V = 2 },
            ],
            Layout = new PlateRebarLayer { InputMode = "direct", Asx = 0.003, Zsx = -0.12, Face = RebarFace.MinusN }
        };
        var baseLayout = f.Section.RebarLayers
            .Select(l => { var c = l.Clone(); c.Face = l.Zsx < 0 ? RebarFace.MinusN : RebarFace.PlusN; return c; })
            .ToList();
        var field = new PlateRebarField(baseLayout, [zone]);

        var layouts = PlateStripFiberLayouts.Resolve(field, analogy, Stations, f.Sources.Count);
        Assert.True(layouts.IsCalculable);

        var concrete = f.Materials[1];
        var steel = f.Materials[2];
        var cDiagram = concrete.GetDiagramms(DiagrammType.L2)![CalcType.C];
        var sDiagram = steel.GetDiagramms(DiagrammType.L2)![CalcType.C];
        var rows = layouts.PerElement
            .Select(element => (IReadOnlyList<IPlateSectionResponse>)element.Select(layers =>
            {
                var variant = f.Section.CloneForCalc();
                variant.RebarLayers = layers.Select(l => l.Clone()).ToList();
                return (IPlateSectionResponse)new PlateSectionLiveResponse(variant, cDiagram, sDiagram);
            }).ToList())
            .ToList();
        Assert.NotEqual(
            layouts.PerElement[0][0].Sum(l => l.Asx), layouts.PerElement[^1][0].Sum(l => l.Asx));

        var scheme = StripBeamSupportScheme.SimplySupported;
        StripLoad[] loads = [Udl(-110.0)];
        var newton = new StripNewtonOptions(LoadSteps: 8, MaxIterations: 60);
        var own = StripBeamNonlinearModel.Solve(
            new StripSectionSourceGrid(rows), WidthM, LengthM, Stations, scheme, new StripLoadSet(loads),
            null, newton, []);
        Assert.True(own.IsCalculable);

        var fiber = await PlateStripFiberBeamRunner.RunAsync(
            f.Section, field, analogy, Stations, f.Sources.Count, scheme, loads, null, [], newton.LoadSteps,
            new PlateStripFiberCheck { Materials = f.Materials },
            OpenSeesTestExecutable.ResolveOrSkip(), CancellationToken.None);
        Assert.True(fiber.IsConverged, string.Join("; ", fiber.Diagnostics.Select(d => d.Message)));

        Assert.True(MaxMomentMismatch(own.StationResultants, fiber.StationResultants) < 0.02);
        Assert.InRange(fiber.MaxDeflectionM / MaxDeflection(own.Displacements), 0.97, 1.03);
    }
}
