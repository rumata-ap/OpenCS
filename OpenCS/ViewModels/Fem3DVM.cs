using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CScore.Fem;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Группа стержней одного цвета для 3D-отображения.</summary>
public record BarGroup(string Label, Color Color, Point3DCollection Points, double Thickness = 1.5);

/// <summary>ViewModel 3D-вида расчётной схемы или конструктивного элемента МКЭ.</summary>
public class Fem3DVM : ViewModelBase
{
    static readonly Color[] _palette =
    [
        Colors.SteelBlue, Colors.OrangeRed, Colors.ForestGreen, Colors.DarkOrange,
        Colors.MediumPurple, Colors.Crimson, Colors.Teal, Colors.Goldenrod,
        Colors.SlateBlue, Colors.DarkCyan,
    ];

    readonly int             _schemaId;
    readonly DatabaseService _db;
    readonly FemMember?      _memberOnly;      // показывать только КЭ члена
    readonly FemMember?      _highlightMember; // показывать всю схему, член подсвечен

    bool   _isLoading = true;
    bool   _noData;
    string _status    = "";

    public bool   IsLoading { get => _isLoading; private set { _isLoading = value; OnPropertyChanged(); } }
    public bool   NoData    { get => _noData;    private set { _noData    = value; OnPropertyChanged(); } }
    public string Status    { get => _status;    private set { _status    = value; OnPropertyChanged(); } }

    public List<BarGroup>      BarGroups       { get; private set; } = [];
    public MeshGeometry3D?     ShellMesh       { get; private set; }
    public MeshGeometry3D?     HiShellMesh     { get; private set; }
    public Point3DCollection?  ShellEdgePoints { get; private set; }
    public Point3DCollection?  NodePoints      { get; private set; }

    public FemSchemaSelectionVM? Selection { get; set; }
    public bool EditMode { get; set; }

    /// <summary>Тег узла и его позиция — для построения кликабельных прокси в режиме редактирования.</summary>
    public List<(string Tag, Point3D Position)> NodeProxies { get; private set; } = [];
    /// <summary>Тег элемента и его концы — для построения кликабельных прокси в режиме редактирования.</summary>
    public List<(string Tag, Point3D P1, Point3D P2)> BarProxies { get; private set; } = [];

    public static Color ShellColor   => Color.FromArgb(180, 160, 190, 210);
    public static Color ShellBgColor => Color.FromArgb(100, 180, 180, 190);
    public static Color ShellHiColor => Color.FromArgb(210, 255, 100,  50);

    /// <summary>Режим схемы — показывает все КЭ, раскрашенные по жёсткости.</summary>
    public Fem3DVM(FemSchema schema, DatabaseService db)
    {
        _schemaId = schema.Id;
        _db       = db;
        Status    = Loc.S("Fem3DLoading");
    }

    /// <summary>Режим конструктивного элемента — показывает только КЭ этого члена.</summary>
    public Fem3DVM(FemMember member, DatabaseService db)
    {
        _schemaId   = member.SchemaId;
        _db         = db;
        _memberOnly = member;
        Status      = Loc.S("Fem3DLoading");
    }

