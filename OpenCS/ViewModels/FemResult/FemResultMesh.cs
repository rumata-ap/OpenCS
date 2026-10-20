using System.Globalization;
using System.Windows.Media.Media3D;
using CScore.Fem;
using CScore.Fem.Loads;

namespace OpenCS.ViewModels.FemResult;

/// <summary>
/// Сетка схемы для показа результата расчёта (номера узлов и КЭ — теги сетки, как в моделях OpenSees и CSfea):
/// узлы, стержни, пластины с обходом контура, габарит. КЭ и узлы с нечисловыми тегами и КЭ с неизвестными узлами
/// пропускаются.
/// </summary>
public sealed class FemResultMesh
{
    readonly Dictionary<int, Point3D> _nodes = new();
    readonly Dictionary<string, int[]> _elementNodes = new(StringComparer.Ordinal);
    readonly List<(string Tag, int I, int J)> _bars = new();
    readonly List<(string Tag, int[] Contour)> _shells = new();
    readonly HashSet<string> _barTags = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> _owner = new(StringComparer.Ordinal);
    readonly Dictionary<string, List<string>> _byOwner = new(StringComparer.Ordinal);

    /// <param name="memberTags">КонЭ схемы: владелец КЭ признаётся, только если такой КонЭ есть (у КЭ импортной сетки
    /// без КонЭ <c>SourceMemberTag</c> может указывать на элемент программы-источника); null — по происхождению КЭ.</param>
    public FemResultMesh(IEnumerable<FemMeshNode> meshNodes, IEnumerable<FemElement> meshElements,
        IReadOnlySet<string>? memberTags = null)
    {
        var byTag = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        foreach (var n in meshNodes)
            if (FemMeshTopology.CanonicalNodeTag(n.NodeTag) is { } tag && byTag.TryAdd(tag, n))
                _nodes[int.Parse(tag, CultureInfo.InvariantCulture)] = new Point3D(n.X, n.Y, n.Z);

        var elements = new List<FemElement>();
        foreach (var e in meshElements)
        {
            if (!int.TryParse(e.ElemTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int num)) continue;
            if (FemElementGeometry.Of(e, byTag) is not { } g || !(g.IsBar || g.IsShell)) continue;
            string tag = num.ToString(CultureInfo.InvariantCulture);
            if (_elementNodes.ContainsKey(tag)) continue;
            var ids = g.NodeTags.Select(t => int.Parse(t, CultureInfo.InvariantCulture)).ToArray();
            _elementNodes[tag] = ids;
            // КЭ дискретизации КонЭ или КЭ импортной сетки, собранный в КонЭ (кБ ЛИРЫ, «КонЭ из выделенных КЭ»).
            if (e.SourceMemberTag is { } owner && !string.IsNullOrWhiteSpace(owner)
                && (memberTags?.Contains(owner.Trim()) ?? e.Origin != FemMember.MeshSourceImported))
            {
                owner = owner.Trim();
                _owner[tag] = owner;
                if (!_byOwner.TryGetValue(owner, out var list)) _byOwner[owner] = list = [];
                list.Add(tag);
            }
            if (g.IsBar) { _bars.Add((tag, ids[0], ids[1])); _barTags.Add(tag); }
            else _shells.Add((tag, g.Contour.Select(i => ids[i]).ToArray()));
            elements.Add(e);
        }
        Elements = elements;
        UsedNodes = _elementNodes.Values.SelectMany(ids => ids).Distinct().Order().ToArray();
        Diagonal = DiagonalOf(UsedNodes.Select(n => _nodes[n]));
    }

    /// <summary>Диагональ габарита точек, м; 0 — точек нет.</summary>
    public static double DiagonalOf(IEnumerable<Point3D> points)
    {
        var p = points.ToList();
        if (p.Count == 0) return 0;
        double dx = p.Max(v => v.X) - p.Min(v => v.X), dy = p.Max(v => v.Y) - p.Min(v => v.Y), dz = p.Max(v => v.Z) - p.Min(v => v.Z);
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>Узлы сетки по номеру, исходные координаты, м.</summary>
    public IReadOnlyDictionary<int, Point3D> Nodes => _nodes;
    /// <summary>Номера узлов, входящих в КЭ, по возрастанию.</summary>
    public IReadOnlyList<int> UsedNodes { get; }
    /// <summary>КЭ сетки, вошедшие в сетку результата (стержни и пластины).</summary>
    public IReadOnlyList<FemElement> Elements { get; }
    public IReadOnlyList<(string Tag, int I, int J)> Bars => _bars;
    public IReadOnlyList<(string Tag, int[] Contour)> Shells => _shells;
    /// <summary>Диагональ габарита узлов КЭ, м.</summary>
    public double Diagonal { get; }

    /// <summary>Узлы КЭ по тегу; null — КЭ нет в сетке.</summary>
    public IReadOnlyList<int>? NodesOf(string elemTag) => _elementNodes.TryGetValue(elemTag, out var ids) ? ids : null;

    /// <summary>Конструктивный элемент КЭ; null — КЭ без КонЭ (импортированная сетка) или его нет в сетке.</summary>
    public string? OwnerOf(string elemTag) => _owner.TryGetValue(elemTag, out var owner) ? owner : null;

    /// <summary>КЭ сетки конструктивного элемента (пусто — КонЭ не дискретизирован или его нет).</summary>
    public IReadOnlyList<string> ElementsOf(string ownerTag) =>
        _byOwner.TryGetValue(ownerTag.Trim(), out var list) ? list : [];

    /// <summary>КЭ — стержень сетки.</summary>
    public bool IsBar(string elemTag) => _barTags.Contains(elemTag);

    /// <summary>Отрезки (парами точек): стержни и контуры пластин.</summary>
    public Point3DCollection EdgeLines(Func<int, Point3D> position)
    {
        var pts = new Point3DCollection(2 * _bars.Count + _shells.Sum(s => 2 * s.Contour.Length));
        foreach (var (_, i, j) in _bars) { pts.Add(position(i)); pts.Add(position(j)); }
        foreach (var (_, c) in _shells)
            for (int k = 0; k < c.Length; k++) { pts.Add(position(c[k])); pts.Add(position(c[(k + 1) % c.Length])); }
        pts.Freeze();
        return pts;
    }

    /// <summary>
    /// Масштаб деформаций по умолчанию: наибольшее перемещение — 5 % диагонали габарита (округление до сотых);
    /// 1 — перемещений нет.
    /// </summary>
    public static double SuggestDeformScale(double diagonal, double maxDisplacement)
    {
        if (diagonal <= 1e-9 || maxDisplacement <= 1e-12) return 1;
        double v = 0.05 * diagonal / maxDisplacement;
        double r = Math.Round(v, 2);
        return r > 0 ? r : v;
    }
}
