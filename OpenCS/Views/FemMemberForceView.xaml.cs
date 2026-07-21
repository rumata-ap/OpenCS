using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using CScore;
using CScore.Fem;
using OpenCS.OpenSees.Structural;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using OpenCS.Views.Helpers;

namespace OpenCS.Views;

/// <summary>Хост 2D-эпюр усилий одного конструктивного стержня с выбором компоненты.</summary>
public partial class FemMemberForceView : UserControl
{
    /// <summary>Дуговые координаты элемента и его концевые усилия.</summary>
    readonly record struct ElemArc(double Si, double Sj, FemElementEndForces Forces);

    readonly List<ElemArc> _elements = [];
    readonly string _memberTag;

    public FemMemberForceView(DatabaseService db, FemSchema schema, string memberTag, CalcResult result)
    {
        InitializeComponent();
        _memberTag = memberTag;

        BuildElements(db, schema, memberTag, result);

        componentBox.ItemsSource = System.Enum.GetValues<FemForceComponent>();
        componentBox.SelectedItem = FemForceComponent.Mz;
    }

    void BuildElements(DatabaseService db, FemSchema schema, string memberTag, CalcResult result)
    {
        var forcesByElem = FemMemberForceResultResolver.ResolveElementForces(result)
            .ToDictionary(f => f.ElemTag);

        var meshPos = new Dictionary<int, Point3D>();
        foreach (var n in db.GetFemMeshNodes(schema.Id))
            if (int.TryParse(n.NodeTag, out int t)) meshPos[t] = new Point3D(n.X, n.Y, n.Z);

        // Начало и направление стержня — по конструктивным концам, иначе по крайним mesh-узлам
        var (start, dir) = MemberAxis(db, schema, memberTag, meshPos);

        foreach (var e in db.GetFemMeshElements(schema.Id))
        {
            if (e.SourceMemberTag != memberTag) continue;
            if (!int.TryParse(e.ElemTag, out int etag) || !forcesByElem.TryGetValue(etag, out var f)) continue;
            var ends = JsonSerializer.Deserialize<int[]>(e.NodeIdsJson) ?? [];
            if (ends.Length != 2 || !meshPos.TryGetValue(ends[0], out var pa) || !meshPos.TryGetValue(ends[1], out var pb))
                continue;
            double si = Vector3D.DotProduct(pa - start, dir);
            double sj = Vector3D.DotProduct(pb - start, dir);
            _elements.Add(new ElemArc(si, sj, f));
        }
        _elements.Sort((a, b) => System.Math.Min(a.Si, a.Sj).CompareTo(System.Math.Min(b.Si, b.Sj)));
    }

    static (Point3D Start, Vector3D Dir) MemberAxis(
        DatabaseService db, FemSchema schema, string memberTag, Dictionary<int, Point3D> meshPos)
    {
        var member = db.GetFemMembers(schema.Id).FirstOrDefault(m => m.ElemTag == memberTag);
        var nodesByTag = new Dictionary<string, Point3D>();
        foreach (var n in db.GetFemNodes(schema.Id)) nodesByTag[n.NodeTag] = new Point3D(n.X, n.Y, n.Z);

        if (member?.Node1 is { } n1 && member.Node2 is { } n2 &&
            nodesByTag.TryGetValue(n1.ToString(), out var p1) && nodesByTag.TryGetValue(n2.ToString(), out var p2))
        {
            var d = p2 - p1;
            if (d.Length > 1e-9) { d.Normalize(); return (p1, d); }
        }
        // Fallback: направление по разбросу mesh-узлов
        if (meshPos.Count > 0)
        {
            var origin = meshPos.Values.First();
            var far = meshPos.Values.OrderByDescending(p => (p - origin).Length).First();
            var d = far - origin;
            if (d.Length > 1e-9) { d.Normalize(); return (origin, d); }
        }
        return (new Point3D(), new Vector3D(1, 0, 0));
    }

    void ComponentBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (componentBox.SelectedItem is not FemForceComponent comp) return;
        var segs = _elements
            .Select(el =>
            {
                var (vi, vj) = ComponentValues(comp, el.Forces);
                return new FemMemberForceCanvas.Segment(el.Si, el.Sj, vi / 1000, vj / 1000);
            })
            .ToList();
        bool isForce = comp is FemForceComponent.N or FemForceComponent.Qy or FemForceComponent.Qz;
        string unit = isForce ? Loc.S("UnitKN") : Loc.S("UnitKNm");
        string title = string.Format(Loc.S("FemMemberForceTitle"), _memberTag, comp) + $", {unit}";
        canvas.SetData(segs, title);
    }

    static (double Vi, double Vj) ComponentValues(FemForceComponent comp, FemElementEndForces f) => comp switch
    {
        FemForceComponent.N  => (-f.Ni,  f.Nj),
        FemForceComponent.Qy => (-f.Qyi, f.Qyj),
        FemForceComponent.Qz => (-f.Qzi, f.Qzj),
        FemForceComponent.Mx => (-f.Mxi, f.Mxj),
        FemForceComponent.My => (f.Myi, -f.Myj),
        FemForceComponent.Mz => (f.Mzi, -f.Mzj),
        _ => (0, 0)
    };
}
