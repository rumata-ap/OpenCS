using CScore.Fem;
using CScore.Planar;
using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class StripSupportCandidateCollectorTests
{
    // Плита 0..12 × −1..1 в плоскости z = 0 с отверстием 4..6 × −0,5..0,5.
    static PlanarRegion Slab(Frame3D? frame = null)
    {
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [0, 12, 12, 0], Y = [-1, -1, 1, 1] },
            holes: [new Contour { X = [4, 6, 6, 4], Y = [-0.5, -0.5, 0.5, 0.5] }],
            frame: frame ?? Frame3D.Identity, tag: "slab");
        region.Id = 1;
        return region;
    }

    const int Tz = 1 << 2;
    const int Ry = 1 << 4;

    static FemNode Node(string tag, double x, double y, double z, int mask = 0) =>
        new() { Id = int.Parse(tag), NodeTag = tag, X = x, Y = y, Z = z, DofMask = mask };

    static StripSupportCollectionResult Collect(
        IReadOnlyList<FemNode>? nodes = null,
        IReadOnlyList<FemMember>? members = null,
        IReadOnlyList<PlanarRegion>? regions = null,
        PlanarRegion? slab = null) =>
        StripSupportCandidateCollector.Collect(slab ?? Slab(), nodes ?? [], members ?? [], regions ?? []);

    // ---------- узловые закрепления ----------

    [Fact]
    public void NodeRestrainedAlongNormal_BecomesCandidateWithMask()
    {
        var result = Collect([Node("7", 2, 0, 0, Tz | Ry)]);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(StripSupportKind.NodalRestraint, candidate.Kind);
        Assert.Equal("support:nodalrestraint:fem_node:7:0", candidate.Id);
        Assert.Equal([false, false, true, false, true, false], candidate.RestrainedDofs);
        Assert.Equal(2.0, candidate.Footprint.Points[0].U, 12);
        Assert.Equal(7, candidate.Source.NodeId);
    }

    [Theory]
    [InlineData(0.0, 0.3, 0.0, true)]    // на ребре hull
    [InlineData(13.0, 0.0, 0.0, false)]  // вне контура
    [InlineData(5.0, 0.0, 0.0, false)]   // строго в отверстии
    [InlineData(4.0, 0.0, 0.0, true)]    // на границе отверстия
    [InlineData(2.0, 0.0, 0.5, false)]   // над плоскостью
    public void NodePosition_DecidesMembership(double x, double y, double z, bool expected)
    {
        var result = Collect([Node("1", x, y, z, Tz)]);

        Assert.Equal(expected, result.Candidates.Count == 1);
    }

    [Fact]
    public void NodeRestrainedOnlyInPlane_IsNotSupport()
    {
        Assert.Empty(Collect([Node("1", 2, 0, 0, 1)]).Candidates);
    }

    [Fact]
    public void InclinedSlab_RequiresAllTranslationsWithNormalProjection()
    {
        // Плита, повёрнутая на 30° вокруг глобальной X: нормаль имеет компоненты Y и Z.
        double c = Math.Cos(Math.PI / 6), s = Math.Sin(Math.PI / 6);
        var frame = new Frame3D(PlanarVector3.Zero, new(1, 0, 0), new(0, c, s), new(0, -s, c));
        var point = PlanarBoundaryFrameConverter.ToGlobalPoint(frame, new(2, 0, 0));

        Assert.Empty(Collect([Node("1", point.X, point.Y, point.Z, Tz)], slab: Slab(frame)).Candidates);
        Assert.Single(Collect([Node("1", point.X, point.Y, point.Z, 0b111)], slab: Slab(frame)).Candidates);
    }

    [Fact]
    public void NodeWithNaN_IsSkippedWithWarning()
    {
        var result = Collect([Node("1", double.NaN, 0, 0, Tz)]);

        Assert.Empty(result.Candidates);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("plate_strip_support_source_invalid", diagnostic.Code);
        Assert.False(diagnostic.IsError);
    }

    // ---------- колонны ----------

    static FemMember Beam(int id, string nodeIdsJson) =>
        new() { Id = id, ElemTag = $"C{id}", ElemType = "beam", NodeIdsJson = nodeIdsJson };

    [Fact]
    public void ColumnsBelowAndAbove_GiveTwoCandidatesAtOnePoint()
    {
        var nodes = new[] { Node("1", 6, 0, -3), Node("2", 6, 0, 0), Node("3", 6, 0, 3) };
        var result = Collect(nodes, [Beam(10, "[1,2]"), Beam(11, "[2,3]")]);

        Assert.Equal(2, result.Candidates.Count);
        Assert.All(result.Candidates, c =>
        {
            Assert.Equal(StripSupportKind.Column, c.Kind);
            Assert.Equal(6.0, c.Footprint.Points[0].U, 12);
            Assert.All(c.RestrainedDofs, Assert.False);
        });
        Assert.NotEqual(result.Candidates[0].Id, result.Candidates[1].Id);
    }

    [Fact]
    public void TiltedColumnBeyondTolerance_IsNotSupport()
    {
        double dx = 3.0 * Math.Tan(10.0 * Math.PI / 180.0);
        var nodes = new[] { Node("1", 6 - dx, 0, -3), Node("2", 6, 0, 0) };

        Assert.Empty(Collect(nodes, [Beam(10, "[1,2]")]).Candidates);
    }

    [Fact]
    public void BeamInSlabPlane_IsNotColumn()
    {
        var nodes = new[] { Node("1", 1, 0, 0), Node("2", 3, 0, 0) };

        Assert.Empty(Collect(nodes, [Beam(10, "[1,2]")]).Candidates);
    }

    [Fact]
    public void ColumnOutsideSlab_IsNotSupport()
    {
        var nodes = new[] { Node("1", 20, 0, -3), Node("2", 20, 0, 0) };

        Assert.Empty(Collect(nodes, [Beam(10, "[1,2]")]).Candidates);
    }

    [Theory]
    [InlineData("[1,99]")]     // неразрешимый тег
    [InlineData("not json")]   // повреждённый JSON
    [InlineData("[1]")]        // меньше двух узлов
    [InlineData("[1,1]")]      // нулевая ось
    public void DamagedColumn_IsSkippedWithWarning(string nodeIdsJson)
    {
        var nodes = new[] { Node("1", 6, 0, 0), Node("2", 6, 0, -3) };

        var result = Collect(nodes, [Beam(10, nodeIdsJson)]);

        Assert.Empty(result.Candidates);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_support_source_invalid");
    }

    // ---------- стены ----------

    /// <summary>Вертикальная стена в плоскости x = xw поперёк плиты: локальные u вдоль Y,
    /// v вдоль Z; контур u ∈ [u0, u1] (от y = −1), v ∈ [v0, v1] (от z = zBase).</summary>
    static PlanarRegion Wall(int id, double xw, double u0, double u1, double v0, double v1, double zBase = -3.0)
    {
        var frame = new Frame3D(new(xw, -1, zBase), new(0, 1, 0), new(0, 0, 1), new(1, 0, 0));
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [u0, u1, u1, u0], Y = [v0, v0, v1, v1] }, frame: frame, tag: $"W{id}");
        region.Id = id;
        return region;
    }

    static FemMember WallMember(int regionId) =>
        new() { Id = 100 + regionId, ElemTag = $"shell{regionId}", ElemType = "shell", Kind = "wall", PlanarRegionId = regionId };

    [Fact]
    public void WallBelowSlab_TopEdgeInPlane_GivesSegmentAcrossSlab()
    {
        var result = Collect(members: [WallMember(2)], regions: [Wall(2, 2, 0, 2, 0, 3)]);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(StripSupportKind.Wall, candidate.Kind);
        Assert.Equal("support:wall:planar_region:W2:0", candidate.Id);
        AssertSegment(candidate, (2, -1), (2, 1));
    }

    [Fact]
    public void WallThroughSlab_GivesSegmentByEdgeInterpolation()
    {
        var result = Collect(members: [WallMember(2)], regions: [Wall(2, 2, 0, 2, 0, 6)]);

        AssertSegment(Assert.Single(result.Candidates), (2, -1), (2, 1));
    }

    [Fact]
    public void WallVertexInPlane_IsNotCountedTwice()
    {
        // Ромбовидная стена: вершины (u=1, v=0 → z=−3), (2,3 → z=0), (1,6), (0,3 → z=0).
        var frame = new Frame3D(new(2, -1, -3), new(0, 1, 0), new(0, 0, 1), new(1, 0, 0));
        var wall = PlanarRegion.CreateFromContour(
            new Contour { X = [1, 2, 1, 0], Y = [0, 3, 6, 3] }, frame: frame, tag: "W2");
        wall.Id = 2;

        var candidate = Assert.Single(Collect(members: [WallMember(2)], regions: [wall]).Candidates);

        AssertSegment(candidate, (2, -1), (2, 1));
    }

    [Fact]
    public void WallTouchingPlaneAtVertex_IsNotSupport()
    {
        var frame = new Frame3D(new(2, -1, -3), new(0, 1, 0), new(0, 0, 1), new(1, 0, 0));
        var wall = PlanarRegion.CreateFromContour(
            new Contour { X = [0, 2, 1], Y = [0, 0, 3] }, frame: frame, tag: "W2");
        wall.Id = 2;

        Assert.Empty(Collect(members: [WallMember(2)], regions: [wall]).Candidates);
    }

    [Fact]
    public void WallLongerThanSlab_IsClipped()
    {
        var result = Collect(members: [WallMember(2)], regions: [Wall(2, 2, -1, 3, 0, 3)]);

        AssertSegment(Assert.Single(result.Candidates), (2, -1), (2, 1));
    }

    [Fact]
    public void WallOnHullEdge_IsKept()
    {
        var result = Collect(members: [WallMember(2)], regions: [Wall(2, 0, 0, 2, 0, 3)]);

        AssertSegment(Assert.Single(result.Candidates), (0, -1), (0, 1));
    }

    [Fact]
    public void WallCutByHole_GivesTwoOrderedCandidates()
    {
        var result = Collect(members: [WallMember(2)], regions: [Wall(2, 5, 0, 2, 0, 3)]);

        Assert.Equal(2, result.Candidates.Count);
        Assert.Equal("support:wall:planar_region:W2:0", result.Candidates[0].Id);
        Assert.Equal("support:wall:planar_region:W2:1", result.Candidates[1].Id);
        AssertSegment(result.Candidates[0], (5, -1), (5, -0.5));
        AssertSegment(result.Candidates[1], (5, 0.5), (5, 1));
    }

    [Fact]
    public void RegionWithoutWallMember_IsIgnored()
    {
        var plateMember = new FemMember { Id = 5, ElemType = "shell", Kind = "plate", PlanarRegionId = 2 };

        Assert.Empty(Collect(members: [plateMember], regions: [Wall(2, 2, 0, 2, 0, 3)]).Candidates);
    }

    static void AssertSegment(StripSupportCandidate candidate, (double U, double V) a, (double U, double V) b)
    {
        Assert.Equal(PlanarConstraintGeometryKind.Curve, candidate.Footprint.Kind);
        var points = candidate.Footprint.Points;
        Assert.Equal(2, points.Count);
        var sorted = points.OrderBy(p => p.V).ThenBy(p => p.U).ToList();
        Assert.Equal(a.U, sorted[0].U, 9);
        Assert.Equal(a.V, sorted[0].V, 9);
        Assert.Equal(b.U, sorted[1].U, 9);
        Assert.Equal(b.V, sorted[1].V, 9);
    }
}
