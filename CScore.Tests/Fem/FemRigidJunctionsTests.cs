using CScore.Fem;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.Fem;

public sealed class FemRigidJunctionsTests
{
    const int Square40 = 1, Square30 = 2, Rib = 3;

    static readonly Dictionary<int, IReadOnlyList<(double Y, double Z)>> Outlines = new()
    {
        [Square40] = [(-0.2, -0.2), (0.2, -0.2), (0.2, 0.2), (-0.2, 0.2)],
        [Square30] = [(-0.15, -0.15), (0.15, -0.15), (0.15, 0.15), (-0.15, 0.15)],
        // Ребро: высота 0,5 по Y, ширина 0,3 по Z несимметрично (−0,2…0,1).
        [Rib] = [(-0.25, -0.2), (0.25, -0.2), (0.25, 0.1), (-0.25, 0.1)],
    };

    static Contour Box(double x0, double y0, double x1, double y1, string tag) =>
        new(new[] { x0, x1, x1, x0, x0 }, new[] { y0, y0, y1, y1, y0 }, tag);

    /// <summary>Плита 6×6 в плоскости z = 0 с центром в начале координат.</summary>
    static PlanarRegion Slab(params Contour[] holes) =>
        PlanarRegion.CreateFromContour(Box(-3, -3, 3, 3, "slab"), holes, Frame3D.Identity, "slab");

    static FemNode N(string tag, double x, double y, double z) => new() { NodeTag = tag, X = x, Y = y, Z = z };

    static FemMember Bar(string tag, string i, string j, int? section, double rotation = 0) => new()
    {
        ElemTag = tag, ElemType = "beam", NodeIdsJson = $"[{i},{j}]", CrossSectionId = section, RotationDeg = rotation
    };

    static RigidJunctionTraceSet Build(PlanarRegion region, FemNode[] nodes, FemMember[] members, RigidJunctionOptions? options = null) =>
        FemRigidJunctions.Build(region, nodes, members, id => Outlines.GetValueOrDefault(id), options);

    static double Area(RigidJunctionTrace trace) => trace.Pieces.Sum(p => Math.Abs(PlanarPolygonOps.SignedArea(p)));

    [Fact]
    public void Column_PerpendicularToSlab_SquareTrace()
    {
        var set = Build(Slab(), [N("1", 0, 0, -3), N("2", 0, 0, 0)], [Bar("C1", "1", "2", Square40)]);

        var trace = Assert.Single(set.Traces);
        Assert.Equal(RigidJunctionCase.Column, trace.Case);
        Assert.Equal("rigid-junction:2", trace.Id);
        Assert.Equal("2", trace.MasterNodeTag);
        Assert.Equal(0.16, Area(trace), 9);
        Assert.Equal((PlanarDofMask)63, trace.Mask);
        Assert.False(trace.Coupled);
        Assert.Empty(set.Diagnostics);
    }

    [Fact]
    public void Column_Rotated30_TraceRotated()
    {
        var set = Build(Slab(), [N("1", 0, 0, -3), N("2", 0, 0, 0)], [Bar("C1", "1", "2", Square40, rotation: 30)]);

        var trace = Assert.Single(set.Traces);
        Assert.Equal(0.16, Area(trace), 9);
        double r = 0.2 * Math.Sqrt(2), angle = 75 * Math.PI / 180;
        Assert.Contains(trace.Pieces[0], p =>
            Math.Abs(p.U - r * Math.Cos(angle)) < 1e-9 && Math.Abs(p.V - r * Math.Sin(angle)) < 1e-9 ||
            Math.Abs(p.U + r * Math.Cos(angle)) < 1e-9 && Math.Abs(p.V + r * Math.Sin(angle)) < 1e-9);
    }

    [Fact]
    public void Column_Inclined30_TraceStretched()
    {
        double s = Math.Sin(Math.PI / 6), c = Math.Cos(Math.PI / 6);
        var set = Build(Slab(), [N("1", -3 * s, 0, -3 * c), N("2", 0, 0, 0)], [Bar("C1", "1", "2", Square40)]);

        Assert.Equal(0.16 / c, Area(Assert.Single(set.Traces)), 9);
    }

    [Fact]
    public void ColumnsBelowAndAbove_OneBody_MasksUnited()
    {
        var below = Bar("C1", "1", "2", Square40);
        var above = Bar("C2", "2", "3", Square30);
        below.RigidJunctionMask = 7;
        above.RigidJunctionMask = 56;

        var set = Build(Slab(), [N("1", 0, 0, -3), N("2", 0, 0, 0), N("3", 0, 0, 3)], [below, above]);

        var trace = Assert.Single(set.Traces);
        Assert.Equal(["C1", "C2"], trace.MemberTags);
        Assert.Equal(0.16, Area(trace), 9);
        Assert.Equal((PlanarDofMask)63, trace.Mask);
    }

