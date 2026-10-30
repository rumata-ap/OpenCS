using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Planar;
using Xunit;
using static CScore.Tests.Fem.Fragment.StoreyFragmentFixtures;
using static CScore.Tests.Fem.Fragment.StoreyFragmentPrismFixtures;

namespace CScore.Tests.Fem.Fragment;

public sealed class StoreyFragmentPrismSlabTests
{
    static double Length((PlanarVector3 P, PlanarVector3 Q) s) => (s.Q - s.P).Length;

    static List<PlanarPoint2D> Hull(PlanarRegion r) =>
        Enumerable.Range(0, r.RequireHull().X.Count - 1).Select(i => new PlanarPoint2D(r.RequireHull().X[i], r.RequireHull().Y[i])).ToList();

    static PlanarStructuralFacet None => new(PlanarStructuralKind.None);

    static PlanarMeshFacet Embedded => new(PlanarMeshKind.EmbeddedPoint);

    [Fact]
    public void ColumnInSlab_SlabClipped_FourLateralEdgeCuts()
    {
        var d = BuildPrism(Square(1), [N("1", 0, 0, 0), N("2", 0, 0, 6)], [(Bar("C1", "1", "2"), null), Slab("S1", 1, 3)]);

        Assert.False(d.HasErrors);
        var slab = Assert.Single(d.Members, m => m.ParentMemberTag == "S1");
        Assert.Equal("S1", slab.Member.ElemTag);
        Assert.Equal(4, Area(slab.Region!), 6);
        var lateral = d.Cuts.Where(c => c.MemberTag == "S1").ToList();
        Assert.Equal(4, lateral.Count);
        Assert.All(lateral, c => Assert.Equal((FragmentCutKind.Edge, FragmentCutSide.Lateral, 3.0), (c.Kind, c.Side, c.Z)));
        Assert.All(lateral, c => Assert.Equal(2, c.Segments.Sum(Length), 6));
        Assert.Equal(4, lateral.Select(c => c.Outward).Distinct().Count());
        Assert.Equal(2, d.Members.Count(m => m.Member.ElemType == "beam"));
    }

    [Fact]
    public void SlabEdgeOnFace_NotACut()
    {
        // x −1…3: кромка плиты x = 3 совпадает с гранью.
        var d = BuildPrism(FragmentPrism.Create(1, 0, 1, 0, 2, 2), [], [Slab("S1", 1, 3)]);

        Assert.Equal(16, Area(Assert.Single(d.Members).Region!), 6);
        var lateral = d.Cuts.Where(c => c.MemberTag == "S1").ToList();
        Assert.Equal(3, lateral.Count);
        Assert.DoesNotContain(lateral, c => c.Outward == new PlanarVector3(1, 0, 0));
    }

    [Fact]
    public void SlabOutsidePrism_NotIncluded()
    {
        var frame = new Frame3D(new PlanarVector3(0, 0, 3), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1));
        var far = PlanarRegion.CreateFromContour(Box(10, -3, 16, 3, "S2"), [], frame, "S2");
        far.Id = 2;
        var s2 = (new FemMember { ElemTag = "S2", ElemType = "shell", NodeIdsJson = "[]", PlanarRegionId = 2, Kind = "plate", ThicknessM = 0.2 }, (PlanarRegion?)far);

        var d = BuildPrism(Square(1), [], [Slab("S1", 1, 3), s2]);
        Assert.False(d.HasErrors);
        Assert.Equal(new[] { "S1" }, d.Members.Select(m => m.Member.ElemTag));
    }

    [Fact]
    public void Constraints_RuleV()
    {
        var slab = Slab("S1", 1, 3);
        var r = slab.Region!;
        r.ConstraintObjects.Add(PlanarConstraintObject.Point("in", new(0.5, 0.5), None, Embedded));
        r.ConstraintObjects.Add(PlanarConstraintObject.Point("out", new(2, 2), None, Embedded));
        r.ConstraintObjects.Add(PlanarConstraintObject.Curve("line", [new(-2, 0), new(2, 0)], None, new(PlanarMeshKind.EmbeddedCurve)));
        r.ConstraintObjects.Add(PlanarConstraintObject.Region("cross", [new(0.8, -0.2), new(1.5, -0.2), new(1.5, 0.2), new(0.8, 0.2)],
            None, new(PlanarMeshKind.EmbeddedRegion)));
        r.ConstraintObjects.Add(PlanarConstraintObject.Region("inner", [new(-0.5, -0.5), new(0.5, -0.5), new(0.5, 0.5), new(-0.5, 0.5)],
            None, new(PlanarMeshKind.EmbeddedRegion)));

        var d = BuildPrism(Square(1), [], [slab]);

        var kept = Assert.Single(d.Members).Region!.ConstraintObjects;
        Assert.Equal(new[] { "in", "line", "inner" }, kept.Select(o => o.Id));
        var line = kept.Single(o => o.Id == "line").Geometry.Points;
        Assert.Equal((-1.0, 1.0), (line.Min(p => p.U), line.Max(p => p.U)));
        Assert.Equal(2, r.ConstraintObjects.Single(o => o.Id == "line").Geometry.Points.Max(p => p.U));
        var w = Assert.Single(d.Diagnostics, x => x.Code == "fragment_slab_constraints_lost");
        Assert.Contains("cross", w.Message);
        Assert.DoesNotContain("out", w.Message);
    }

    [Fact]
    public void BoundarySegments_RemappedLostAndFreeOnFace()
    {
        var slab = Slab("S1", 1, 3);
        var hull = Hull(slab.Region!);
        int Index(double u, double v) => hull.FindIndex(p => p.U == u && p.V == v);
        slab.Region!.BoundarySegments.Add(new BoundarySegment { StartVertex = Index(3, -3), EndVertex = Index(3, 3), Role = BoundaryRole.Support });
        slab.Region.BoundarySegments.Add(new BoundarySegment { StartVertex = Index(-3, -3), EndVertex = Index(3, -3), Role = BoundaryRole.BeamJunction });

        // x −1…3, y −4…4: правое ребро целиком внутри, нижнее разрезано гранью x = −1.
        var d = BuildPrism(FragmentPrism.Create(1, 0, 1, 0, 2, 4), [], [slab]);

        var region = Assert.Single(d.Members).Region!;
        var pts = Hull(region);
        var support = Assert.Single(region.BoundarySegments, b => b.Role == BoundaryRole.Support);
        Assert.Equal((3.0, -3.0, 3.0, 3.0), (pts[support.StartVertex].U, pts[support.StartVertex].V, pts[support.EndVertex].U, pts[support.EndVertex].V));
        var free = Assert.Single(region.BoundarySegments, b => b.Role == BoundaryRole.Free);
        Assert.Equal(StoreyFragmentBuilder.LateralCutProvenance, free.Provenance);
        Assert.Equal(-1, pts[free.StartVertex].U, 9);
        Assert.Equal(-1, pts[free.EndVertex].U, 9);
        Assert.Contains(d.Diagnostics, x => x.Code == "fragment_slab_constraints_lost" && x.Message.Contains("BeamJunction"));
    }
}
