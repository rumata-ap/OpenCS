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

    [Fact]
    public async Task ControlCheck_HomogeneousPatch_ReportsConsistentWithinTolerance()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        string gmshRoot = Path.Combine(Path.GetTempPath(), "opencs-shell-mesh-patch-opensees-controlcheck", Guid.NewGuid().ToString("N"));
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

            var strip = new PlateStripBeamAnalogy { ExplicitWidthM = 4.0, Geometry = new PlateStripGeometry { LengthM = 4.0 } };
            var pointwise = PlateSectionTangentSnapshot.Create(section, concreteDiagram, concreteDiagram);
            Assert.True(pointwise.IsCalculable);

            var buildEquiv = EquivalentSectionCalculator.Build(
                strip, pointwise.Source, widthSources: [pointwise.Source!, pointwise.Source!],
                ReductionPolicy.ConstitutiveIntegration, widthIntegrationPoints: 2);
            Assert.True(buildEquiv.IsCalculable, string.Join("; ", buildEquiv.Diagnostics.Select(d => d.Message)));

            var (widthV, _) = EquivalentSectionCalculator.WidthGaussPoints(4.0, 2);
            const double regionCenterV = 2.0;
            var rve1 = await ShellMeshPatchPlateSectionResponse.CreateAsync(
                region, stripFrame: region.Frame, centerU: 2.0, centerV: regionCenterV + widthV[0], field, section,
                concreteDiagram, concreteDiagram, resolver,
                new ShellMeshPatchStateBounds(1e-4, 0.01), rveSizeM: 1.0,
                new PlanarMeshSettings(0.3, 6, PlanarMeshElementMode.Triangles), mesher, runner, executable, CancellationToken.None);
            var rve2 = await ShellMeshPatchPlateSectionResponse.CreateAsync(
                region, stripFrame: region.Frame, centerU: 2.0, centerV: regionCenterV + widthV[1], field, section,
                concreteDiagram, concreteDiagram, resolver,
                new ShellMeshPatchStateBounds(1e-4, 0.01), rveSizeM: 1.0,
                new PlanarMeshSettings(0.3, 6, PlanarMeshElementMode.Triangles), mesher, runner, executable, CancellationToken.None);
            Assert.True(rve1.IsCalculable && rve2.IsCalculable,
                string.Join("; ", rve1.Diagnostics.Concat(rve2.Diagnostics).Select(d => d.Message)));

            var beamState = new BeamStrainState(Eps0: 5e-5, KappaY: 0, KappaZ: 0);
            var check = EquivalentSectionControlCheck.Run(
                buildEquiv.Section, [rve1.Source!, rve2.Source!], beamState,
                relativeTolerance: 0.08, absoluteTolerance: 1e-6);

            Assert.True(check.IsCalculable, string.Join("; ", check.Diagnostics.Select(d => d.Message)));
            Assert.True(check.IsConsistent, $"residual=[{string.Join(",", check.Residual)}]");
        }
        finally
        {
            if (Directory.Exists(gmshRoot)) Directory.Delete(gmshRoot, recursive: true);
        }
    }

    // PlateSection.ComputeTangent трактует E (LinearElasticDiagram, ниже) как уже "кН/м²"-
    // согласованное значение: Nx=E·eps·h БЕЗ множителя ×1000 (см. комментарий в
    // CSfea.Tests/ShellMeshPatchCSfeaTests.cs.LinearElasticDiagram). Нативный OpenSees-материал
    // работает в подлинном СИ (Па), а RVE.Forces() делит результат на 1000 (Н/м → кН/м) —
    // поэтому, чтобы предсказание EquivalentSection (построенное из PlateSectionTangentSnapshot
    // с ConcreteEValue) и прямой RVE-отклик описывали ФИЗИЧЕСКИ ОДИН И ТОТ ЖЕ материал, нужно
    // ConcreteEPa = ConcreteEValue·1000, а не ConcreteEValue напрямую (не "физически реальный"
    // бетон ~30e9 Па — это дало бы несогласованный по величине с диаграммой материал и ложный
    // провал control check с невязкой ~1000).
    const double ConcreteEValue = 30_000.0;
    const double ConcreteEPa = ConcreteEValue * 1000.0;

    sealed class ConcreteOnlyResolver : IPlateSectionShellMaterialResolver
    {
        public IReadOnlyList<NativeShellMaterialDefinition> ResolveConcrete(int sourceMaterialId) =>
            [new(1, $"concrete:{sourceMaterialId}", new ElasticIsotropicShellMaterialSpec(ConcreteEPa, 0.0))];

        public IReadOnlyList<NativeShellMaterialDefinition> ResolveRebar(int sourceMaterialId) =>
            throw new NotSupportedException("Тест не использует армирование.");
    }

    internal static Diagramm LinearElasticDiagram()
    {
        const double e = ConcreteEValue;
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