    [Fact]
    public void ColumnOff_IsExcludedFromBody()
    {
        var above = Bar("C2", "2", "3", Square30);
        above.RigidJunctionMode = FemMember.RigidJunctionOff;

        var set = Build(Slab(), [N("1", 0, 0, -3), N("2", 0, 0, 0), N("3", 0, 0, 3)],
            [Bar("C1", "1", "2", Square40), above]);

        Assert.Equal(["C1"], Assert.Single(set.Traces).MemberTags);
    }

    [Fact]
    public void Coupled_OnlyWhenAllMembersCoupled()
    {
        var below = Bar("C1", "1", "2", Square40);
        var above = Bar("C2", "2", "3", Square30);
        below.RigidJunctionKind = FemMember.RigidJunctionCoupled;
        var nodes = new[] { N("1", 0, 0, -3), N("2", 0, 0, 0), N("3", 0, 0, 3) };

        Assert.False(Assert.Single(Build(Slab(), nodes, [below, above]).Traces).Coupled);
        above.RigidJunctionKind = FemMember.RigidJunctionCoupled;
        Assert.True(Assert.Single(Build(Slab(), nodes, [below, above]).Traces).Coupled);
    }

    [Fact]
    public void ExplicitTrace_OverridesSection()
    {
        var column = Bar("C1", "1", "2", Square40);
        column.RigidJunctionTraceJson = "[[-0.5,-0.5,0],[0.5,-0.5,0],[0,0.5,0]]";

        var set = Build(Slab(), [N("1", 0, 0, -3), N("2", 0, 0, 0)], [column]);

        Assert.Equal(0.5, Area(Assert.Single(set.Traces)), 9);
        Assert.Empty(set.Diagnostics);
    }

    [Fact]
    public void ExplicitTrace_NotCoveringMaster_Warns()
    {
        var column = Bar("C1", "1", "2", Square40);
        column.RigidJunctionTraceJson = "[[1,1,0],[2,1,0],[2,2,0],[1,2,0]]";

        var set = Build(Slab(), [N("1", 0, 0, -3), N("2", 0, 0, 0)], [column]);

        Assert.Single(set.Traces);
        Assert.Contains(set.Diagnostics, d => d.Code == "rigid_junction_master_outside" && !d.IsError);
    }

    [Fact]
    public void AngleThreshold_DecidesColumnOrShallow()
    {
        // Стержень под 15° к плоскости плиты, второй конец над плитой.
        double a = 15 * Math.PI / 180;
        var nodes = new[] { N("1", 0, 0, 0), N("2", 3 * Math.Cos(a), 0, 3 * Math.Sin(a)) };
        var members = new[] { Bar("S1", "1", "2", Square40) };

        Assert.Single(Build(Slab(), nodes, members, new RigidJunctionOptions(AngleDeg: 10)).Traces);

        var shallow = Build(Slab(), nodes, members, new RigidJunctionOptions(AngleDeg: 20));
        Assert.Empty(shallow.Traces);
        Assert.Contains(shallow.Diagnostics, d => d.Code == "rigid_junction_shallow_angle");
    }

    [Fact]
    public void ColumnThroughSlabWithoutNode_MasterIsCrossingPoint()
    {
        var set = Build(Slab(), [N("1", 1, 1, -1), N("2", 1, 1, 1)], [Bar("C1", "1", "2", Square40)]);

        var trace = Assert.Single(set.Traces);
        Assert.Equal("rigid-junction:C1@cross", trace.Id);
        Assert.Null(trace.MasterNodeTag);
        Assert.Equal(new PlanarVector3(1, 1, 0), trace.MasterPoint);
        Assert.Equal(0.16, Area(trace), 9);
    }

    /// <summary>Без сечения — след по условному квадрату 10×10 см с предупреждением; сечение стержню не назначается.</summary>
    [Fact]
    public void NoSection_AssumedSquareTrace()
    {
        var bar = Bar("C1", "1", "2", null);
        var set = Build(Slab(), [N("1", 0, 0, -3), N("2", 0, 0, 0)], [bar]);

        var trace = Assert.Single(set.Traces);
        Assert.Equal(0.01, Area(trace), 9);
        Assert.Contains(set.Diagnostics, d => d.Code == "rigid_junction_section_assumed" && !d.IsError);
        Assert.Null(bar.CrossSectionId);
    }

