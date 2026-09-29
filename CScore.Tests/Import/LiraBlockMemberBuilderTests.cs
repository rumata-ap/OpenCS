using System.Text.Json;
using CScore.Fem;
using CScore.Import;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.Import;

public class LiraBlockMemberBuilderTests
{
    readonly List<FemMeshNode> _nodes = [];
    readonly List<FemElement> _elements = [];

    int Node(double x, double y, double z)
    {
        int tag = _nodes.Count + 1;
        _nodes.Add(new FemMeshNode { NodeTag = tag.ToString(), X = x, Y = y, Z = z });
        return tag;
    }

    string Bar(int a, int b, string section = "К 40x40")
    {
        string tag = (_elements.Count + 1).ToString();
        _elements.Add(new FemElement { ElemTag = tag, ElemType = "beam", NodeIdsJson = JsonSerializer.Serialize(new[] { a, b }), SectionTag = section });
        return tag;
    }

    /// <summary>Пластина по узлам в порядке обхода контура; четырёхузловая хранится, как в ЛИРЕ, «1 2 4 3».</summary>
    string Shell(string section = "Пл 200", params int[] ring)
    {
        if (ring.Length == 4) ring = [ring[0], ring[1], ring[3], ring[2]];
        string tag = (_elements.Count + 1).ToString();
        _elements.Add(new FemElement { ElemTag = tag, ElemType = "shell", NodeIdsJson = JsonSerializer.Serialize(ring), SectionTag = section, ThicknessM = 0.2 });
        return tag;
    }

    /// <summary>Прямоугольная сетка квадов в плоскости по функции точки (i, j) → (x, y, z); skip — пропуск ячейки (проём).</summary>
    List<string> Grid(int nx, int ny, Func<int, int, (double, double, double)> at, Func<int, int, bool>? skip = null, string section = "Пл 200")
    {
        var ids = new int[nx + 1, ny + 1];
        for (int i = 0; i <= nx; i++)
            for (int j = 0; j <= ny; j++)
            {
                var (x, y, z) = at(i, j);
                ids[i, j] = Node(x, y, z);
            }
        var tags = new List<string>();
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
                if (skip?.Invoke(i, j) != true)
                    tags.Add(Shell(section, ids[i, j], ids[i + 1, j], ids[i + 1, j + 1], ids[i, j + 1]));
        return tags;
    }

    LiraBlockMemberBuild Build(string type, IEnumerable<string> tags, int id = 5, string floor = "1-й этаж", string mark = "")
        => LiraBlockMemberBuilder.Build(new LiraBlockInfo(id, type, floor, mark, tags.ToList()), _nodes, _elements);

    static int[] Ends(FemMember m) => JsonSerializer.Deserialize<int[]>(m.NodeIdsJson)!;

    [Fact]
    public void Column_OfFiveBars_IsOneMemberBetweenEndNodes()
    {
        var n = Enumerable.Range(0, 6).Select(k => Node(0, 0, k * 0.6)).ToArray();
        var tags = Enumerable.Range(0, 5).Select(k => Bar(n[k], n[k + 1])).ToList();

        var build = Build("КОЛОННА", tags, mark: "К-1");

        var part = Assert.Single(build.Parts);
        Assert.Equal("КОЛОННА №5 [1-й этаж] К-1", part.Member.ElemTag);
        Assert.Equal("beam", part.Member.ElemType);
        Assert.Equal(FemMember.MeshSourceImported, part.Member.MeshSource);
        Assert.Equal("К 40x40", part.Member.SectionTag);
        Assert.Equal(new[] { n[0], n[5] }, Ends(part.Member).Order());
        Assert.Equal(tags.Order(), part.ElementTags.Order());
        Assert.Equal(new[] { n[0].ToString(), n[5].ToString() }, build.Nodes.Select(x => x.NodeTag).Order());
        Assert.Empty(build.Diagnostics);
    }

    [Fact]
    public void BentChain_SplitsAtKink_AndNamesParts()
    {
        int a = Node(0, 0, 0), b = Node(3, 0, 0), c = Node(3, 4, 0);
        var tags = new[] { Bar(a, b), Bar(b, c) };

        var build = Build("БАЛКА", tags, floor: "");

        Assert.Equal(2, build.Parts.Count);
        Assert.Equal(["БАЛКА №5 · 1", "БАЛКА №5 · 2"], build.Parts.Select(p => p.Member.ElemTag));
    }

