using CScore;
using CScore.Planar;
using OpenCS.Gmsh.Generation;
using Xunit;

namespace OpenCS.Gmsh.Tests;

/// <summary>Точки и линии constraint-ов на кромке и пересечения кривых (CSfea 4г, срез 1): до нодирования
/// Gmsh на таких данных давал «Impossible to recover edge» и пустую сетку.</summary>
public sealed class PlanarBoundaryNodingMeshingTests
{
    const string Gmsh = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe";

    [Fact]
    public void Plan_SplitsBoundaryAtConstraintPointAndKeepsKeys()
    {
        var region = Slab();
        var plan = GmshPlanarGeoBuilder.Plan(region, Settings(), [Point("column", 2.5, 0)]);

        // Нижняя кромка (участок 0–1) разбита на две линии, остальные три — по одной.
        var bottom = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 0, 1);
        Assert.Equal(2, plan.BoundaryLines.Values.Count(key => key == bottom));
        Assert.Equal(5, plan.BoundaryLines.Count);
        // Точка на кромке в поверхность не встраивается — она уже вершина границы.
        Assert.DoesNotContain("Point {", plan.Geo);
        Assert.Contains("Physical Point(\"constraint:column:point\"", plan.Geo);
    }

    [Fact]
    public void Plan_CurveAlongBoundaryIsNotEmbeddedAndCrossingCurvesShareVertex()
    {
        var region = Slab();
        var plan = GmshPlanarGeoBuilder.Plan(region, Settings(),
        [
            Curve("edge-beam", (6, 1), (6, 3)),
            Curve("beam-x", (0, 2), (6, 2)),
            Curve("beam-y", (3, 0), (3, 4))
        ]);
        var lines = plan.Geo.Split(Environment.NewLine);

        // Правая кромка разбита в (6,1), (6,2), (6,3); балка по кромке — две её линии, не встраиваются.
        var right = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 1, 2);
        Assert.Equal(4, plan.BoundaryLines.Values.Count(key => key == right));
        // Пересечение балок (3,2) — общая точка: у каждой балки по две встроенные линии.
        var embedded = lines.Where(line => line.StartsWith("Line {")).ToArray();
        Assert.Equal(2, embedded.Length);
        Assert.All(embedded, line => Assert.Equal(2, line[(line.IndexOf('{') + 1)..line.IndexOf('}')].Split(", ").Length));
        Assert.Single(lines, line => line.StartsWith("Point(") && line.Contains("{3, 2, 0,"));
    }

    [Fact]
    public async Task BuildAsync_ColumnOnEdgeAndBeamAlongEdge()
    {
        var snapshot = await Mesh(Slab(),
        [
            Point("column", 2.5, 0),
            Curve("edge-beam", (6, 0), (6, 4))
        ]);

        Assert.True(snapshot.IsCalculable, string.Join(Environment.NewLine, snapshot.Diagnostics));
        var column = Mapping(snapshot, "column");
        var node = snapshot.Nodes[Assert.Single(column.PointNodeIndices)];
        Assert.Equal(2.5, node.U, 9);
        Assert.Equal(0, node.V, 9);
        // Узел колонны — в цепочке нижней кромки.
        var bottom = snapshot.BoundaryMappings.Single(m => m.Key == new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 0, 1));
        Assert.Contains(column.PointNodeIndices[0], bottom.NodeIndices);
        // Балка по кромке — цепочка рёбер правой кромки целиком.
        var beam = Mapping(snapshot, "edge-beam");
        var right = snapshot.BoundaryMappings.Single(m => m.Key == new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 1, 2));
        Assert.Equal(right.NodeIndices.Count - 1, beam.OrderedCurveEdges.Count);
    }

    [Fact]
    public async Task BuildAsync_BeamEdgeToEdgeAndCrossingBeams()
    {
        var snapshot = await Mesh(Slab(),
        [
            Curve("beam-x", (0, 2), (6, 2)),
            Curve("beam-y", (3, 0), (3, 4))
        ]);

        Assert.True(snapshot.IsCalculable, string.Join(Environment.NewLine, snapshot.Diagnostics));
        var x = Mapping(snapshot, "beam-x");
        var y = Mapping(snapshot, "beam-y");
        Assert.NotEmpty(x.OrderedCurveEdges);
        Assert.NotEmpty(y.OrderedCurveEdges);
        // Общий узел в точке пересечения.
        var xNodes = x.OrderedCurveEdges.SelectMany(e => new[] { e.A, e.B }).ToHashSet();
        var common = y.OrderedCurveEdges.SelectMany(e => new[] { e.A, e.B }).Where(xNodes.Contains).Distinct().ToArray();
        var cross = snapshot.Nodes[Assert.Single(common)];
        Assert.Equal(3, cross.U, 9);
        Assert.Equal(2, cross.V, 9);
        // Концы балок — на кромках.
        Assert.Contains(snapshot.BoundaryMappings, m => m.NodeIndices.Contains(x.OrderedCurveEdges[0].A));
    }

    [Fact]
    public async Task BuildAsync_KeepVerticesAddsNoNodesBetweenCurveVertices()
    {
        var keep = Curve("junction", (0, 1.5), (1.5, 1.5), (3, 1.5), (4.5, 1.5), (6, 1.5));
        keep.KeepVertices = true;
        var snapshot = await Mesh(Slab(), [keep]);

        Assert.True(snapshot.IsCalculable, string.Join(Environment.NewLine, snapshot.Diagnostics));
        // Шаг вершин 1,5 м при размере КЭ 0,5 м — без флага Gmsh поставил бы по 3 ребра на звено.
        Assert.Equal(4, Mapping(snapshot, "junction").OrderedCurveEdges.Count);
    }

    static async Task<PlanarMeshSnapshot> Mesh(PlanarRegion region, IReadOnlyList<PlanarConstraintObject> constraints)
    {
        var root = Path.Combine(Path.GetTempPath(), "opencs-gmsh-noding", Guid.NewGuid().ToString("N"));
        try
        {
            var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions { ExecutablePath = Gmsh, ArtifactRoot = root });
            return await mesher.BuildAsync(new PlanarMeshingRequest(region, Settings(), constraints));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    static PlanarConstraintMeshMapping Mapping(PlanarMeshSnapshot snapshot, string id) =>
        snapshot.ConstraintMappings.Single(m => m.ConstraintObjectId == id);

    static PlanarRegion Slab() =>
        PlanarRegion.CreateFromContour(new Contour { X = [0, 6, 6, 0], Y = [0, 0, 4, 4] }, frame: Frame3D.Identity);

    static PlanarMeshSettings Settings() => new(0.5, 6, PlanarMeshElementMode.Quads);

    static PlanarConstraintObject Point(string id, double u, double v) =>
        PlanarConstraintObject.Point(id, new PlanarPoint2D(u, v),
            new PlanarStructuralFacet(PlanarStructuralKind.None), new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint));

    static PlanarConstraintObject Curve(string id, params (double U, double V)[] points) =>
        PlanarConstraintObject.Curve(id, points.Select(p => new PlanarPoint2D(p.U, p.V)),
            new PlanarStructuralFacet(PlanarStructuralKind.None), new PlanarMeshFacet(PlanarMeshKind.ConformingPartition));
}
