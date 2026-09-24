using CScore;
using CScore.Planar;
using CScore.PlateRebar;
using CScore.PlateStrip;
using OpenCS.Gmsh;
using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.CScore.Fragments;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Tcl;
using OpenCS.OpenSees.Tests.Fixtures;
using Xunit;

namespace OpenCS.OpenSees.Tests;

/// <summary>
/// Срез 7, Task 12: сквозной прогон цепочки срезов 1–7 на реальных gmsh.exe + OpenSees.exe.
///
/// Схема: железобетонная полоса 6 × 2 × 0,3 м, шарнирно опёртая по коротким краям, равномерная
/// поверхностная нагрузка. Фикстура намеренно без начальных усилий в нулевом состоянии — иначе
/// сравнение с линейным путём Срезов 2–6 некорректно по построению (см. A.1 спеки).
/// </summary>
public sealed class PlateStripAnalogyIntegrationTests
{
    const double LengthM = 6.0;
    const double WidthM = 2.0;
    const double ThicknessM = 0.3;
    const double ConcreteEValue = 30_000.0;
    const double ConcreteEPa = ConcreteEValue * 1000.0;

    [Fact]
    public async Task BeforeCracking_ShellAndBeamAgree()
    {
        var (result, root) = await RunAsync(surfaceLoadKnM2: -3.0);
        try
        {
            Assert.True(result.IsConverged,
                string.Join("; ", result.BoundaryDiagnostics));
            Assert.True(result.BeamIsCalculable,
                string.Join("; ", result.DomainDiagnostics.Select(d => d.Message)));
            Assert.DoesNotContain(result.DomainDiagnostics, d => d.IsError);

            // Момент в середине пролёта: статически определимая схема, поэтому обе модели
            // обязаны дать qL²/8 независимо от жёсткости.
            int mid = result.StationFractions.Count / 2;
            double analyticKnM = 3.0 * WidthM * LengthM * LengthM / 8.0;
            Assert.Equal(analyticKnM, Math.Abs(result.BeamResultants[mid][1]), 1);
            Assert.Equal(analyticKnM, Math.Abs(result.ShellResultants[mid][1]), 0);

            Assert.True(result.MaxRelativeMomentMismatch < 0.15,
                $"Расхождение эпюр shell/beam {result.MaxRelativeMomentMismatch:P1} " +
                "превышает инженерный допуск.");
            Assert.True(result.RelativeDeflectionMismatch < 0.25,
                $"Расхождение прогибов {result.RelativeDeflectionMismatch:P1} превышает допуск.");

            // Защита от схлопывания в заглушку.
            Assert.True(result.BeamMaxDeflectionM > 0.0);
            Assert.True(result.ShellMaxDeflectionM > 0.0);
            Assert.NotEqual(0.004, result.BeamMaxDeflectionM);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AfterCracking_BeamIsSofterButKeepsEquilibrium()
    {
        var (linear, linearRoot) = await RunAsync(surfaceLoadKnM2: -3.0);
        var (cracked, crackedRoot) = await RunAsync(surfaceLoadKnM2: -6.0);
        try
        {
            Assert.True(linear.BeamIsCalculable);
            Assert.True(cracked.BeamIsCalculable,
                string.Join("; ", cracked.DomainDiagnostics.Select(d => d.Message)));

            // Нагрузка выросла вдвое; при линейном отклике прогиб вырос бы ровно вдвое.
            // Нелинейный обязан вырасти сильнее.
            double ratio = cracked.BeamMaxDeflectionM / linear.BeamMaxDeflectionM;
            Assert.True(ratio > 2.0,
                $"После трещинообразования прогиб обязан расти быстрее нагрузки, отношение {ratio:F2}.");

            // Момент статически определимой схемы определяется равновесием, а не жёсткостью.
            int mid = cracked.StationFractions.Count / 2;
            double analyticKnM = 6.0 * WidthM * LengthM * LengthM / 8.0;
            Assert.Equal(analyticKnM, Math.Abs(cracked.BeamResultants[mid][1]), 1);
        }
        finally
        {
            if (Directory.Exists(linearRoot)) Directory.Delete(linearRoot, recursive: true);
            if (Directory.Exists(crackedRoot)) Directory.Delete(crackedRoot, recursive: true);
        }
    }

    // ---------- Срез 8a: неразрезная плита, опоры из родителя ----------

    const double SpanM = 6.0;
    const double ContinuousLengthM = 3 * SpanM;

    /// <summary>Срез 8a: полоса среднего пролёта неразрезной плиты, опоры и концевые моменты —
    /// из родителя (сам shell-прогон). Три равных пролёта: опорный момент 0,1·q·L²·b, в пролёте
    /// 0,025·q·L²·b. Нагрузка ниже излома диаграммы — сравниваются одинаковые постановки.</summary>
    [Fact]
    public async Task ContinuousSlab_DerivedSupports_MatchShell()
    {
        const double q = 3.0;
        var (result, root) = await RunContinuousAsync(surfaceLoadKnM2: -q, interfaces: []);
        try
        {
            string Report() => string.Join("; ", result.DomainDiagnostics.Select(d => d.Code + ": " + d.Message)
                .Concat(result.BoundaryDiagnostics).Concat(result.MeshDiagnostics));
            Assert.True(result.IsConverged, Report());
            Assert.True(result.BeamIsCalculable, Report());

            Assert.Equal(
                new StripBeamSupportScheme(StripBeamEndCondition.Pinned, StripBeamEndCondition.Pinned, StripAxialRestraint.StartOnly),
                result.Derivation!.Scheme);
            Assert.Equal(StripSupportKind.Wall, result.Derivation.Start!.Kind);
            Assert.Equal(StripSupportKind.Wall, result.Derivation.End!.Kind);

            // Над опорой растянут верх: в конвенции OpenCS положительный момент растягивает +Z.
            double qL2b = q * SpanM * SpanM * WidthM;
            Assert.True(result.EndActions!.StartMy > 0.0 && result.EndActions.EndMy > 0.0, Report());
            Assert.InRange(result.EndActions.StartMy, 0.075 * qL2b, 0.125 * qL2b);
            Assert.InRange(result.EndActions.EndMy, 0.075 * qL2b, 0.125 * qL2b);
            Assert.Contains(result.DomainDiagnostics, d => d.Code == "plate_strip_end_moments_frozen");

            int mid = result.StationFractions.Count / 2;
            Assert.True(result.BeamResultants[mid][1] < 0.0, "В пролёте растянут низ.");
            Assert.InRange(-result.BeamResultants[mid][1], 0.0175 * qL2b, 0.0325 * qL2b);

            Assert.True(result.MaxRelativeMomentMismatch < 0.15,
                $"Расхождение эпюр {result.MaxRelativeMomentMismatch:P1}.");
            Assert.True(result.RelativeDeflectionMismatch < 0.25,
                $"Расхождение прогибов {result.RelativeDeflectionMismatch:P1}.");

            // Отрицательный контроль в том же прогоне: та же балка без концевых моментов родителя —
            // однопролётная схема, qL²/8 в пролёте вместо 0,025·qL².
            var withoutParent = StripBeamNonlinearModel.Solve(
                StripSectionSourceGrid.Uniform([LiveSource(), LiveSource()], result.StationFractions.Count - 1),
                WidthM, SpanM, result.StationFractions, result.Derivation.Scheme!,
                new StripLoadSet([new StripLoad
                {
                    SourceTag = "q", Kind = StripLoadKind.DistributedUniform, QzKnM = -q * WidthM
                }]),
                options: new StripNewtonOptions(LoadSteps: 4, MaxIterations: 40));
            Assert.True(withoutParent.IsCalculable);
            double ratio = Math.Abs(withoutParent.StationResultants[mid][1]) / Math.Abs(result.ShellResultants[mid][1]);
            Assert.True(ratio > 3.0, $"Без моментов родителя расхождение обязано быть многократным, отношение {ratio:F2}.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Срез 8a: осадка опоры x = 12 без нагрузки. Кинематика доходит и до оболочки (sp
    /// вместо fix на линии стены), и до балки (заданный w конечного узла); моменты от осадки
    /// приходят в балку концевыми моментами родителя.</summary>
    [Fact]
    public async Task ContinuousSlab_SupportSettlement_MatchesShell()
    {
        const double settlement = -0.005;
        var interfaceAtSupport = new StripBoundaryInterface
        {
            Id = "settle",
            StripId = "strip-mid",
            Geometry = new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Curve,
                [new(2 * SpanM, -WidthM / 2), new(2 * SpanM, WidthM / 2)]),
            NormalFromReplacedToRetained = new PlanarVector3(1, 0, 0),
            ModeByDof = PlanarBoundaryModeByDof.None.With(PlanarDofMask.UZ, PlanarBoundaryDofMode.Kinematic),
            KinematicAction = new PlanarBoundaryKinematicAction
            {
                InterfaceId = "settle",
                DofMask = PlanarDofMask.UZ,
                Samples = [new PlanarBoundaryKinematicSample(0, new PlanarVector3(0, 0, settlement), PlanarVector3.Zero)]
            }
        };

        var (result, root) = await RunContinuousAsync(surfaceLoadKnM2: 0.0, interfaces: [interfaceAtSupport]);
        try
        {
            string Report() => string.Join("; ", result.DomainDiagnostics.Select(d => d.Code + ": " + d.Message)
                .Concat(result.BoundaryDiagnostics).Concat(result.MeshDiagnostics));
            Assert.True(result.IsConverged, Report());
            Assert.True(result.BeamIsCalculable, Report());

            int last = result.StationFractions.Count - 1;
            Assert.Equal([new StripPrescribedDisplacement(last, 2, settlement)], result.Prescribed);

            Assert.Equal(Math.Abs(settlement), result.BeamMaxDeflectionM, 9);
            Assert.InRange(result.ShellMaxDeflectionM, 0.85 * Math.Abs(settlement), 1.15 * Math.Abs(settlement));
            Assert.True(result.MaxRelativeMomentMismatch < 0.15,
                $"Расхождение эпюр {result.MaxRelativeMomentMismatch:P1}. {Report()}");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    static PlateSectionLiveResponse LiveSource()
    {
        var section = new PlateSection
        {
            H = ThicknessM, NLayers = 12, TensionConcrete = true,
            PlateModel = "layered", ConcreteMaterialId = 1
        };
        var diagram = BilinearDiagram();
        return new PlateSectionLiveResponse(section, diagram, diagram);
    }

    static StripSupportCandidate WallAt(double u)
    {
        var source = new PlanarBoundarySourceReference("planar_region", $"W{u:G}");
        return new(StripSupportCandidate.BuildId(StripSupportKind.Wall, source, 0), StripSupportKind.Wall,
            new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Curve,
                [new(u, -WidthM / 2), new(u, WidthM / 2)]),
            new bool[6], source);
    }

    static SupportLocus LocusAt(double x) => new()
    {
        Frame = new Frame3D(new PlanarVector3(x, 0, 0),
            Frame3D.Identity.LocalX, Frame3D.Identity.LocalY, Frame3D.Identity.LocalZ)
    };

    /// <summary>Трёхпролётная плита 18 × 2 × 0,3 м на линейных опорах-стенах x = 0, 6, 12, 18
    /// (крайние — на контуре, средние встраиваются в сетку); полоса среднего пролёта 6 → 12.</summary>
    static async Task<(PlateStripAnalogyResult Result, string Root)> RunContinuousAsync(
        double surfaceLoadKnM2, IReadOnlyList<StripBoundaryInterface> interfaces)
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        string root = Path.Combine(Path.GetTempPath(), "opencs-strip-e2e", Guid.NewGuid().ToString("N"));

        var region = PlanarRegion.CreateFromContour(
            new Contour
            {
                X = [0, ContinuousLengthM, ContinuousLengthM, 0],
                Y = [-WidthM / 2, -WidthM / 2, WidthM / 2, WidthM / 2]
            },
            frame: Frame3D.Identity);
        region.Id = 1;

        var built = PlateStripGeometryBuilder.Build("strip-mid", region, LocusAt(SpanM), LocusAt(2 * SpanM), WidthM);
        Assert.True(built.IsCalculable, string.Join("; ", built.Diagnostics.Select(d => d.Message)));

        var section = new PlateSection
        {
            H = ThicknessM, NLayers = 12, TensionConcrete = true,
            PlateModel = "layered", ConcreteMaterialId = 1
        };
        var diagram = BilinearDiagram();
        var source = new PlateSectionLiveResponse(section, diagram, diagram);

        var request = new PlateStripAnalogyRequest
        {
            Region = region,
            Section = section,
            Analogy = built.Analogy!,
            Loads = surfaceLoadKnM2 == 0.0
                ? []
                : [new PlanarLoad
                {
                    Tag = "q", Kind = PlanarLoadKind.Surface,
                    Components = new PlanarVector3(0, 0, surfaceLoadKnM2)
                }],
            MeshSettings = new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Quads),
            StationFractions = [0.0, 0.125, 0.25, 0.375, 0.5, 0.625, 0.75, 0.875, 1.0],
            WidthSources = [source, source],
            NewtonOptions = new StripNewtonOptions(LoadSteps: 4, MaxIterations: 40),
            SupportMode = PlateStripSupportMode.DerivedFromParent,
            ParentSupports = [WallAt(0), WallAt(SpanM), WallAt(2 * SpanM), WallAt(ContinuousLengthM)],
            Interfaces = interfaces
        };

        var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions
        {
            ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe",
            ArtifactRoot = root
        });
        var analysis = new ShellAnalysisRunner(
            new ShellTclGenerator(), new OpenSeesArtifactStore(root),
            new OpenSeesProcessRunner(), new ShellResultParser(), TimeSpan.FromSeconds(240));

        var result = await new PlateStripAnalogyRunner().RunAsync(
            request, mesher, new ConcreteOnlyResolver(), analysis, executable, CancellationToken.None);
        return (result, root);
    }

    static async Task<(PlateStripAnalogyResult Result, string Root)> RunAsync(double surfaceLoadKnM2)
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        string root = Path.Combine(Path.GetTempPath(), "opencs-strip-e2e", Guid.NewGuid().ToString("N"));

        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [0, LengthM, LengthM, 0], Y = [-WidthM / 2, -WidthM / 2, WidthM / 2, WidthM / 2] },
            frame: Frame3D.Identity);
        region.Id = 1;