    [Fact]
    public void StraightChain_SplitsWhereSectionChanges()
    {
        int a = Node(0, 0, 0), b = Node(3, 0, 0), c = Node(6, 0, 0);
        var build = Build("БАЛКА", [Bar(a, b, "Б 30x60"), Bar(b, c, "Б 30x50")]);

        Assert.Equal(["Б 30x60", "Б 30x50"], build.Parts.Select(p => p.Member.SectionTag));
    }

    [Fact]
    public void Wall_WithOpening_IsOneRegionWithHullAndHole()
    {
        // Стена 3×2 м в плоскости XZ (y = 0), ячейки 0,5 м, проём 1×1 м в середине.
        var tags = Grid(6, 4, (i, j) => (i * 0.5, 0, j * 0.5), skip: (i, j) => i is 2 or 3 && j is 1 or 2);

        var build = Build("СТЕНА", tags, mark: "С-1");

        var part = Assert.Single(build.Parts);
        Assert.Equal("wall", part.Member.Kind);
        Assert.Equal("import", part.Member.KindSource);
        Assert.Equal(0.2, part.Member.ThicknessM);
        var region = Assert.IsType<PlanarRegion>(part.Region);
        Assert.Equal("СТЕНА №5 [1-й этаж] С-1", region.Tag);
        Assert.Equal(4, PlanarRegionTopologyValidator.ToOpenLoop(region.Hull!.X, region.Hull.Y).X.Length);
        var hole = Assert.Single(region.Holes);
        Assert.Equal(4, PlanarRegionTopologyValidator.ToOpenLoop(hole.X, hole.Y).X.Length);
        Assert.Equal(6.0, Math.Abs(PlanarRegionTopologyValidator.SignedArea(region.Hull.X, region.Hull.Y)), 6);
        Assert.Equal(1.0, Math.Abs(PlanarRegionTopologyValidator.SignedArea(hole.X, hole.Y)), 6);
        Assert.Equal(1.0, region.Frame.LocalY.Z, 9); // стена: локальная Y — вверх
    }

    [Fact]
    public void Plate_KindFromGeometry_WhenTypeIsUnknown()
    {
        var tags = Grid(2, 2, (i, j) => (i * 1.0, j * 1.0, 3.0));

        var build = Build("Блок", tags, floor: "");

        var part = Assert.Single(build.Parts);
        Assert.Equal("plate", part.Member.Kind);
        Assert.Equal("auto", part.Member.KindSource);
        Assert.Equal(4.0, Math.Abs(PlanarRegionTopologyValidator.SignedArea(part.Region!.Hull!.X, part.Region.Hull.Y)), 6);
    }

    [Fact]
    public void LShapedWall_InPlan_IsTwoRegions()
    {
        // Две стены по 2 м, сходятся под прямым углом по вертикальной кромке x = 2, y = 0.
        var first = Grid(4, 2, (i, j) => (i * 0.5, 0, j * 0.5));
        var cornerTags = _nodes.Where(n => n.X == 2 && n.Y == 0).ToDictionary(n => n.Z, n => int.Parse(n.NodeTag));
        var ids = new int[5, 3];
        for (int i = 0; i <= 4; i++)
            for (int j = 0; j <= 2; j++)
                ids[i, j] = i == 0 ? cornerTags[j * 0.5] : Node(2, i * 0.5, j * 0.5);
        var second = new List<string>();
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 2; j++)
                second.Add(Shell("Пл 200", ids[i, j], ids[i + 1, j], ids[i + 1, j + 1], ids[i, j + 1]));

        var build = Build("СТЕНА", first.Concat(second));