    /// <summary>Стержень в плоскости плиты без сечения — полосы нет (связь общими узлами), условный квадрат не берётся.</summary>
    [Fact]
    public void Rib_NoSection_NoStrip()
    {
        var set = Build(Slab(), [N("1", -2, 0, 0), N("2", 2, 0, 0)], [Bar("B1", "1", "2", null)]);

        Assert.Empty(set.Traces);
        Assert.Contains(set.Diagnostics, d => d.Code == "rigid_junction_rib_section_missing" && !d.IsError);
        Assert.DoesNotContain(set.Diagnostics, d => d.Code == "rigid_junction_section_assumed");
    }

    [Fact]
    public void Rib_InPlane_StripOfSectionWidth()
    {
        var set = Build(Slab(), [N("1", -2, 0, 0), N("2", 2, 0, 0)], [Bar("B1", "1", "2", Rib)]);

        var trace = Assert.Single(set.Traces);
        Assert.Equal(RigidJunctionCase.Rib, trace.Case);
        Assert.Equal("rigid-rib:B1", trace.Id);
        Assert.Null(trace.MasterNodeTag);
        Assert.Equal(4 * 0.3, Area(trace), 9);
        // Ось Z стержня вдоль X направлена в −Y общих осей: Z ∈ [−0,2; 0,1] → V ∈ [−0,1; 0,2].
        Assert.Equal(-0.1, trace.Pieces[0].Min(p => p.V), 9);
        Assert.Equal(0.2, trace.Pieces[0].Max(p => p.V), 9);
    }

    [Fact]
    public void EdgeColumn_TraceClippedBySlabContour()
    {
        var set = Build(Slab(), [N("1", 2.9, 0, -3), N("2", 2.9, 0, 0)], [Bar("C1", "1", "2", Square40)]);

        var trace = Assert.Single(set.Traces);
        Assert.Equal(0.3 * 0.4, Area(trace), 9);
        Assert.True(trace.Pieces.SelectMany(p => p).All(p => p.U <= 3 + 1e-12));
    }

    [Fact]
    public void HoleCutsCorner()
    {
        var set = Build(Slab(Box(0, 0, 1, 1, "hole")), [N("1", 0, 0, -3), N("2", 0, 0, 0)], [Bar("C1", "1", "2", Square40)]);

        Assert.Equal(0.16 - 0.04, Area(Assert.Single(set.Traces)), 9);
    }

    [Fact]
    public void HoleStripSplitsTrace_OneBody()
    {
        var set = Build(Slab(Box(0.05, -1, 0.1, 1, "hole")), [N("1", 0, 0, -3), N("2", 0, 0, 0)], [Bar("C1", "1", "2", Square40)]);

        var trace = Assert.Single(set.Traces);
        Assert.True(trace.Pieces.Count >= 2);
        Assert.Equal(0.16 - 0.05 * 0.4, Area(trace), 9);
        Assert.DoesNotContain(set.Diagnostics, d => d.Code == "rigid_junction_master_outside");
    }

    [Fact]
    public void TraceTouchingOpeningEdge_NoSlivers()
    {
        var set = Build(Slab(Box(0.2, -1, 1, 1, "hole")), [N("1", 0, 0, -3), N("2", 0, 0, 0)], [Bar("C1", "1", "2", Square40)]);

        var trace = Assert.Single(set.Traces);
        Assert.Equal(0.16, Area(trace), 9);
        Assert.Single(trace.Pieces);
    }

    [Fact]
    public void ColumnInsideOpening_NoTrace()
    {
        var set = Build(Slab(Box(0.2, -1, 1, 1, "hole")), [N("1", 0.5, 0, -3), N("2", 0.5, 0, 0)], [Bar("C1", "1", "2", Square40)]);

        Assert.Empty(set.Traces);
    }

    [Fact]
    public void ToConstraints_RegionPerPieceWithMasterAndMask()
    {
        var column = Bar("C1", "1", "2", Square40);
        column.RigidJunctionMask = 7;
        var set = Build(Slab(Box(0.05, -1, 0.1, 1, "hole")), [N("1", 0, 0, -3), N("2", 0, 0, 0)], [column]);

        var constraints = set.ToConstraints();

        Assert.Equal(set.Traces[0].Pieces.Count, constraints.Count);
        Assert.Equal("rigid-junction:2#0", constraints[0].Id);
        Assert.All(constraints, c =>
        {
            Assert.Equal(PlanarConstraintGeometryKind.Region, c.Geometry.Kind);
            Assert.Equal(PlanarStructuralKind.RigidBody, c.StructuralFacet.Kind);
            Assert.Equal(PlanarMeshKind.EmbeddedRegion, c.MeshFacet.Kind);
            Assert.Equal(new PlanarMasterReference("fem-node", "2"), c.MasterReference);
            Assert.Equal((PlanarDofMask)7, c.DofMask);
            Assert.True(c.IsDerived);
            Assert.Equal("auto:junction", c.Provenance);
        });
    }
}
