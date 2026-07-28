using CScore;
using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Services;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using OpenCS.OpenSees.Tests.Fixtures;
using Xunit;

namespace OpenCS.OpenSees.Tests;

public sealed class FemNonlinearIntegrationTests
{
    [Fact]
    public async Task Cantilever_SmallElasticTipLoad_MatchesBeamTheory()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        string root = Path.Combine(Path.GetTempPath(), "opencs-fem-nonlinear-integration", Guid.NewGuid().ToString("N"));

        // Консоль вдоль X, L=2 м; узел 1 — заделка, узел 2 — свободный конец.
        // Сечение: SymmetricElasticSection() — 4 фибры в углах квадрата 1x1 м, E=2e8 Па, Iy=Iz=0.25 м⁴.
        // Нагрузка -1000 Н вдоль Z: изгиб об локальную ось y, макс. деформация фибры ≈ 2e-5 —
        // далеко в пределах линейного участка диаграммы материала (±0.01).
        const double L = 2.0, E = 2e8, Iy = 0.25, P = -1000.0;
        var baseSection = CrossSectionFixtures.SymmetricElasticSection();
        // SymmetricElasticSection() не задаёт GJ (по умолчанию 0 — она используется в 2D section-level
        // тестах, где кручение не участвует). Для 3D forceBeamColumn нужна собственная ручная GJ,
        // иначе агрегированная крутильная жёсткость секции равна нулю и матрица гибкости вырождена.
        var section = new OpenCS.OpenSees.Model.OpenSeesSectionModel
        {
            Materials = baseSection.Materials,
            Fibers = baseSection.Fibers,
            GJ = 1e6
        };
        var model = new FemNonlinearModel
        {
            Nodes =
            [
                new FemLinearNode(1, 0, 0, 0, [true, true, true, true, true, true]),
                new FemLinearNode(2, L, 0, 0, new bool[6]),
            ],
            Sections = new Dictionary<int, OpenCS.OpenSees.Model.OpenSeesSectionModel> { [1] = section },
            Elements = [new FemNonlinearElement(1, 1, 2, SectionTag: 1, NumIntegrationPoints: 5, Vecxz: (0, 0, 1))],
            Stages = [new FemNonlinearStage { Tag = "Стадия 1", Loads = [new FemLinearNodalLoad(2, 0, 0, P, 0, 0, 0)] }],
            LoadFactorStep = 0.25, MaxLoadFactor = 1.0, RefinementDivisions = 10,
            Tolerance = 1e-8, MaxIterations = 30, GeomTransfKind = "Linear"
        };