        Assert.Equal(2, build.Parts.Count);
        Assert.All(build.Parts, p => Assert.Equal("wall", p.Member.Kind));
        Assert.Equal(first.Order(), build.Parts[0].ElementTags.Order());
    }

    [Fact]
    public void Triangles_FormRegion()
    {
        int a = Node(0, 0, 0), b = Node(2, 0, 0), c = Node(2, 2, 0), d = Node(0, 2, 0);
        var build = Build("ПЛИТА", [Shell("Пл 200", a, b, c), Shell("Пл 200", a, c, d)]);

        var part = Assert.Single(build.Parts);
        Assert.Equal("plate", part.Member.Kind);
        Assert.Equal(4.0, Math.Abs(PlanarRegionTopologyValidator.SignedArea(part.Region!.Hull!.X, part.Region.Hull.Y)), 6);
    }

    [Fact]
    public void MixedBlock_GivesBarAndPlate()
    {
        var plate = Grid(2, 2, (i, j) => (i * 1.0, j * 1.0, 3.0));
        int top = _nodes.First(n => n.X == 0 && n.Y == 0).NodeTag is var t ? int.Parse(t) : 0;
        int bottom = Node(0, 0, 0);
        var bar = Bar(bottom, top);

        var build = Build("Блок", plate.Append(bar));

        Assert.Equal(["beam", "shell"], build.Parts.Select(p => p.Member.ElemType));
    }

    [Fact]
    public void SquaresTouchingByCorner_AreSeparateParts()
    {
        int a = Node(0, 0, 0), b = Node(1, 0, 0), c = Node(1, 1, 0), d = Node(0, 1, 0);
        int e = Node(2, 1, 0), f = Node(2, 2, 0), g = Node(1, 2, 0);
        var build = Build("ПЛИТА", [Shell("Пл 200", a, b, c, d), Shell("Пл 200", c, e, f, g)]);

        Assert.Equal(2, build.Parts.Count);
    }

    [Fact]
    public void ContourTouchingItself_IsSkippedWithDiagnostic()
    {
        // Плита 3×3 без центральной и угловой ячеек: проём касается выреза в узле (2, 2).
        var tags = Grid(3, 3, (i, j) => (i, j, 0), skip: (i, j) => (i, j) is (1, 1) or (2, 2));

        var build = Build("ПЛИТА", tags);

        Assert.Empty(build.Parts);
        Assert.Contains(build.Diagnostics, d => d.Code == "lira_block_contour_ambiguous");
    }

    [Fact]
    public void QuadWithRepeatedNode_IsTriangle_AndZeroAreaElementIsSkipped()
    {
        int a = Node(0, 0, 0), b = Node(2, 0, 0), c = Node(2, 2, 0), d = Node(0, 2, 0), e = Node(1, 0, 0);
        var build = Build("ПЛИТА",
        [
            Shell("Пл 200", a, b, c, c),   // треугольник четырёхузловым КЭ
            Shell("Пл 200", a, c, d),
            Shell("Пл 200", a, e, b),      // узлы на одной прямой — нулевая площадь
        ]);

        var part = Assert.Single(build.Parts);
        Assert.Equal(2, part.ElementTags.Count);
        Assert.Equal(4.0, Math.Abs(PlanarRegionTopologyValidator.SignedArea(part.Region!.Hull!.X, part.Region.Hull.Y)), 6);
        Assert.Single(build.Diagnostics, x => x.Code == "lira_block_element_degenerate");
    }

    [Fact]
    public void LiraQuad_NumberedAlongTwoSides_IsReadInContourOrder()
    {
        // Квадрат 2×2: узлы ЛИРЫ 1 (0,0), 2 (2,0), 3 (0,2), 4 (2,2) — обход 1→2→4→3.
        int a = Node(0, 0, 0), b = Node(2, 0, 0), c = Node(0, 2, 0), d = Node(2, 2, 0);
        _elements.Add(new FemElement { ElemTag = "1", ElemType = "shell", NodeIdsJson = JsonSerializer.Serialize(new[] { a, b, c, d }), SectionTag = "Пл 200" });

        var part = Assert.Single(Build("ПЛИТА", ["1"]).Parts);

        Assert.Equal(4.0, Math.Abs(PlanarRegionTopologyValidator.SignedArea(part.Region!.Hull!.X, part.Region.Hull.Y)), 6);
    }

    [Fact]
    public void MissingElement_IsReported()
    {
        int a = Node(0, 0, 0), b = Node(1, 0, 0);
        var build = Build("БАЛКА", [Bar(a, b), "999"]);

        Assert.Single(build.Parts);
        Assert.Contains(build.Diagnostics, d => d.Code == "lira_block_element_missing");
    }

    [Theory]
    [InlineData("СТЕНА №5 [1-й этаж] С-1", 5, "СТЕНА", "1-й этаж", "С-1")]
    [InlineData("Блок №27", 27, "Блок", "", "")]
    [InlineData("Блок №28 К-1", 28, "Блок", "", "К-1")]
    public void BlockTag_RoundTrips(string tag, int id, string type, string floor, string mark)
    {
        Assert.True(LiraBlockTags.TryParse(tag, out var pid, out var ptype, out var pfloor, out var pmark));
        Assert.Equal((id, type, floor, mark), (pid, ptype, pfloor, pmark));
        Assert.Equal(tag, LiraBlockTags.Format(id, type, floor, mark));
    }
}