    /// <summary>Режим схемы с подсветкой — все КЭ схемы, КЭ члена выделены красным.</summary>
    public Fem3DVM(FemMember member, DatabaseService db, bool highlightOnSchema)
    {
        _schemaId        = member.SchemaId;
        _db              = db;
        _highlightMember = highlightOnSchema ? member : null;
        Status           = Loc.S("Fem3DLoading");
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        NoData    = false;
        Status    = Loc.S("Fem3DLoading");

        var allNodes    = await Task.Run(() => _db.GetFemNodes(_schemaId));
        var allElements = await Task.Run(() => _db.GetFemElements(_schemaId));

        // В режиме члена — фильтруем только нужные КЭ
        List<FemElement> elements;
        if (_memberOnly != null)
        {
            var memberIds = (JsonSerializer.Deserialize<int[]>(_memberOnly.ElemIdsJson) ?? [])
                            .ToHashSet();
            elements = allElements
                .Where(e => int.TryParse(e.ElemTag, out var id) && memberIds.Contains(id))
                .ToList();
        }
        else
        {
            elements = allElements;
        }

        if (allNodes.Count == 0 || elements.Count == 0)
        {
            IsLoading = false;
            NoData    = true;
            Status    = "";
            return;
        }

        var nodeMap = allNodes.ToDictionary(n => n.NodeTag, n => new Point3D(n.X, n.Y, n.Z));

        if (_highlightMember != null)
        {
            var hiTags  = (JsonSerializer.Deserialize<int[]>(_highlightMember.ElemIdsJson) ?? [])
                          .Select(id => id.ToString())
                          .ToHashSet(StringComparer.Ordinal);
            var hiElems = elements.Where(e => hiTags.Contains(e.ElemTag.Trim())).ToList();
            var bgElems = elements.Where(e => !hiTags.Contains(e.ElemTag.Trim())).ToList();

            var hiBars = hiElems.Where(e => e.ElemType == "beam").ToList();
            var bgBars = bgElems.Where(e => e.ElemType == "beam").ToList();

            var bars = new List<BarGroup>();
            if (bgBars.Count > 0)
                bars.Add(new BarGroup("", Colors.LightGray, BuildLinePoints(nodeMap, bgBars), 0.8));
            if (hiBars.Count > 0)
                bars.Add(new BarGroup(_highlightMember.Tag, Colors.OrangeRed, BuildLinePoints(nodeMap, hiBars), 3.0));
            BarGroups   = bars;
            HiShellMesh = BuildShellMesh(nodeMap, hiElems);
            ShellMesh   = BuildShellMesh(nodeMap, bgElems);
        }
        else
        {
            var allBars = elements.Where(e => e.ElemType == "beam").ToList();
            BarGroups   = BuildSectionColoredBars(nodeMap, allBars);
            ShellMesh   = BuildShellMesh(nodeMap, elements);
            HiShellMesh = null;
        }
        ShellEdgePoints = BuildShellEdges(nodeMap, elements);

        // Узлы: только те, что реально используются отображаемыми КЭ
        var usedNodeKeys = elements
            .SelectMany(e => JsonSerializer.Deserialize<int[]>(e.NodeIdsJson) ?? [])
            .Select(id => id.ToString())
            .ToHashSet();
        NodePoints = new Point3DCollection(
            allNodes.Where(n => usedNodeKeys.Contains(n.NodeTag))
                    .Select(n => new Point3D(n.X, n.Y, n.Z)));

        if (EditMode)
        {
            NodeProxies = allNodes
                .Where(n => usedNodeKeys.Contains(n.NodeTag))
                .Select(n => (n.NodeTag, new Point3D(n.X, n.Y, n.Z)))
                .ToList();
            BarProxies = elements
                .Where(e => e.ElemType == "beam")
                .Select(e => (Tag: e.ElemTag, Pair: GetBarPoints(nodeMap, e)))
                .Where(x => x.Pair.HasValue)
                .Select(x => (x.Tag, x.Pair!.Value.p1, x.Pair.Value.p2))
                .ToList();
        }

        OnPropertyChanged(nameof(BarGroups));
        OnPropertyChanged(nameof(ShellMesh));
        OnPropertyChanged(nameof(HiShellMesh));
        OnPropertyChanged(nameof(ShellEdgePoints));
        OnPropertyChanged(nameof(NodePoints));
        IsLoading = false;
        Status    = "";
    }

    // -------------------------------------------------------------------------

    List<BarGroup> BuildSectionColoredBars(Dictionary<string, Point3D> nodeMap, List<FemElement> bars)
    {
        var grouped = bars.GroupBy(e => e.SectionTag ?? "").OrderBy(g => g.Key).ToList();
        var result  = new List<BarGroup>(grouped.Count);

        for (int i = 0; i < grouped.Count; i++)
        {
            var g      = grouped[i];
            var color  = _palette[i % _palette.Length];
            var points = BuildLinePoints(nodeMap, g);
            if (points.Count > 0)
                result.Add(new BarGroup(g.Key, color, points));
        }

        return result;
    }

