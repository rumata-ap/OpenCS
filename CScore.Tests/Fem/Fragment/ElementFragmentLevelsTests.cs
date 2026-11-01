using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Planar;
using Xunit;
using static CScore.Tests.Fem.Fragment.StoreyFragmentFixtures;

namespace CScore.Tests.Fem.Fragment;

/// <summary>Перекрытия i…k и «вся высота» фрагмента вокруг стен и колонн (спека призмы 2.2, срез 3).</summary>
public sealed class ElementFragmentLevelsTests
{
    static int s_id;

    static (FemMember Member, PlanarRegion Region) Plate(string tag, double x0, double y0, double x1, double y1, double z)
    {
        var frame = new Frame3D(new PlanarVector3(0, 0, z), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1));
        var region = PlanarRegion.CreateFromContour(Box(x0, y0, x1, y1, tag), null, frame, tag);
        region.Id = ++s_id;
        return (new FemMember { ElemTag = tag, ElemType = "shell", NodeIdsJson = "[]", PlanarRegionId = region.Id, Kind = "plate" }, region);
    }

    static List<StoreyFloorLevel> Levels(params (FemMember Member, PlanarRegion Region)[] items) =>
        StoreyFloorLevels.Find(items.Select(i => i.Member), items.Select(i => i.Region), [], []);

    /// <summary>Призма 3×3 м вокруг колонны в (5, 5).</summary>
    static readonly FragmentPrism Prism = FragmentPrism.Create(5, 5, 1, 0, 1.5, 1.5);

    /// <summary>Четыре перекрытия 10×10 м: 0, 3, 6, 9.</summary>
    static List<StoreyFloorLevel> Building() => Levels(
        Plate("П0", 0, 0, 10, 10, 0), Plate("П1", 0, 0, 10, 10, 3), Plate("П2", 0, 0, 10, 10, 6), Plate("П3", 0, 0, 10, 10, 9));

    [Fact]
    public void StoreyColumn_OwnFloorsAndNeighbours()
    {
        var levels = Building();
        var h = ElementFragmentLevels.Of(levels, Prism, 3, 6);
        Assert.Equal([0, 1, 2, 3], h.Available);
        Assert.Equal(1, h.Default);
        Assert.Equal((1, 2), (h.FullFrom, h.FullTo));

        var (z1, z2) = ElementFragmentLevels.Range(levels, 1, 2, 0, 9);
        Assert.Equal(1.5, z1, 6);
        Assert.Equal(7.5, z2, 6);
        Assert.Throws<ArgumentOutOfRangeException>(() => ElementFragmentLevels.Range(levels, 2, 1, 0, 9));
    }

    [Fact]
    public void MidStoreyColumnWithoutFloorsAtEnds_NearestBeyond()
    {
        // Колонна 1…5: внутри перекрытие 3, ниже 0, выше 6 — «вся высота» по ближайшим за концами.
        var h = ElementFragmentLevels.Of(Building(), Prism, 1, 5);
        Assert.Equal([0, 1, 2], h.Available);
        Assert.Equal(1, h.Default);
        Assert.Equal((0, 2), (h.FullFrom, h.FullTo));
    }

    [Fact]
    public void ColumnOnFoundationSlab_FoundationIsBottom()
    {
        var levels = Building();
        var h = ElementFragmentLevels.Of(levels, Prism, 0, 3);
        Assert.Equal([0, 1, 2], h.Available);
        Assert.Equal((0, 1), (h.FullFrom, h.FullTo));
        // Под фундаментной плитой этажа нет — z₁ за низом схемы с запасом.
        var (z1, _) = ElementFragmentLevels.Range(levels, 0, 1, -0.5, 9);
        Assert.Equal(-0.5 - StoreyFloorLevels.EdgeMarginM, z1, 6);
    }

    [Fact]
    public void ColumnBelowAllFloors_LowestAvailable()
    {
        // Колонна −3…−0,5 без плиты снизу: i — ближайшее выше (нижнее доступное).
        var h = ElementFragmentLevels.Of(Building(), Prism, -3, -0.5);
        Assert.Equal([0], h.Available);
        Assert.Equal((0, 0), (h.FullFrom, h.FullTo));
        Assert.Equal(0, h.Default);
    }

    [Fact]
    public void TallWall_AllFloorsInside()
    {
        var h = ElementFragmentLevels.Of(Building(), FragmentPrism.Create(5, 5, 1, 0, 4, 1.6), 0, 9);
        Assert.Equal([0, 1, 2, 3], h.Available);
        Assert.Equal((0, 3), (h.FullFrom, h.FullTo));
        Assert.Contains(h.Default!.Value, new[] { 1, 2 });
    }

    [Fact]
    public void FloorOfOtherWing_NotAvailable()
    {
        // На отметке 3 — только плита другого крыла (20…30), призма её не задевает.
        var levels = Levels(Plate("П0", 0, 0, 10, 10, 0), Plate("Крыло", 20, 0, 30, 10, 3), Plate("П2", 0, 0, 10, 10, 6));
        var h = ElementFragmentLevels.Of(levels, Prism, 0, 6);
        Assert.Equal([0, 2], h.Available);
        Assert.False(StoreyFloorLevels.Covers(levels[1], Prism.Corners()));
        Assert.True(StoreyFloorLevels.Covers(levels[0], Prism.Corners()));
    }

    [Fact]
    public void NoFloors_FullHeightByElements()
    {
        var h = ElementFragmentLevels.Of([], Prism, 0, 3);
        Assert.Empty(h.Available);
        Assert.Null(h.Default);
        Assert.Null(h.FullFrom);
        Assert.Null(h.FullTo);
        var (z1, z2) = ElementFragmentLevels.FullHeightNoFloors(0, 3);
        Assert.Equal(-StoreyFloorLevels.EdgeMarginM, z1, 6);
        Assert.Equal(3 + StoreyFloorLevels.EdgeMarginM, z2, 6);
    }
}