        try
        {
            var result = await new FemNonlinearAnalysisService(
                new FemNonlinearTclGenerator(),
                new OpenSeesProcessRunner(),
                new OpenSeesArtifactStore(root),
                new FemNonlinearResultParser())
                .RunAsync(model, new OpenSeesRunRequest
                {
                    ExecutablePath = executable,
                    WorkingDirectory = Path.GetTempPath(),
                    Timeout = TimeSpan.FromSeconds(30)
                }, CancellationToken.None);

            Assert.True(result.Status == "ok", $"status={result.Status}; diagnostics={string.Join(" | ", result.Diagnostics)}");
            Assert.Equal(4, result.Steps.Count);
            Assert.All(result.Steps, s => Assert.True(s.Converged));

            var last = result.Steps[^1];
            Assert.InRange(last.LoadFactor, 0.99, 1.01);

            double expectedUz = P * L * L * L / (3.0 * E * Iy);   // ≈ -5.33e-5 м
            double uz = last.Displacements.Single(d => d.NodeTag == 2).Uz;
            Assert.InRange(uz, expectedUz * 1.02, expectedUz * 0.98);

            var reaction = last.Reactions.Single(r => r.NodeTag == 1);
            Assert.InRange(System.Math.Abs(reaction.Rz), 950, 1050);

            double baseMoment = last.ElementForces
                .SelectMany(f => new[] { System.Math.Abs(f.Myi), System.Math.Abs(f.Myj) })
                .Max();
            Assert.InRange(baseMoment, 1900, 2100);   // момент заделки ≈ |P|·L = 2000

            var fiberParser = new FemNonlinearFiberStateParser();
            var locations = fiberParser.ParseLocations(
                Path.Combine(result.ArtifactDirectory!, result.SectionOrderFileName!));
            Assert.Equal(5, locations.Count);
            var firstLocation = locations[0];
            var selectedStates = fiberParser.ParseSection(
                Path.Combine(result.ArtifactDirectory!, result.FiberStateFileName!),
                firstLocation.ElementTag, firstLocation.IntegrationPoint);
            Assert.NotEmpty(selectedStates);
            Assert.Equal(4, selectedStates.Select(s => s.StepIndex).Distinct().Count());
            Assert.DoesNotContain('\0', File.ReadAllText(
                Path.Combine(result.ArtifactDirectory!, "nonlinear_node_disp.out")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ContinuousBeam_KinematicMidspanDisplacement_CracksButConverges()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        string root = Path.Combine(Path.GetTempPath(), "opencs-fem-nonlinear-kinematic", Guid.NewGuid().ToString("N"));

        // Неразрезная балка на 3 опорах (0, 6, 12 м), вынужденное смещение -0.02 м (2 см)
        // в серединах обоих пролётов (узлы 2 и 4) — воспроизводит реальный сценарий
        // пользователя, где сечение реально трескается (не остаётся в линейном участке,
        // в отличие от Cantilever_SmallElasticTipLoad_MatchesBeamTheory выше).
        // Диаграмма бетона (сечение 1) — реальная СП63-подобная кривая с плоским хвостом
        // и в растяжении (после трещинообразования, ε≈0.0001), и в сжатии (после
        // раздавливания, ε≈-0.002…-0.0035); MaterialDiagramMapper придаёт этим хвостам
        // малый ненулевой наклон, иначе ForceBeamColumn не может обратить матрицу
        // гибкости сечения, как только волокна выходят на плато.
        var concrete = new OpenSeesMaterialDefinition
        {
            Tag = 1,
            NegativeEnvelope =
            [
                new EnvelopePoint(-0.0035, -14_511_250), new EnvelopePoint(-0.003125, -14_500_000),
                new EnvelopePoint(-0.00275, -14_500_000), new EnvelopePoint(-0.002375, -14_500_000),
                new EnvelopePoint(-0.002, -14_500_000), new EnvelopePoint(-0.0015725, -13_050_000),
                new EnvelopePoint(-0.001145, -11_600_000), new EnvelopePoint(-0.0007175, -10_150_000),
                new EnvelopePoint(-0.00029, -8_700_000), new EnvelopePoint(-0.0002175, -6_525_000),
                new EnvelopePoint(-0.000145, -4_350_000), new EnvelopePoint(-0.0000725, -2_175_000),
                new EnvelopePoint(0, 0)
            ],
            PositiveEnvelope =
            [
                new EnvelopePoint(0, 0), new EnvelopePoint(0.0000525, 157_500),
                new EnvelopePoint(0.000105, 315_000), new EnvelopePoint(0.0001575, 472_500),
                new EnvelopePoint(0.00021, 630_000), new EnvelopePoint(0.0004075, 735_000),
                new EnvelopePoint(0.000605, 840_000), new EnvelopePoint(0.0008025, 945_000),
                new EnvelopePoint(0.001, 1_050_000), new EnvelopePoint(0.001125, 1_050_000),
                new EnvelopePoint(0.00125, 1_050_000), new EnvelopePoint(0.001375, 1_050_000),
                new EnvelopePoint(0.0015, 1_050_375)
            ]
        };
        var section = new OpenCS.OpenSees.Model.OpenSeesSectionModel
        {
            Materials = [concrete],
            Fibers =
            [
                new OpenSeesFiber(-0.5, -0.5, 0.25, 1), new OpenSeesFiber(-0.5, 0.5, 0.25, 1),
                new OpenSeesFiber(0.5, -0.5, 0.25, 1), new OpenSeesFiber(0.5, 0.5, 0.25, 1)
            ],
            GJ = 1e6
        };

        var model = new FemNonlinearModel
        {
            Nodes =
            [
                new FemLinearNode(1, 0, 0, 0, [true, true, true, true, false, true]),
                new FemLinearNode(2, 3, 0, 0, [false, true, false, true, false, true]),
                new FemLinearNode(3, 6, 0, 0, [false, true, true, true, false, true]),
                new FemLinearNode(4, 9, 0, 0, [false, true, false, true, false, true]),
                new FemLinearNode(5, 12, 0, 0, [false, true, true, true, false, true]),
            ],
            Sections = new Dictionary<int, OpenCS.OpenSees.Model.OpenSeesSectionModel> { [1] = section },
            Elements =
            [
                new FemNonlinearElement(1, 1, 2, SectionTag: 1, NumIntegrationPoints: 5, Vecxz: (0, 0, 1)),
                new FemNonlinearElement(2, 2, 3, SectionTag: 1, NumIntegrationPoints: 5, Vecxz: (0, 0, 1)),
                new FemNonlinearElement(3, 3, 4, SectionTag: 1, NumIntegrationPoints: 5, Vecxz: (0, 0, 1)),
                new FemNonlinearElement(4, 4, 5, SectionTag: 1, NumIntegrationPoints: 5, Vecxz: (0, 0, 1)),
            ],
            Stages = [new FemNonlinearStage
            {
                Tag = "Стадия 1",
                KinematicLoads =
                [
                    new FemLinearKinematicLoad(2, 3, -0.02),
                    new FemLinearKinematicLoad(4, 3, -0.02),
                ]
            }],
            LoadFactorStep = 0.1, MaxLoadFactor = 1.0, RefinementDivisions = 10, MaxRefinementDepth = 4,
            Tolerance = 1e-6, MaxIterations = 50, GeomTransfKind = "Linear"
        };

        try
        {
            var result = await new FemNonlinearAnalysisService(
                new FemNonlinearTclGenerator(),
                new OpenSeesProcessRunner(),
                new OpenSeesArtifactStore(root),
                new FemNonlinearResultParser())
                .RunAsync(model, new OpenSeesRunRequest
                {
                    ExecutablePath = executable,
                    WorkingDirectory = Path.GetTempPath(),
                    Timeout = TimeSpan.FromSeconds(60)
                }, CancellationToken.None);

            Assert.True(result.Status == "ok", $"status={result.Status}; diagnostics={string.Join(" | ", result.Diagnostics)}");
            var last = result.Steps[^1];
            Assert.InRange(last.LoadFactor, 0.99, 1.01);
            Assert.True(last.Converged);

            double uz2 = last.Displacements.Single(d => d.NodeTag == 2).Uz;
            Assert.InRange(uz2, -0.0201, -0.0199);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Cantilever_TinyTipLoad_NativeConcrete04_Converges()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        string root = Path.Combine(Path.GetTempPath(), "opencs-fem-nonlinear-native-diag", Guid.NewGuid().ToString("N"));

        Material concrete = new() { Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000 };
        foreach (CalcType calc in Enum.GetValues<CalcType>())
        {
            MaterialChars chars = new()
            {
                Type = MatType.Concrete, TypeCalc = calc, E = 30_000_000,
                Fc = -14_500, Ft = 1_050, Ec0 = -0.002, Ec2 = -0.0035, Ec1Red = -0.0015,
                Et0 = 0.0001, Et1Red = 0.00008, Et2 = 0.00015
            };
            switch (calc)
            {
                case CalcType.C: concrete.C = chars; break;
                case CalcType.CL: concrete.CL = chars; break;
                case CalcType.N: concrete.N = chars; break;
                case CalcType.NL: concrete.NL = chars; break;
            }
        }

        MaterialArea concreteArea = new()
        {
            Id = 1, Tag = "concrete", Material = concrete, MaterialId = concrete.Id,
            DiagrammType = DiagrammType.L2,
            Fibers =
            [
                new Fiber { X = -0.5, Y = -0.5, Area = 0.25, TypeFiber = FiberType.tri },
                new Fiber { X = -0.5, Y = 0.5, Area = 0.25, TypeFiber = FiberType.tri },
                new Fiber { X = 0.5, Y = -0.5, Area = 0.25, TypeFiber = FiberType.tri },
                new Fiber { X = 0.5, Y = 0.5, Area = 0.25, TypeFiber = FiberType.tri }
            ]
        };
        CrossSection crossSection = new() { Id = 1, Areas = [concreteArea] };

        var adapterOptions = new CrossSectionToOpenSeesAdapter.Options
        {
            GJ = 1e6, MaterialSource = MaterialSource.Native, SteelModel = SteelModelKind.Steel02
        };
        var sectionModel = CrossSectionToOpenSeesAdapter.Build(
            crossSection, CalcType.C, new Dictionary<int, Material> { [concrete.Id] = concrete },
            customPool: null, adapterOptions);

        // Тот же геометрия/нагрузка, что в Cantilever_SmallElasticTipLoad_MatchesBeamTheory, но
        // с реальным бетоном (E=30 ГПа вместо E=2e8 Па заглушки) — деформация волокна будет
        // на порядки меньше epsc0=0.002, глубоко в линейном участке.
        var model = new FemNonlinearModel
        {
            Nodes =
            [
                new FemLinearNode(1, 0, 0, 0, [true, true, true, true, true, true]),
                new FemLinearNode(2, 2.0, 0, 0, new bool[6]),
            ],
            Sections = new Dictionary<int, OpenSeesSectionModel> { [1] = sectionModel },
            Elements = [new FemNonlinearElement(1, 1, 2, SectionTag: 1, NumIntegrationPoints: 5, Vecxz: (0, 0, 1))],
            Stages = [new FemNonlinearStage { Tag = "Стадия 1", Loads = [new FemLinearNodalLoad(2, 0, 0, -1000, 0, 0, 0)] }],
            LoadFactorStep = 0.25, MaxLoadFactor = 1.0, RefinementDivisions = 10,
            Tolerance = 1e-8, MaxIterations = 30, GeomTransfKind = "Linear"
        };

        try
        {
            var result = await new FemNonlinearAnalysisService(
                new FemNonlinearTclGenerator(),
                new OpenSeesProcessRunner(),
                new OpenSeesArtifactStore(root),
                new FemNonlinearResultParser())
                .RunAsync(model, new OpenSeesRunRequest
                {
                    ExecutablePath = executable,
                    WorkingDirectory = Path.GetTempPath(),
                    Timeout = TimeSpan.FromSeconds(30)
                }, CancellationToken.None);

            Assert.True(result.Status == "ok", $"status={result.Status}; diagnostics={string.Join(" | ", result.Diagnostics)}");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LShapedFrame_CompressColumnThenBendBeam_PreservesStage1State()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        string root = Path.Combine(Path.GetTempPath(), "opencs-fem-nonlinear-staged", Guid.NewGuid().ToString("N"));

        // Г-образная рама: стойка узел 1 (заделка) — узел 2 (0,0,3), ригель узел 2 — узел 3 (3,0,3).
        // Сечение — упругая заглушка (как в Cantilever_SmallElasticTipLoad_MatchesBeamTheory),
        // чтобы результат был предсказуем аналитически и тест не зависел от нелинейности материала.
        var baseSection = CrossSectionFixtures.SymmetricElasticSection();
        var section = new OpenSeesSectionModel
        {
            Materials = baseSection.Materials,
            Fibers = baseSection.Fibers,
            GJ = 1e6
        };

        const double N = -50_000.0;    // стадия 1: сжатие стойки, Н (вдоль -Z, вниз на узел 2)
        const double P = -10_000.0;    // стадия 2: нагрузка на ригель, Н (вдоль -Z, вниз на узел 3)

        var model = new FemNonlinearModel
        {
            Nodes =
            [
                new FemLinearNode(1, 0, 0, 0, [true, true, true, true, true, true]),
                new FemLinearNode(2, 0, 0, 3, new bool[6]),
                new FemLinearNode(3, 3, 0, 3, new bool[6]),
            ],
            Sections = new Dictionary<int, OpenSeesSectionModel> { [1] = section },
            Elements =
            [
                new FemNonlinearElement(1, 1, 2, SectionTag: 1, NumIntegrationPoints: 5, Vecxz: (0, 1, 0)),
                new FemNonlinearElement(2, 2, 3, SectionTag: 1, NumIntegrationPoints: 5, Vecxz: (0, 0, 1)),
            ],
            Stages =
            [
                new FemNonlinearStage { Tag = "Сжатие стойки", Loads = [new FemLinearNodalLoad(2, 0, 0, N, 0, 0, 0)] },
                new FemNonlinearStage { Tag = "Нагрузка на ригель", Loads = [new FemLinearNodalLoad(3, 0, 0, P, 0, 0, 0)] },
            ],
            LoadFactorStep = 0.5, MaxLoadFactor = 1.0, RefinementDivisions = 10,
            Tolerance = 1e-8, MaxIterations = 30, GeomTransfKind = "Linear"
        };

        try
        {
            var result = await new FemNonlinearAnalysisService(
                new FemNonlinearTclGenerator(),
                new OpenSeesProcessRunner(),
                new OpenSeesArtifactStore(root),
                new FemNonlinearResultParser())
                .RunAsync(model, new OpenSeesRunRequest
                {
                    ExecutablePath = executable,
                    WorkingDirectory = Path.GetTempPath(),
                    Timeout = TimeSpan.FromSeconds(30)
                }, CancellationToken.None);

            Assert.True(result.Status == "ok", $"status={result.Status}; diagnostics={string.Join(" | ", result.Diagnostics)}");
            Assert.Equal(2, result.StageTags.Count);
            Assert.Equal("Сжатие стойки", result.StageTags[0]);
            Assert.Equal("Нагрузка на ригель", result.StageTags[1]);

            // Все шаги стадии 1 — StageIndex=0, все шаги стадии 2 — StageIndex=1, границы не смешаны.
            var stage1Steps = result.Steps.Where(s => s.StageIndex == 0).ToList();
            var stage2Steps = result.Steps.Where(s => s.StageIndex == 1).ToList();
            Assert.NotEmpty(stage1Steps);
            Assert.NotEmpty(stage2Steps);
            Assert.All(stage1Steps, s => Assert.True(s.StepIndex < stage2Steps.Min(s2 => s2.StepIndex)));

            // Последний шаг стадии 1: осевая сила в стойке (элемент 1) по модулю ≈ |N| (заделка
            // воспринимает сжатие целиком). Знак Ni в локальной системе forceBeamColumn не
            // совпадает со знаком глобальной нагрузки N — сравниваем по модулю.
            var lastStage1 = stage1Steps.Last(s => s.Converged);
            double axialAfterStage1 = lastStage1.ElementForces.Single(f => f.ElemTag == 1).Ni;
            Assert.InRange(System.Math.Abs(axialAfterStage1), System.Math.Abs(N) * 0.95, System.Math.Abs(N) * 1.05);

            // Последний шаг стадии 2: осевая сила в стойке НЕ ослабевает (нагрузка ригеля добавляет
            // изгиб/поперечную силу в стойку через жёсткий узел, но продольное сжатие стадии 1
            // остаётся приложенным — |N| после стадии 2 не меньше, чем после стадии 1).
            var lastStage2 = stage2Steps.Last(s => s.Converged);
            double axialAfterStage2 = lastStage2.ElementForces.Single(f => f.ElemTag == 1).Ni;
            Assert.True(System.Math.Abs(axialAfterStage2) >= System.Math.Abs(axialAfterStage1) * 0.99,
                $"Осевая сила стойки не должна ослабевать после стадии 2: было {axialAfterStage1}, стало {axialAfterStage2}");

            // Ригель нагружен только на стадии 2 — момент в ригеле (элемент 2) отсутствует после
            // стадии 1 и появляется после стадии 2.
            double beamMomentAfterStage1 = lastStage1.ElementForces.Single(f => f.ElemTag == 2).Myi;
            double beamMomentAfterStage2 = lastStage2.ElementForces.Single(f => f.ElemTag == 2).Myi;
            Assert.InRange(beamMomentAfterStage1, -1, 1);
            Assert.True(System.Math.Abs(beamMomentAfterStage2) > 1000,
                $"Момент ригеля после стадии 2 должен быть заметно ненулевым, получено {beamMomentAfterStage2}");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