    MeshGeometry3D? BuildShellMesh(Dictionary<string, Point3D> nodeMap, List<FemElement> elements)
    {
        var shells = elements.Where(e => e.ElemType == "shell").ToList();
        if (shells.Count == 0) return null;

        var positions = new Point3DCollection(shells.Count * 4);
        var indices   = new Int32Collection(shells.Count * 6);
        int idx       = 0;

        foreach (var e in shells)
        {
            var ids = JsonSerializer.Deserialize<int[]>(e.NodeIdsJson) ?? [];
            var pts = ids.Select(id =>
                          nodeMap.TryGetValue(id.ToString(), out var p) ? p : (Point3D?)null)
                         .Where(p => p.HasValue)
                         .Select(p => p!.Value)
                         .ToArray();

            if (pts.Length == 3)
            {
                foreach (var p in pts) positions.Add(p);
                indices.Add(idx); indices.Add(idx + 1); indices.Add(idx + 2);
                idx += 3;
            }
            else if (pts.Length >= 4)
            {
                // ЛИРА хранит узлы как [n1,n2,n3,n4], геометрический обход: n1→n2→n4→n3
                for (int k = 0; k < 4; k++) positions.Add(pts[k]);
                indices.Add(idx); indices.Add(idx + 1); indices.Add(idx + 3);
                indices.Add(idx); indices.Add(idx + 3); indices.Add(idx + 2);
                idx += 4;
            }
        }

        return new MeshGeometry3D { Positions = positions, TriangleIndices = indices };
    }

    Point3DCollection? BuildShellEdges(Dictionary<string, Point3D> nodeMap, List<FemElement> elements)
    {
        var shells = elements.Where(e => e.ElemType == "shell").ToList();
        if (shells.Count == 0) return null;

        var seen = new HashSet<(int, int)>();
        var pts  = new Point3DCollection(shells.Count * 4);

        void AddEdge(int id1, int id2, Point3D p1, Point3D p2)
        {
            if (!seen.Add(id1 < id2 ? (id1, id2) : (id2, id1))) return;
            pts.Add(p1);
            pts.Add(p2);
        }

        foreach (var e in shells)
        {
            var ids = JsonSerializer.Deserialize<int[]>(e.NodeIdsJson) ?? [];
            var corners = ids
                .Select(id => nodeMap.TryGetValue(id.ToString(), out var p)
                    ? (nodeId: id, pos: p)
                    : (nodeId: -1, pos: default(Point3D)))
                .Where(x => x.nodeId >= 0)
                .ToArray();

            if (corners.Length == 3)
            {
                AddEdge(corners[0].nodeId, corners[1].nodeId, corners[0].pos, corners[1].pos);
                AddEdge(corners[1].nodeId, corners[2].nodeId, corners[1].pos, corners[2].pos);
                AddEdge(corners[2].nodeId, corners[0].nodeId, corners[2].pos, corners[0].pos);
            }
            else if (corners.Length >= 4)
            {
                // ЛИРА: геометрический обход n1→n2→n4→n3
                AddEdge(corners[0].nodeId, corners[1].nodeId, corners[0].pos, corners[1].pos);
                AddEdge(corners[1].nodeId, corners[3].nodeId, corners[1].pos, corners[3].pos);
                AddEdge(corners[3].nodeId, corners[2].nodeId, corners[3].pos, corners[2].pos);
                AddEdge(corners[2].nodeId, corners[0].nodeId, corners[2].pos, corners[0].pos);
            }
        }

        return pts.Count > 0 ? pts : null;
    }

    // -------------------------------------------------------------------------

    Point3DCollection BuildLinePoints(Dictionary<string, Point3D> nodeMap, IEnumerable<FemElement> elems)
    {
        var pts = new Point3DCollection();
        foreach (var e in elems)
        {
            var pair = GetBarPoints(nodeMap, e);
            if (pair.HasValue) { pts.Add(pair.Value.p1); pts.Add(pair.Value.p2); }
        }
        return pts;
    }

    static (Point3D p1, Point3D p2)? GetBarPoints(Dictionary<string, Point3D> nodeMap, FemElement e)
    {
        var ids = JsonSerializer.Deserialize<int[]>(e.NodeIdsJson) ?? [];
        if (ids.Length < 2) return null;
        if (!nodeMap.TryGetValue(ids[0].ToString(), out var p1)) return null;
        if (!nodeMap.TryGetValue(ids[1].ToString(), out var p2)) return null;
        return (p1, p2);
    }
}
