using CScore;
using CScore.Planar;
using CScore.PlateRebar;
using CScore.PlateStrip;
using OpenCS.Gmsh;
using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Tcl;
using OpenCS.OpenSees.Tests.Fixtures;
using Xunit;

namespace OpenCS.OpenSees.Tests;

public sealed class ShellMeshPatchOpenSeesTests
{
    [Fact]
    public async Task HomogeneousPatch_AxialState_ProducesFiniteResultants()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        string gmshRoot = Path.Combine(Path.GetTempPath(), "opencs-shell-mesh-patch-opensees", Guid.NewGuid().ToString("N"));
        try
        {
            var region = PlanarRegion.CreateFromContour(
                new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] }, frame: Frame3D.Identity);
            var section = new PlateSection { H = 0.2, NLayers = 4, TensionConcrete = true };
            var field = new PlateRebarField([], []);
            var resolver = new ConcreteOnlyResolver();
            var concreteDiagram = LinearElasticDiagram();

            var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions
            {
                ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe",
                ArtifactRoot = gmshRoot
            });
            var runner = new ShellAnalysisRunner(
                new ShellTclGenerator(), new OpenSeesArtifactStore(gmshRoot), new OpenSeesProcessRunner(), new ShellResultParser());

            var buildResult = await ShellMeshPatchPlateSectionResponse.CreateAsync(
                region, stripFrame: region.Frame, centerU: 2.0, centerV: 2.0, field, section,
                concreteDiagram, concreteDiagram, resolver,
                bounds: new ShellMeshPatchStateBounds(1e-4, 0.01), rveSizeM: 1.0,
                meshSettings: new PlanarMeshSettings(0.3, 6, PlanarMeshElementMode.Triangles),
                mesher, runner, executable, CancellationToken.None);

            Assert.True(buildResult.IsCalculable, string.Join("; ", buildResult.Diagnostics.Select(d => d.Message)));

            var state = new ShellStrainState(5e-5, 0, 0, 0, 0, 0);
            var forces = buildResult.Source!.Forces(state);

            Assert.True(double.IsFinite(forces.Nx));
            Assert.True(forces.Nx > 0, "Растяжение должно давать положительный Nx.");
        }
        finally
        {
            if (Directory.Exists(gmshRoot)) Directory.Delete(gmshRoot, recursive: true);
        }
    }

    sealed class ConcreteOnlyResolver : IPlateSectionShellMaterialResolver
    {
        public IReadOnlyList<NativeShellMaterialDefinition> ResolveConcrete(int sourceMaterialId) =>
            [new(1, $"concrete:{sourceMaterialId}", new ElasticIsotropicShellMaterialSpec(30e9, 0.0))];

        public IReadOnlyList<NativeShellMaterialDefinition> ResolveRebar(int sourceMaterialId) =>
            throw new NotSupportedException("Тест не использует армирование.");
    }

    internal static Diagramm LinearElasticDiagram()
    {
        // E — в собственной единичной конвенции PlateSection.ComputeTangent (см. комментарий в
        // CSfea.Tests/ShellMeshPatchCSfeaTests.cs.LinearElasticDiagram) — используется ТОЛЬКО
        // для preflight/As, не для реальной жёсткости OpenSees-модели (та берётся из
        // ConcreteOnlyResolver.ResolveConcrete, E=30e9 Па — настоящая СИ-величина).
        const double e = 30_000.0;
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            E = e, Ry = e / 50, Ru = e / 50, Ft = e / 50, Fc = -e / 50,
            Ec2 = -0.05, Et2 = 0.05, Type = MatType.ReSteelF,
        };
        var m = new Material { Id = 1, E = e, Type = MatType.ReSteelF, Tag = "shell-mesh-patch-opensees-linear" };
        m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
        return m.GetDiagramms(DiagrammType.L2)![CalcType.C];
    }
}
