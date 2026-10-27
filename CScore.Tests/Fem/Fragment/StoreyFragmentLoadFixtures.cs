using System.Globalization;
using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Planar;
using static CScore.Tests.Fem.Fragment.StoreyFragmentFixtures;

namespace CScore.Tests.Fem.Fragment;

/// <summary>Сетки и нагрузки родителя для тестов переноса нагрузок.</summary>
static class StoreyFragmentLoadFixtures
{
    static string S(int v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Сетка плиты 6×6 (центр в начале координат) на отметке <paramref name="z"/>: n×n Q4 в хранении «1 2 4 3», теги КЭ
    /// <paramref name="firstElem"/>… построчно от (−3, −3), узлы — с <paramref name="firstNode"/>; нормаль +Z.
    /// </summary>
    public static (List<FemMeshNode> Nodes, List<FemElement> Elements) SlabMesh(
        string member, double z, int n, int firstNode = 1001, int firstElem = 1)
    {
        var nodes = new List<FemMeshNode>();
        double h = 6.0 / n;
        for (int j = 0; j <= n; j++)
            for (int i = 0; i <= n; i++)
                nodes.Add(new FemMeshNode { NodeTag = S(firstNode + j * (n + 1) + i), X = -3 + i * h, Y = -3 + j * h, Z = z });
        int Tag(int i, int j) => firstNode + j * (n + 1) + i;
        var elements = new List<FemElement>();
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
                elements.Add(new FemElement
                {
                    ElemTag = S(firstElem + j * n + i), ElemType = "shell", SourceMemberTag = member, ThicknessM = 0.2,
                    NodeIdsJson = $"[{Tag(i, j)},{Tag(i + 1, j)},{Tag(i, j + 1)},{Tag(i + 1, j + 1)}]",
                });
        return (nodes, elements);
    }

    /// <summary>Цепочка КЭ колонны по отметкам <paramref name="zs"/>: узлы с <paramref name="firstNode"/>, КЭ с <paramref name="firstElem"/>.</summary>
    public static (List<FemMeshNode> Nodes, List<FemElement> Elements) ColumnMesh(
        string member, double x, double y, double[] zs, int firstNode = 101, int firstElem = 501)
    {
        var nodes = zs.Select((z, k) => new FemMeshNode { NodeTag = S(firstNode + k), X = x, Y = y, Z = z }).ToList();
        var elements = Enumerable.Range(0, zs.Length - 1).Select(k => new FemElement
        {
            ElemTag = S(firstElem + k), ElemType = "beam", SourceMemberTag = member, SectionTag = "1",
            NodeIdsJson = $"[{firstNode + k},{firstNode + k + 1}]",
        }).ToList();
        return (nodes, elements);
    }

    public static FemLoadCase Lc(int id, string tag) => new() { Id = id, Tag = tag };

    /// <summary>Родитель: колонна C1 (Id 1, узлы 1–2 от z = 0 до 6 в (0, 0)) и плита S1 6×6 на z = 3 с сеткой n×n.</summary>
    public static StoreyFragmentParentSnapshot Snapshot(
        int n = 4, double meshSize = 0.5, FemLoadCase[]? loadCases = null, FemLoadDefinition[]? definitions = null,
        FemMemberLoad[]? memberLoads = null, FemElementLoad[]? elementLoads = null, FemMeshNodeLoad[]? meshNodeLoads = null,
        FemNodeLoad[]? nodeLoads = null, FemKinematicLoad[]? kinematicLoads = null)
    {
        var (slabMember, slabRegion) = Slab("S1", 1, 3);
        slabRegion!.MeshMaxElementSizeM = meshSize;
        var column = Bar("C1", "1", "2");
        column.Id = 1;
        var (sn, se) = SlabMesh("S1", 3, n);
        var (cn, ce) = ColumnMesh("C1", 0, 0, [0, 1.5, 3, 4.5, 6]);
        return new StoreyFragmentParentSnapshot
        {
            Nodes = [N("1", 0, 0, 0), N("2", 0, 0, 6)],
            Members = [column, slabMember],
            Regions = [slabRegion],
            MeshNodes = [.. sn, .. cn],
            MeshElements = [.. se, .. ce],
            LoadCases = loadCases ?? [Lc(7, "Полезная")],
            LoadDefinitions = definitions ?? [],
            MemberLoads = memberLoads ?? [],
            ElementLoads = elementLoads ?? [],
            MeshNodeLoads = meshNodeLoads ?? [],
            NodeLoads = nodeLoads ?? [],
            KinematicLoads = kinematicLoads ?? [],
        };
    }

    public static StoreyFragmentDraft Build(StoreyFragmentParentSnapshot parent, double z1 = 1.5, double z2 = 4.5) =>
        StoreyFragmentBuilder.Build(parent, new StoreyFragmentRequest(z1, z2, "Этаж 2"));

    public static FemElementLoad ElementLoad(int lc, string kind, string axis, double[] values, params string[] elements)
    {
        var load = new FemElementLoad { LoadCaseId = lc, LoadKind = kind, Axis = axis, CoordinateSystem = "global" };
        load.SetTargetTags(elements);
        load.SetValues(values);
        return load;
    }

    public static PlanarVector3 At(FemNode n) => new(n.X, n.Y, n.Z);
}