        var section = new PlateSection
        {
            H = ThicknessM, NLayers = 12, TensionConcrete = true,
            PlateModel = "layered", ConcreteMaterialId = 1
        };
        var diagram = BilinearDiagram();
        var source = new PlateSectionLiveResponse(section, diagram, diagram);

        var request = new PlateStripAnalogyRequest
        {
            Region = region,
            Section = section,
            Analogy = new PlateStripBeamAnalogy
            {
                Id = "strip-e2e", SourceRegionId = 1, ExplicitWidthM = WidthM,
                Fingerprint = "e2e", StripFrame = Frame3D.Identity,
                Geometry = new PlateStripGeometry { LengthM = LengthM }
            },
            Loads =
            [
                new PlanarLoad
                {
                    Tag = "q", Kind = PlanarLoadKind.Surface,
                    Components = new PlanarVector3(0, 0, surfaceLoadKnM2)
                }
            ],
            MeshSettings = new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Quads),
            StationFractions = [0.0, 0.125, 0.25, 0.375, 0.5, 0.625, 0.75, 0.875, 1.0],
            WidthSources = [source, source],
            NewtonOptions = new StripNewtonOptions(LoadSteps: 4, MaxIterations: 40)
        };

        var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions
        {
            ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe",
            ArtifactRoot = root
        });
        var analysis = new ShellAnalysisRunner(
            new ShellTclGenerator(), new OpenSeesArtifactStore(root),
            new OpenSeesProcessRunner(), new ShellResultParser(), TimeSpan.FromSeconds(240));

        var result = await new PlateStripAnalogyRunner().RunAsync(
            request, mesher, new ConcreteOnlyResolver(), analysis, executable, CancellationToken.None);
        return (result, root);
    }

    /// <summary>Билинейный материал с изломом: до него отклик линеен, после — заметно мягче.
    /// В нулевом состоянии усилий нет, поэтому сравнение с линейным путём корректно.</summary>
    static Diagramm BilinearDiagram()
    {
        // Предельный упругий момент секции W·Ry = (2·0,3²/6)·1500 = 45 кН·м, пластический
        // ≈67 кН·м. Нагрузки тестов подобраны относительно этих значений: 3 кН/м² даёт
        // M = 27 кН·м (упругая работа), 6 кН/м² — 54 кН·м (за изломом, но без полной
        // пластификации, при которой касательная вырождается физически корректно).
        const double ry = 1500.0;
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            E = ConcreteEValue, Ry = ry, Ru = ry * 1.1, Ft = ry, Fc = -ry,
            // Предельная деформация обязана быть больше деформации текучести
            // eps_y = Ry/E = 0,05, иначе узлы диаграммы совпадают.
            Ec2 = -0.15, Et2 = 0.15, Type = MatType.ReSteelF,
        };
        var m = new Material { Id = 1, E = ConcreteEValue, Type = MatType.ReSteelF, Tag = "strip-e2e" };
        m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
        return m.GetDiagramms(DiagrammType.L2)![CalcType.C];
    }

    sealed class ConcreteOnlyResolver : IPlateSectionShellMaterialResolver
    {
        public IReadOnlyList<NativeShellMaterialDefinition> ResolveConcrete(int sourceMaterialId) =>
            [new(1, $"concrete:{sourceMaterialId}", new ElasticIsotropicShellMaterialSpec(ConcreteEPa, 0.0))];

        public IReadOnlyList<NativeShellMaterialDefinition> ResolveRebar(int sourceMaterialId) =>
            throw new NotSupportedException("Тест не использует армирование.");
    }
}
