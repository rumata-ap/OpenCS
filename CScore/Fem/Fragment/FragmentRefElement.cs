using CScore.Fem.Loads;
using CScore.Planar;

namespace CScore.Fem.Fragment;

/// <summary>
/// КЭ опорной сетки фрагмента — снимок КЭ родителя полосы (спека разд. 5): тег, тип, узлы в координатах (порядок
/// хранения), жёсткость, c1, ТЗА и толщина. Пишется при сборке и не меняется; по нему новые КЭ берут значения родителя.
/// </summary>
public sealed class FragmentRefElement
{
    public FragmentRefElement(string tag, string elemType, IReadOnlyList<PlanarVector3> points,
        int? stiffnessNum = null, double? foundationC1 = null, string? sourceMemberTag = null,
        string? reinforcementTypeIds = null, double? thicknessM = null)
    {
        Tag = tag;
        ElemType = elemType;
        Points = points;
        StiffnessNum = stiffnessNum;
        FoundationC1 = foundationC1;
        SourceMemberTag = sourceMemberTag;
        ReinforcementTypeIds = reinforcementTypeIds;
        ThicknessM = thicknessM;
        Geometry = FemElementGeometry.FromPoints(points) ?? throw new ArgumentException($"КЭ {tag}: ожидалось 2–4 узла.", nameof(points));
        Centroid = Geometry.Centroid;
        Direction = Geometry.IsBar ? (points[1] - points[0]).Normalize() : Geometry.ShellFrame().Z;
        Radius = points.Max(p => (p - Centroid).Length);
    }

    public string Tag { get; }
    public string ElemType { get; }
    /// <summary>Узлы в порядке хранения, м.</summary>
    public IReadOnlyList<PlanarVector3> Points { get; }
    public int? StiffnessNum { get; }
    public double? FoundationC1 { get; }
    /// <summary>КонЭ родителя, которому принадлежал КЭ.</summary>
    public string? SourceMemberTag { get; }
    /// <summary>ТЗА ЛИРЫ КЭ родителя (<see cref="FemElement.ReinforcementTypeIds"/>); null — не назначены.</summary>
    public string? ReinforcementTypeIds { get; }
    /// <summary>Собственная толщина КЭ родителя, м (<see cref="FemElement.ThicknessM"/>); null — не задана.</summary>
    public double? ThicknessM { get; }
    public FemElementGeometry Geometry { get; }
    public PlanarVector3 Centroid { get; }
    /// <summary>Пластина — единичная нормаль по обходу контура, стержень — единичная ось I → J.</summary>
    public PlanarVector3 Direction { get; }
    /// <summary>Наибольшее расстояние от центра до узла, м.</summary>
    public double Radius { get; }
    public bool IsBar => Geometry.IsBar;
    public bool IsShell => Geometry.IsShell;

    /// <summary>Опорная сетка по КЭ сетки родителя; КЭ с неизвестными узлами, не 2–4 узлами или вырожденные пропускаются.</summary>
    public static List<FragmentRefElement> From(IEnumerable<FemElement> elements, IReadOnlyDictionary<string, FemMeshNode> nodes)
    {
        var result = new List<FragmentRefElement>();
        foreach (var e in elements)
        {
            if (FemElementGeometry.Of(e, nodes) is not { } g) continue;
            if (g.IsBar ? g.Length < 1e-9 : g.Area < 1e-12) continue;
            result.Add(new(e.ElemTag, e.ElemType, g.Points, e.StiffnessNum, e.FoundationC1, e.SourceMemberTag,
                e.ReinforcementTypeIds, e.ThicknessM));
        }
        return result;
    }
}
