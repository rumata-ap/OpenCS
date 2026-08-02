using CScore.Planar;
using OpenCS.Gmsh;
using OpenCS.Gmsh.Generation;
using Xunit;

namespace OpenCS.Gmsh.Tests;

public sealed class GmshCutInterfaceMappingTests
{
    [Fact]
    public void CutInterfaceConstraintIsCurveAndNotHole()
    {
        var region = Region();
        var cut = Cut();

        var geo = GmshPlanarGeoBuilder.Build(
            region,
            new PlanarMeshSettings(0.25, 6, PlanarMeshElementMode.Mixed),
            [cut.CreateMeshConstraint()]);

        Assert.Contains("Physical Curve(\"constraint:cut:top:curve\"", geo);
        Assert.Contains("In Surface {1};", geo);
        Assert.Contains("Plane Surface(1) = {1};", geo);
        Assert.DoesNotContain("Plane Surface(2)", geo);
    }

    [Fact]
    public async Task RealGmshMapsCutCurveToContinuousChain()
    {
        string root = Path.Combine(Path.GetTempPath(), "opencs-gmsh-cut-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions
            {
                ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe",
                ArtifactRoot = root
            });
            var cut = Cut();
            var snapshot = await mesher.BuildAsync(new PlanarMeshingRequest(
                Region(),
                new PlanarMeshSettings(0.35, 6, PlanarMeshElementMode.Mixed),
                [cut.CreateMeshConstraint()]));

            Assert.True(snapshot.IsCalculable, string.Join(Environment.NewLine, snapshot.Diagnostics.Select(d => d.Message)));
            Assert.Contains(snapshot.ConstraintMappings, mapping => mapping.ConstraintObjectId == "cut:top");

            var mappingResult = PlanarCutInterfaceMeshMapper.Map(cut, snapshot);

            Assert.True(mappingResult.IsCalculable, string.Join(Environment.NewLine, mappingResult.Diagnostics.Select(d => d.Message)));
            Assert.True(mappingResult.Mapping!.OrderedNodes.Count >= 2);
            Assert.Equal(0.25, mappingResult.Mapping.OrderedNodes[0].Position.X, 8);
            Assert.Equal(1.75, mappingResult.Mapping.OrderedNodes[^1].Position.X, 8);
            Assert.True(File.Exists(Path.Combine(snapshot.Provenance!.ArtifactDirectory!, "manifest.json")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    static PlanarRegion Region() => PlanarRegion.CreateFromContour(new CScore.Contour
    {
        X = [0, 2, 2, 0],
        Y = [0, 0, 2, 2]
    });

    static PlanarCutInterface Cut() => new()
    {
        Id = "top",
        Geometry = new PlanarConstraintGeometry(
            PlanarConstraintGeometryKind.Curve,
            [new(0.25, 1), new(1.75, 1)]),
        NormalFromFragmentToOmittedSide = new(0, 1, 0),
        ModeByDof = PlanarBoundaryModeByDof.All(PlanarBoundaryDofMode.Free)
    };
}
