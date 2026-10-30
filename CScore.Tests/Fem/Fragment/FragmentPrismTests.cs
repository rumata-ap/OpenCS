using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Planar;
using Xunit;
using static CScore.Tests.Fem.Fragment.StoreyFragmentFixtures;

namespace CScore.Tests.Fem.Fragment;

public sealed class FragmentPrismTests
{
    [Fact]
    public void RotatedPrism_FacesAndContains()
    {
        // Ось под 45°, полуразмеры 2 вдоль и 1 поперёк, центр (1, 1).
        var p = FragmentPrism.Create(1, 1, 1, 1, 2, 1);
        double s = Math.Sqrt(0.5);
        Assert.True(p.Contains(1 + 1.9 * s, 1 + 1.9 * s, 1e-9));
        Assert.False(p.Contains(1 + 2.1 * s, 1 + 2.1 * s, 1e-9));
        Assert.True(p.Contains(1 - 0.9 * s, 1 + 0.9 * s, 1e-9));
        Assert.False(p.Contains(1 - 1.1 * s, 1 + 1.1 * s, 1e-9));
        Assert.Equal(4, p.Faces.Count);
        Assert.All(p.Faces, f => Assert.Equal(1, f.Normal.Length, 9));
        Assert.All(p.Corners(), c => Assert.True(p.Contains(c.U, c.V, 1e-9)));
        Assert.Equal(8, Math.Abs(PlanarPolygonOps.SignedArea(p.Corners())), 9);
        Assert.True(PlanarPolygonOps.SignedArea(p.Corners()) > 0);
    }

    [Fact]
    public void CutOutward_ByNormalOrSide()
    {
        var bottom = new FragmentCut("C1", FragmentCutKind.End, FragmentCutSide.Bottom, 1.5, "1", []);
        var top = bottom with { Side = FragmentCutSide.Top };
        var lateral = bottom with { Side = FragmentCutSide.Lateral, Normal = new PlanarVector3(1, 0, 0) };
        Assert.Equal(new PlanarVector3(0, 0, -1), bottom.Outward);
        Assert.Equal(new PlanarVector3(0, 0, 1), top.Outward);
        Assert.Equal(new PlanarVector3(1, 0, 0), lateral.Outward);
        Assert.Throws<InvalidOperationException>(() => (bottom with { Side = FragmentCutSide.Lateral }).Outward);
    }
}

public sealed class FragmentPrismBuilderTests
{
    static readonly IReadOnlyList<(double Y, double Z)> Rect06x04 = [(-0.3, -0.2), (0.3, -0.2), (0.3, 0.2), (-0.3, 0.2)];

    static StoreyFragmentParentSnapshot Parent(FemNode[] nodes, (FemMember Member, PlanarRegion? Region)[] members) => new()
    {
        Nodes = nodes,
        Members = members.Select(m => m.Member).ToList(),
        Regions = members.Where(m => m.Region is not null).Select(m => m.Region!).ToList(),
        SectionOutline = id => id == 1 ? Rect06x04 : null,
    };

    [Fact]
    public void Column_WithSection_RectanglePlusOffset()
    {
        var parent = Parent([N("1", 2, 3, 0), N("2", 2, 3, 3)], [(Bar("C1", "1", "2"), null)]);
        var r = FragmentPrismBuilder.Build(parent, ["C1"], 1);

        Assert.Empty(r.Diagnostics);
        var p = r.Prism!;
        // Вертикальная колонна: местная Y — глобальная X (габарит Y сечения 0,6 — вдоль X).
        Assert.Equal((2.0, 3.0, 1.3, 1.2), (p.CenterX, p.CenterY, p.HalfLength, p.HalfWidth));
        Assert.Equal(1, Math.Abs(p.AxisX), 9);
    }

