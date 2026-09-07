using CScore;
using CScore.Planar;
using CScore.PlateRebar;
using OpenCS.Gmsh;
using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using OpenCS.OpenSees.Tests.Fixtures;
using Xunit;

namespace OpenCS.OpenSees.Tests;

/// <summary>
/// Срез 7, Task 9 (часть 2): подтверждение знаковой конвенции моментов OpenSees на реальном
/// прогоне — до того, как на неё обопрётся сверка shell ↔ beam.
///
/// Без этой проверки систематическое расхождение знака в E2E было бы неотличимо от «физического»
/// расхождения из-за постоянной D на элементе, и его списали бы на упрощение модели.
///
/// Схема: прямоугольная полоса 6 × 2 м, шарнирно опёртая по коротким краям, равномерная
/// нагрузка вниз. Волокна снизу растянуты, ось z направлена вверх, поэтому по внутренней
/// конвенции OpenCS (Mx = ∫σx·z dz) момент в середине пролёта обязан быть ОТРИЦАТЕЛЬНЫМ.
/// </summary>
public class ShellMomentSignConventionTests
{
    const double LengthM = 6.0;
    const double WidthM = 2.0;
    const double ThicknessM = 0.3;
    const double ConcreteEValue = 30_000.0;
    const double ConcreteEPa = ConcreteEValue * 1000.0;

    [Fact]
    public async Task SimplySupportedStrip_UnderDownwardLoad_HasNegativeMxAtMidspan()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        string root = Path.Combine(Path.GetTempPath(), "opencs-moment-sign", Guid.NewGuid().ToString("N"));
        try
        {
            var region = PlanarRegion.CreateFromContour(
                new Contour { X = [0, LengthM, LengthM, 0], Y = [0, 0, WidthM, WidthM] },
                frame: Frame3D.Identity);
            var section = new PlateSection
            {
                H = ThicknessM, NLayers = 4, TensionConcrete = true, ConcreteMaterialId = 1
            };

            var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions
            {
                ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe",
                ArtifactRoot = root
            });
            var snapshot = await mesher.BuildAsync(
                new PlanarMeshingRequest(region, new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Quads), []),
                CancellationToken.None);
            Assert.True(snapshot.IsCalculable,
                string.Join("; ", snapshot.Diagnostics.Select(d => d.Message)));

            var built = PlanarMeshSnapshotShellModelAdapter.Build(
                snapshot, region.Frame, section, new PlateRebarField([], []), new ConcreteOnlyResolver());
            Assert.NotNull(built.Model);

            // Шарнирное опирание по коротким краям: закрепляем UZ на обоих, плюс минимальное
            // закрепление в плоскости, чтобы система не была кинематически изменяемой.
            var nodes = built.Model.Nodes.Select(node =>
            {
                bool atStart = Math.Abs(node.X) < 1e-9;
                bool atEnd = Math.Abs(node.X - LengthM) < 1e-9;
                if (!atStart && !atEnd) return node;

                var fixity = new bool[6];
                fixity[2] = true;                       // UZ на обоих краях
                fixity[3] = true;                       // RX: подавляем кручение полосы
                if (atStart) { fixity[0] = true; fixity[1] = true; }
                else fixity[1] = true;                  // UY на дальнем краю, UX свободен
                return node with { Fixed = fixity };
            }).ToList();

            // Равномерная нагрузка вниз, разнесённая по узлам поровну.
            const double totalKn = -60.0;               // кН, вниз
            double perNode = totalKn * 1000.0 / nodes.Count;   // Н на узел
            var loads = nodes
                .Select(n => new ShellNodalLoad(n.Tag, 0, 0, perNode, 0, 0, 0))
                .ToList();

            var model = built.Model with
            {
                Nodes = nodes,
                Stages =
                [
                    new ShellNonlinearStage
                    {
                        Tag = "stage-1", Loads = loads, LoadFactorStep = 1.0, MaxLoadFactor = 1.0
                    }
                ],
                Policy = built.Model.Policy with { Algorithm = "Newton" }
            };

            var runner = new ShellAnalysisRunner(
                new ShellTclGenerator(), new OpenSeesArtifactStore(root),
                new OpenSeesProcessRunner(), new ShellResultParser(), TimeSpan.FromSeconds(180));
            var run = await runner.RunAsync(model, executable, CancellationToken.None);

            Assert.True(run.Outcome == ShellAnalysisOutcome.Completed,
                $"OpenSees не завершил прогон: {run.Outcome}. {run.ErrorMessage}");
            var step = run.Result!.Steps.Last(s => s.Converged);
            Assert.Equal(1.0, step.LoadFactor, 6);

            // Элементы в середине пролёта: их resultants и несут искомый знак.
            var midspan = MidspanResultants(step, snapshot);
            Assert.NotEmpty(midspan);
            double meanMx = midspan.Average(r => r.Mx);

            Assert.True(meanMx < 0.0,
                $"Ожидался отрицательный Mx в середине пролёта (растяжение снизу при z вверх), " +
                $"получено {meanMx:G6}. Если знак устойчиво положителен — конвенция OpenSees " +
                $"противоположна внутренней конвенции OpenCS, и её нужно переворачивать в " +
                $"ShellResultantRotation, а не списывать расхождение E2E на постоянную D.");

            // Порядок величины: q = 60 кН / 6 м = 10 кН/м вдоль пролёта, M = qL²/8 = 45 кН·м
            // на всю ширину, то есть ≈22,5 кН·м/м = 22 500 Н·м/м. Узловая нагрузка разнесена
            // поровну (краевые узлы по-хорошему должны получать меньше), поэтому допуск широкий
            // — задача проверки в том, чтобы момент был физическим, а не «отрицательным нулём».
            Assert.InRange(Math.Abs(meanMx), 5_000.0, 60_000.0);

            // Волокна снизу растянуты: прогиб в середине направлен вниз.
            double minUz = step.Displacements.Min(d => d.Uz);
            Assert.True(minUz < 0.0, $"Прогиб обязан быть направлен вниз, получено {minUz:G6}.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    static List<ShellSectionResultants> MidspanResultants(
        RCShellStepResult step, PlanarMeshSnapshot snapshot)
    {
        var midspanElements = new HashSet<int>();
        foreach (var element in snapshot.Elements)
        {
            var centroid = element.Centroid(snapshot.Nodes);
            if (Math.Abs(centroid.U - LengthM / 2.0) < 0.6)
                midspanElements.Add(element.Index + 1);   // теги адаптера: index + 1
        }
        return step.SectionResultants.Where(r => midspanElements.Contains(r.ElementTag)).ToList();
    }

    sealed class ConcreteOnlyResolver : IPlateSectionShellMaterialResolver
    {
        public IReadOnlyList<NativeShellMaterialDefinition> ResolveConcrete(int sourceMaterialId) =>
            [new(1, $"concrete:{sourceMaterialId}", new ElasticIsotropicShellMaterialSpec(ConcreteEPa, 0.0))];

        public IReadOnlyList<NativeShellMaterialDefinition> ResolveRebar(int sourceMaterialId) =>
            throw new NotSupportedException("Тест не использует армирование.");
    }
}
