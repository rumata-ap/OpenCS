using CScore;
using CScore.Planar;
using CScore.PlateRebar;
using CScore.PlateStrip;
using CSfea.CScoreBridge;
using OpenCS.Gmsh;

namespace CSfea.Tests;

public static class ShellMeshPatchCSfeaTests
{
    public static void RunAll()
    {
        TestHarness.Section("ShellMeshPatchPlateSectionResponse (CSfea): реальный Gmsh + линейный решатель");
        HomogeneousPatch_AxialState_MatchesPointwiseSnapshot().GetAwaiter().GetResult();
    }

    static async Task HomogeneousPatch_AxialState_MatchesPointwiseSnapshot()
    {
        string gmshRoot = Path.Combine(Path.GetTempPath(), "opencs-shell-mesh-patch-csfea", Guid.NewGuid().ToString("N"));
        try
        {
            var region = PlanarRegion.CreateFromContour(
                new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] }, frame: Frame3D.Identity);

            var section = new PlateSection { H = 0.2, NLayers = 20, TensionConcrete = true };
            var concrete = LinearElasticDiagram();
            var materials = new PlateSectionMaterials { ConcreteDiagram = concrete, RebarDiagram = concrete, ConcreteE_MPa = 30000.0 };
            var field = new PlateRebarField([], []);

            var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions
            {
                ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe",
                ArtifactRoot = gmshRoot
            });

            var buildResult = await ShellMeshPatchPlateSectionResponse.CreateAsync(
                region, stripFrame: region.Frame, centerU: 2.0, centerV: 2.0, field, section, materials,
                bounds: new ShellMeshPatchStateBounds(1e-4, 0.01),
                rveSizeM: 1.0,
                meshSettings: new PlanarMeshSettings(0.25, 6, PlanarMeshElementMode.Mixed),
                mesher: mesher);

            TestHarness.Check("Адаптер построен (нет блокирующих диагностик)",
                buildResult.IsCalculable, string.Join("; ", buildResult.Diagnostics.Select(d => d.Message)));
            if (!buildResult.IsCalculable) return;

            var snapshotResult = PlateSectionTangentSnapshot.Create(section, concrete, concrete);
            TestHarness.Check("Поточечный snapshot построен", snapshotResult.IsCalculable, "");
            if (!snapshotResult.IsCalculable) return;

            var state = new ShellStrainState(5e-5, 0, 0, 0, 0, 0);
            var rveForces = buildResult.Source!.Forces(state);
            var pointwiseForces = snapshotResult.Source!.Forces(state);

            double scale = Math.Max(Math.Abs(rveForces.Nx), Math.Abs(pointwiseForces.Nx));
            TestHarness.Check("Nx RVE ≈ Nx поточечного snapshot (однородный патч, чисто осевое состояние)",
                Math.Abs(rveForces.Nx - pointwiseForces.Nx) < 0.05 * scale,
                $"rve.Nx={rveForces.Nx:e6}, pointwise.Nx={pointwiseForces.Nx:e6}");
        }
        finally
        {
            if (Directory.Exists(gmshRoot)) Directory.Delete(gmshRoot, recursive: true);
        }
    }

    /// <summary>E используется в том же внутреннем смысле, что PlateSection.Compute()/
    /// ComputeTangent() (Nx=E·eps·h без множителя ×1000) — по образцу
    /// CSfea.Tests/EquivalentBeamAnalogyE2ETests.cs. НЕ физически реалистичное значение E бетона
    /// (для этого использовалось бы 30e9/1000 в кПа) — намеренно: числа такого порядка (~3e7)
    /// делают шум ComputeTangent'а собственной FD-схемы (fdStep=1e-7 по умолчанию) сравнимым по
    /// величине с теоретически нулевыми элементами B-блока (mасштаб шума растёт линейно с E),
    /// из-за чего ShellMeshPatchPreflight ложно отклоняет заведомо линейный материал.</summary>
    internal static Diagramm LinearElasticDiagram()
    {
        const double e = 30_000.0;
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            E = e, Ry = e / 50, Ru = e / 50, Ft = e / 50, Fc = -e / 50,
            Ec2 = -0.05, Et2 = 0.05, Type = MatType.ReSteelF,
        };
        var m = new Material { Id = 1, E = e, Type = MatType.ReSteelF, Tag = "shell-mesh-patch-linear" };
        m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
        return m.GetDiagramms(DiagrammType.L2)![CalcType.C];
    }
}
