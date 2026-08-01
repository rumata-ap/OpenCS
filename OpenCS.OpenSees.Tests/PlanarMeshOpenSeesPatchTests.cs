using CScore;
using CScore.Planar;
using CScore.PlateRebar;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using OpenCS.OpenSees.Tests.Fixtures;
using ShellResult = OpenCS.OpenSees.Structural.ShellResult;

namespace OpenCS.OpenSees.Tests;

/// <summary>Реальный сквозной patch test: gmsh.exe строит сетку прямоугольника, адаптер (Task 4)
/// переводит её в ShellOpenSeesModel, реальный OpenSees.exe считает — проверяется, что осевое
/// поле деформаций/усилий однородно независимо от T3/Q4/mixed и нерегулярности сетки.</summary>
public sealed class PlanarMeshOpenSeesPatchTests
{
    [Theory]
    [InlineData(PlanarMeshElementMode.Triangles)]
    [InlineData(PlanarMeshElementMode.Quads)]
    [InlineData(PlanarMeshElementMode.Mixed)]
    public async Task AxialTension_ReproducesUniformField(PlanarMeshElementMode mode)
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        string gmshRoot = Path.Combine(Path.GetTempPath(), "opencs-gmsh-patch-tests", Guid.NewGuid().ToString("N"));

        try
        {
            PlanarMeshSnapshot snapshot = await GmshOpenSeesPatchTestFixture.BuildSnapshotAsync(mode, gmshRoot);

            var section = new PlateSection { H = GmshOpenSeesPatchTestFixture.Thickness, NLayers = 4 };
            var field = new PlateRebarField([], []);
            PlanarMeshShellModelResult built = PlanarMeshSnapshotShellModelAdapter.Build(
                snapshot, Frame3D.Identity, section, field, new ConcreteOnlyResolver());

            ShellOpenSeesModel model = GmshOpenSeesPatchTestFixture.BuildLoadedModel(built, snapshot);
            model.Validate();

            using var fixture = new ShellArtifactFixture();
            string scriptPath = Path.Combine(fixture.Directory, "script.tcl");
            File.WriteAllText(scriptPath, new ShellTclGenerator().Generate(model));

            OpenSeesRunResult run = await new OpenSeesProcessRunner().RunAsync(
                new OpenSeesRunRequest
                {
                    ExecutablePath = executable,
                    WorkingDirectory = fixture.Directory,
                    ScriptPath = scriptPath,
                    Timeout = TimeSpan.FromSeconds(60)
                }, CancellationToken.None);

            Assert.Equal(0, run.ExitCode);
            ShellResult result = new ShellResultParser().Parse(
                fixture.Directory, model.Elements.ToDictionary(e => e.Tag));
            Assert.Equal("completed", result.Status);

            GmshOpenSeesPatchTestFixture.AssertUniformAxialField(
                result, model, GmshOpenSeesPatchTestFixture.LoadEdgeCornerTags(built, snapshot));
        }
        finally
        {
            if (Directory.Exists(gmshRoot)) Directory.Delete(gmshRoot, recursive: true);
        }
    }

    private sealed class ConcreteOnlyResolver : IPlateSectionShellMaterialResolver
    {
        public IReadOnlyList<NativeShellMaterialDefinition> ResolveConcrete(int sourceMaterialId) =>
            [new(1, $"concrete:{sourceMaterialId}",
                new ElasticIsotropicShellMaterialSpec(GmshOpenSeesPatchTestFixture.E, GmshOpenSeesPatchTestFixture.Nu))];

        public IReadOnlyList<NativeShellMaterialDefinition> ResolveRebar(int sourceMaterialId) =>
            throw new NotSupportedException("Patch test не использует армирование.");
    }
}
