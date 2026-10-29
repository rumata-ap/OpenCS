using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Planar;
using Xunit;
using static CScore.Tests.Fem.Fragment.StoreyFragmentFixtures;

namespace CScore.Tests.Fem.Fragment;

/// <summary>Поиск перекрытий и автоотметки фрагмента этажа (спека 2026-10-29).</summary>
public sealed class StoreyFloorLevelsTests
{
    static int s_id;

    /// <summary>Горизонтальная плита-прямоугольник в плане на отметке z.</summary>
    static (FemMember Member, PlanarRegion Region) Plate(string tag, double x0, double y0, double x1, double y1, double z)
    {
        var frame = new Frame3D(new PlanarVector3(0, 0, z), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1));
        var region = PlanarRegion.CreateFromContour(Box(x0, y0, x1, y1, tag), null, frame, tag);
        region.Id = ++s_id;
        return (new FemMember { ElemTag = tag, ElemType = "shell", NodeIdsJson = "[]", PlanarRegionId = region.Id, Kind = "plate" }, region);
    }

    static List<StoreyFloorLevel> Levels(params (FemMember Member, PlanarRegion Region)[] items) =>
        StoreyFloorLevels.Find(items.Select(i => i.Member), items.Select(i => i.Region), [], []);

    /// <summary>Сетка плиты n×n КЭ-оболочек (Q4 «1 2 4 3») [x0, x1]×[y0, y1] на отметке z; КЭ без КонЭ, если
    /// <paramref name="owner"/> null.</summary>
    static void MeshSlab(List<FemMeshNode> nodes, List<FemElement> elements, double x0, double y0, double x1, double y1,
        double z, int n, string? owner = null)
    {
        int first = nodes.Count + 1;
        double hx = (x1 - x0) / n, hy = (y1 - y0) / n;
        for (int j = 0; j <= n; j++)
            for (int i = 0; i <= n; i++)
                nodes.Add(new FemMeshNode { NodeTag = (first + j * (n + 1) + i).ToString(), X = x0 + i * hx, Y = y0 + j * hy, Z = z });
        int T(int i, int j) => first + j * (n + 1) + i;
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
                elements.Add(new FemElement
                {
                    ElemTag = $"e{elements.Count + 1}", ElemType = "shell", SourceMemberTag = owner,
                    NodeIdsJson = $"[{T(i, j)},{T(i + 1, j)},{T(i, j + 1)},{T(i + 1, j + 1)}]",
                });
    }

    [Fact]
    public void ThreeStoreys_MidHeights()
    {
        var levels = Levels(Plate("П0", 0, 0, 10, 10, 0), Plate("П1", 0, 0, 10, 10, 3.3), Plate("П2", 0, 0, 10, 10, 6.6));
        Assert.Equal([0, 3.3, 6.6], levels.Select(l => Math.Round(l.Z, 6)));
        Assert.Equal(100, levels[1].AreaM2, 6);

        var band = StoreyFloorLevels.Band(levels, 1, -0.2, 6.8);
        Assert.Equal(1.65, band.Z1, 6);
        Assert.Equal(4.95, band.Z2, 6);
        Assert.Equal(3.3, band.HeightBelow!.Value, 6);
        Assert.Equal(3.3, band.HeightAbove!.Value, 6);
        Assert.Empty(band.CrossedLevelZ);
    }

    [Fact]
    public void FoundationAndRoof_BandGoesToSchemaEdge()
    {
        var levels = Levels(Plate("ФП", 0, 0, 10, 10, 0), Plate("Покр", 0, 0, 10, 10, 3));

        var bottom = StoreyFloorLevels.Band(levels, 0, -12, 4.2);   // сваи до −12
        Assert.Equal(-13, bottom.Z1, 6);
        Assert.Null(bottom.HeightBelow);
        Assert.Equal(1.5, bottom.Z2, 6);

        var top = StoreyFloorLevels.Band(levels, 1, -12, 4.2);      // парапет до +4,2
        Assert.Equal(5.2, top.Z2, 6);
        Assert.Null(top.HeightAbove);
        Assert.Equal(1.5, top.Z1, 6);
    }

    [Fact]
    public void Neighbour_OnlyOverlappingInPlan()
    {
        // Стилобат 40×20 на +6; башня 10×10 над его углом (+9, +12); отдельный корпус 10×10 в стороне на +4 и +8.
        var levels = Levels(
            Plate("Стилобат", 0, 0, 40, 20, 6),
            Plate("Б1", 0, 0, 10, 10, 9), Plate("Б2", 0, 0, 10, 10, 12),
            Plate("К1", 60, 0, 70, 10, 4), Plate("К2", 60, 0, 70, 10, 8));
        int stylobate = StoreyFloorLevels.LevelAt(levels, 6)!.Value;
        var band = StoreyFloorLevels.Band(levels, stylobate, 0, 13);
        Assert.Equal(7.5, band.Z2, 6);            // сосед выше — башня +9, не корпус +8
        Assert.Equal(-1, band.Z1, 6);             // ниже стилобата в плане ничего — край схемы (корпус +4 в стороне)

        var k1 = StoreyFloorLevels.Band(levels, StoreyFloorLevels.LevelAt(levels, 4)!.Value, 0, 13);
        Assert.Equal(6, k1.Z2, 6);                // сосед — К2 (+8), середина 6
        Assert.Equal([6.0], k1.CrossedLevelZ);    // разрез z = 6 проходит по стилобату
    }

    [Fact]
    public void NearbyLevels_Merge_TiltedPlate_Ignored()
    {
        var tilted = Plate("Пандус", 0, 0, 10, 10, 0);
        double a = 10 * Math.PI / 180;
        tilted.Region.Frame = new Frame3D(new PlanarVector3(0, 0, 1), new(Math.Cos(a), 0, Math.Sin(a)), new(0, 1, 0),
            new(-Math.Sin(a), 0, Math.Cos(a)));
        var levels = Levels(Plate("П1а", 0, 0, 10, 10, 3.30), Plate("П1б", 10, 0, 20, 10, 3.28), tilted);
        var level = Assert.Single(levels);
        Assert.Equal(3.29, level.Z, 6);
        Assert.Equal(["П1б", "П1а"], level.MemberTags);
    }

    [Fact]
    public void MeshWithoutMembers_Levels_SmallLandingNotNeighbour()
    {
        // Импорт без КонЭ: плиты 20×20 на 0, 5,4 и 10,8 сеткой; площадка лестницы 2×2 на 2,7 внутри плана; стена (не
        // горизонтальна) не уровень.
        var nodes = new List<FemMeshNode>();
        var elements = new List<FemElement>();
        MeshSlab(nodes, elements, 0, 0, 20, 20, 0, 10);
        MeshSlab(nodes, elements, 8, 8, 10, 10, 2.7, 2);
        MeshSlab(nodes, elements, 0, 0, 20, 20, 5.4, 10);
        MeshSlab(nodes, elements, 0, 0, 20, 20, 10.8, 10);
        int w = nodes.Count + 1;
        nodes.Add(new FemMeshNode { NodeTag = w.ToString(), X = 0, Y = 0, Z = 0 });
        nodes.Add(new FemMeshNode { NodeTag = (w + 1).ToString(), X = 5, Y = 0, Z = 0 });
        nodes.Add(new FemMeshNode { NodeTag = (w + 2).ToString(), X = 0, Y = 0, Z = 5.4 });
        nodes.Add(new FemMeshNode { NodeTag = (w + 3).ToString(), X = 5, Y = 0, Z = 5.4 });
        elements.Add(new FemElement { ElemTag = "wall", ElemType = "shell", NodeIdsJson = $"[{w},{w + 1},{w + 2},{w + 3}]" });

        var levels = StoreyFloorLevels.Find([], [], nodes, elements);
        Assert.Equal([0, 2.7, 5.4, 10.8], levels.Select(l => Math.Round(l.Z, 6)));
        Assert.Equal(400, levels[2].AreaM2, 6);
        Assert.Equal(100, levels[2].ElementCount);
        Assert.Empty(levels[2].MemberTags);

        var band = StoreyFloorLevels.Band(levels, 2, 0, 10.8);
        Assert.Equal((2.7, 8.1), (Math.Round(band.Z1, 6), Math.Round(band.Z2, 6)));   // площадка 4 м² не сосед
        Assert.Equal(5.4, band.HeightBelow!.Value, 6);
    }

    [Fact]
    public void MeshCoveredByMember_NotCountedTwice()
    {
        var nodes = new List<FemMeshNode>();
        var elements = new List<FemElement>();
        var plate = Plate("П1", 0, 0, 10, 10, 3);
        MeshSlab(nodes, elements, 0, 0, 10, 10, 3, 5, owner: "П1");
        var level = Assert.Single(StoreyFloorLevels.Find([plate.Member], [plate.Region], nodes, elements));
        Assert.Equal(100, level.AreaM2, 6);
        Assert.Equal((1, 0), (level.MemberTags.Count, level.ElementCount));
    }

    [Fact]
    public void PickZ_HorizontalTiltedEmpty()
    {
        Assert.Equal(3.31, StoreyFloorLevels.PickZ([new(0, 0, 3.3), new(5, 0, 3.32), new(0, 5, 3.31)], out var e1)!.Value, 6);
        Assert.Null(e1);
        Assert.Null(StoreyFloorLevels.PickZ([new(0, 0, 3.3), new(5, 0, 4.0)], out var e2));
        Assert.Equal("StoreyFloorPickTilted", e2);
        Assert.Null(StoreyFloorLevels.PickZ([], out var e3));
        Assert.Equal("StoreyFloorPickEmpty", e3);

        var levels = Levels(Plate("П0", 0, 0, 10, 10, 0), Plate("П1", 0, 0, 10, 10, 3.3));
        Assert.Equal(1, StoreyFloorLevels.LevelAt(levels, 3.31));
        Assert.Null(StoreyFloorLevels.LevelAt(levels, 1.5));
    }
}
