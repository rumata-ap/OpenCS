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