    [Fact]
    public void Column_Rotated90_LongSideAlongY()
    {
        var bar = Bar("C1", "1", "2");
        bar.RotationDeg = 90;
        var p = FragmentPrismBuilder.Build(Parent([N("1", 0, 0, 0), N("2", 0, 0, 3)], [(bar, null)]), ["C1"], 1).Prism!;
        Assert.True(p.Contains(0, 1.29, 1e-9));
        Assert.False(p.Contains(0, 1.31, 1e-9));
        Assert.True(p.Contains(1.19, 0, 1e-9));
        Assert.False(p.Contains(1.21, 0, 1e-9));
    }

    [Fact]
    public void Column_SectionFromParentElements()
    {
        var bar = Bar("C1", "1", "2");
        bar.CrossSectionId = null;
        var parent = new StoreyFragmentParentSnapshot
        {
            Nodes = [N("1", 0, 0, 0), N("2", 0, 0, 3)],
            Members = [bar],
            MeshElements = [new FemElement { ElemTag = "5", ElemType = "beam", SourceMemberTag = "C1", CrossSectionId = 1, NodeIdsJson = "[]" }],
            SectionOutline = id => id == 1 ? Rect06x04 : null,
        };
        var p = FragmentPrismBuilder.Build(parent, ["C1"], 1).Prism!;
        Assert.Equal((1.3, 1.2), (p.HalfLength, p.HalfWidth));
    }

    [Fact]
    public void Column_NoSection_Square2d()
    {
        var bar = Bar("C1", "1", "2");
        bar.CrossSectionId = 7;
        var p = FragmentPrismBuilder.Build(Parent([N("1", 1, 1, 0), N("2", 1, 1, 3)], [(bar, null)]), ["C1"], 1.5).Prism!;
        Assert.Equal((1.0, 1.0, 1.5, 1.5), (p.CenterX, p.CenterY, p.HalfLength, p.HalfWidth));
    }

    [Fact]
    public void Wall_LengthAndThicknessPlusOffset()
    {
        // Стена в плоскости y = 2, x 0…6, толщина 0,2.
        var p = FragmentPrismBuilder.Build(Parent([], [Wall("W1", 2, 0, 2, 0, 6, 3)]), ["W1"], 1).Prism!;
        Assert.Equal(3, p.CenterX, 9);
        Assert.Equal(2, p.CenterY, 9);
        Assert.Equal(4, p.HalfLength, 9);
        Assert.Equal(1.1, p.HalfWidth, 9);
        Assert.Equal(1, Math.Abs(p.AxisX), 9);
    }

    [Fact]
    public void TwoColumns_GlobalBounds()
    {
        var parent = Parent([N("1", 0, 0, 0), N("2", 0, 0, 3), N("3", 6, 0, 0), N("4", 6, 0, 3)],
            [(Bar("C1", "1", "2"), null), (Bar("C2", "3", "4"), null)]);
        var p = FragmentPrismBuilder.Build(parent, ["C1", "C2"], 1).Prism!;
        Assert.Equal((3.0, 0.0, 1.0, 0.0), (p.CenterX, p.CenterY, p.AxisX, p.AxisY));
        Assert.Equal(4.3, p.HalfLength, 9);
        Assert.Equal(1.2, p.HalfWidth, 9);
    }

    [Fact]
    public void NotVertical_Missing_BadOffset_Errors()
    {
        var parent = Parent([N("1", 0, 0, 3), N("2", 6, 0, 3)], [(Bar("B1", "1", "2"), null), Slab("S1", 1, 3)]);
        Assert.Contains(FragmentPrismBuilder.Build(parent, ["B1"], 1).Diagnostics, x => x.IsError && x.Code == "fragment_prism_not_vertical");
        Assert.Contains(FragmentPrismBuilder.Build(parent, ["S1"], 1).Diagnostics, x => x.IsError && x.Code == "fragment_prism_not_vertical");
        Assert.Contains(FragmentPrismBuilder.Build(parent, ["X"], 1).Diagnostics, x => x.Code == "fragment_prism_member_missing");
        Assert.Contains(FragmentPrismBuilder.Build(parent, ["B1"], 0).Diagnostics, x => x.Code == "fragment_prism_offset_invalid");
        Assert.Null(FragmentPrismBuilder.Build(parent, ["B1"], 1).Prism);
    }
}
