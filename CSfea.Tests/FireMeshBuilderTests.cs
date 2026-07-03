using CScore;
using CScore.Fire;
using CScore.Fire.Entities;
using CSfea.Thermal.Bc;

namespace CSfea.Tests;

/// <summary>Тесты построителя огневой сетки и маппинга граничных рёбер.</summary>
public static class FireMeshBuilderTests
{
    public static void RunAll()
    {
        TestHarness.Section("FireMeshBuilder: прямоугольник 0.2×0.4");
        BuildRectangleMesh_Ruppert();
        MapEdges_AllSidedFire();
    }

    private static void BuildRectangleMesh_Ruppert()
    {
        var section = CreateRectSection(0.2, 0.4);
        var res = FireMeshBuilder.Build(section, meshStepM: 0.05, algorithm: "ruppert", smoothIterTri: 3);

        TestHarness.Check("FireMeshBuilder_NNodes>10", res.Mesh.NNodes > 10, $"NNodes={res.Mesh.NNodes}");
        TestHarness.Check("FireMeshBuilder_NElements>10", res.Mesh.NElements > 10, $"NElements={res.Mesh.NElements}");
        TestHarness.Check("FireMeshBuilder_RebarsEmpty", res.Rebars.Count == 0, $"Rebars={res.Rebars.Count}");
        TestHarness.Check("FireMeshBuilder_BoundaryEdges>0", res.BoundaryEdges.Count > 0, $"Boundary={res.BoundaryEdges.Count}");
    }

    private static void MapEdges_AllSidedFire()
    {
        var section = CreateRectSection(0.2, 0.4);
        var res = FireMeshBuilder.Build(section, meshStepM: 0.05, algorithm: "ruppert", smoothIterTri: 2);
        var fire = new FireSectionDef
        {
            BcPreset = "all-sided",
            HoleBcPreset = "adiabatic",
            Edges = []
        };

        var mapped = FireBoundaryMapper.MapEdges(fire, res, "iso834");
        bool allFire = mapped.All(e => e.BcType == HeatBoundaryBcType.Fire && e.FireCurveAtTime != null);
        TestHarness.Check("FireBoundaryMapper_AllSided", mapped.Count > 0 && allFire, $"Mapped={mapped.Count}");
    }

    private static CrossSection CreateRectSection(double width, double height)
    {
        var hull = new Contour(
            new[] { 0.0, width, width, 0.0, 0.0 },
            new[] { 0.0, 0.0, height, height, 0.0 },
            "rect")
        { Type = ContourType.Hull };

        var area = new MaterialArea
        {
            Tag = "concrete",
            Category = AreaCategory.Region,
            Contours = [hull]
        };
        area.Hull = hull;

        return new CrossSection
        {
            Tag = "fire-rect",
            Areas = [area]
        };
    }
}
