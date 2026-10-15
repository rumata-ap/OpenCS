using System.ComponentModel;
using System.Globalization;
using CScore.Fem;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Вид граничных условий подузла «Граничные условия».</summary>
public enum FemBoundaryKind { Supports, Springs, Releases, Foundation, RigidBodies }

/// <summary>Объекты схемы, на которых заданы ГУ группы: узлы конструктивного уровня, узлы и КЭ сетки.</summary>
public sealed record FemBoundaryTargets(
    IReadOnlyList<string> NodeTags, IReadOnlyList<string> MeshNodeTags, IReadOnlyList<string> MeshElemTags)
{
    public static readonly FemBoundaryTargets Empty = new([], [], []);
}

/// <summary>
/// Группа одинаковых ГУ в дереве схемы («Заделка — 12», «Kz = 5000 кН/м — 4») или одно жёсткое тело.
/// Объекты группы читаются по требованию — у шарниров и основания импортированных схем их могут быть тысячи.
/// </summary>
public sealed class FemBoundaryGroupItem
{
    readonly Func<FemBoundaryTargets> _targets;

    public FemBoundaryKind Kind { get; }
    public FemSchema Schema { get; }
    /// <summary>Подпись группы без счётчика.</summary>
    public string Label { get; }
    /// <summary>Число объектов (узлов или КЭ); у жёсткого тела — 0, счётчик не показывается.</summary>
    public int Count { get; }
    /// <summary>Происхождение для подписи: «вручную», «SCAD», «ЛИРА».</summary>
    public string? OriginText { get; }

    public string CountText => Count > 0 ? $" — {Count}" : "";

    internal FemBoundaryGroupItem(FemBoundaryKind kind, FemSchema schema, string label, int count, string? originText,
        Func<FemBoundaryTargets> targets)
    {
        Kind = kind;
        Schema = schema;
        Label = label;
        Count = count;
        OriginText = originText;
        _targets = targets;
    }

    /// <summary>Узлы и КЭ, на которых заданы ГУ группы.</summary>
    public FemBoundaryTargets Targets() => _targets();
}

/// <summary>Подузел одного вида ГУ («Закрепления», «Пружины»…) с группами; пустой скрыт.</summary>
public sealed class FemBoundaryCategoryNode : FemSubNode, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public FemBoundaryKind Kind { get; }
    public FemSchema Schema { get; }
    public FemTreeItems<FemBoundaryGroupItem> Groups { get; } = [];

    int _count;
    /// <summary>Число объектов с ГУ этого вида (у жёстких тел — число тел).</summary>
    public int Count
    {
        get => _count;
        private set
        {
            if (_count == value) return;
            _count = value;
            Notify(nameof(Count));
            Notify(nameof(IsVisible));
        }
    }

    public override bool IsVisible => _count > 0;

    internal FemBoundaryCategoryNode(FemBoundaryKind kind, FemSchema schema)
    {
        Kind = kind;
        Schema = schema;
    }

    internal void Reset(IReadOnlyList<FemBoundaryGroupItem> groups)
    {
        Groups.Reset(groups);
        Count = Kind == FemBoundaryKind.RigidBodies ? groups.Count : groups.Sum(g => g.Count);
    }

    /// <summary>Объекты всех групп подузла.</summary>
    public FemBoundaryTargets Targets() => FemBoundaryTree.Merge(Groups.Select(g => g.Targets()));

    void Notify(string name) => PropertyChanged?.Invoke(this, new(name));
}

/// <summary>Подузел «Граничные условия» схемы: закрепления, пружины, шарниры стержней, упругое основание, жёсткие
/// тела и объединения — уровня КонЭ и сетки вместе; пустой скрыт.</summary>
public sealed class FemBoundarySubNode : FemSubNode, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public FemSchema Schema { get; }
    public FemBoundaryCategoryNode Supports { get; }
    public FemBoundaryCategoryNode Springs { get; }
    public FemBoundaryCategoryNode Releases { get; }
    public FemBoundaryCategoryNode Foundation { get; }
    public FemBoundaryCategoryNode RigidBodies { get; }
    public FemBoundaryCategoryNode[] Children { get; }

    bool _visible;
    public override bool IsVisible => _visible;

    internal FemBoundarySubNode(FemSchema schema)
    {
        Schema = schema;
        Supports = new(FemBoundaryKind.Supports, schema);
        Springs = new(FemBoundaryKind.Springs, schema);
        Releases = new(FemBoundaryKind.Releases, schema);
        Foundation = new(FemBoundaryKind.Foundation, schema);
        RigidBodies = new(FemBoundaryKind.RigidBodies, schema);
        Children = [Supports, Springs, Releases, Foundation, RigidBodies];
    }

    /// <summary>Перечитывает ГУ схемы из БД.</summary>
    internal void Reload(DatabaseService db, IReadOnlyList<FemNode> nodes)
    {
        int id = Schema.Id;
        Supports.Reset(FemBoundaryTree.SupportGroups(Schema, nodes, db.GetFemMeshNodeSupports(id)));
        Springs.Reset(FemBoundaryTree.SpringGroups(Schema, db.GetFemSprings(id)));
        Releases.Reset(db.GetFemElementReleaseGroups(id).Select(g => FemBoundaryTree.ReleaseGroup(Schema, g.ReleaseI, g.ReleaseJ,
            g.Count, () => new([], [], db.GetFemElementTagsByReleases(id, g.ReleaseI, g.ReleaseJ)))).ToList());
        Foundation.Reset(db.GetFemFoundationGroups(id).Select(g => FemBoundaryTree.FoundationGroup(Schema, g.C1, g.Count,
            () => new([], [], db.GetFemElementTagsByFoundation(id, g.C1)))).ToList());
        RigidBodies.Reset(FemBoundaryTree.RigidBodyItems(Schema, db.GetFemRigidBodies(id)));

        bool visible = Children.Any(c => c.IsVisible);
        if (visible == _visible) return;
        _visible = visible;
        PropertyChanged?.Invoke(this, new(nameof(IsVisible)));
    }
}

/// <summary>Группировка и подписи ГУ для дерева схемы.</summary>
public static class FemBoundaryTree
{
    static readonly string[] ReleaseNames = ["N", "Qy", "Qz", "T", "My", "Mz"];

    /// <summary>Закрепления узлов КонЭ (<see cref="FemNode.DofMask"/>) и узлов сетки по маске и происхождению;
    /// записи с нулевой маской (отметка ручного узла сетки) не показываются.</summary>
    public static List<FemBoundaryGroupItem> SupportGroups(FemSchema schema, IEnumerable<FemNode> nodes,
        IEnumerable<FemMeshNodeSupport> meshSupports)
    {
        var entries = nodes.Where(n => n.DofMask != 0)
            .Select(n => (Mask: n.DofMask & FemBoundaryDofs.All, Origin: FemLoadOrigin.Manual, Mesh: false, Tag: n.NodeTag))
            .Concat(meshSupports.Where(s => (s.Mask & FemBoundaryDofs.All) != 0)
                .Select(s => (Mask: s.Mask & FemBoundaryDofs.All, s.Origin, Mesh: true, Tag: s.NodeTag)));
        return entries
            .GroupBy(e => (e.Mask, e.Origin))
            .OrderBy(g => SupportOrder(g.Key.Mask)).ThenBy(g => g.Key.Mask).ThenBy(g => OriginOrder(g.Key.Origin))
            .Select(g =>
            {
                var list = g.ToList();
                FemBoundaryTargets targets = new(
                    list.Where(e => !e.Mesh).Select(e => e.Tag).ToList(), list.Where(e => e.Mesh).Select(e => e.Tag).ToList(), []);
                return new FemBoundaryGroupItem(FemBoundaryKind.Supports, schema, SupportLabel(g.Key.Mask), list.Count,
                    FemMeshNodeRow.DescribeOrigin(g.Key.Origin), () => targets);
            })
            .ToList();
    }

    /// <summary>Пружины по набору жёсткостей и происхождению.</summary>
    public static List<FemBoundaryGroupItem> SpringGroups(FemSchema schema, IEnumerable<FemSpring> springs) =>
        springs.Where(s => s.ActiveMask != 0)
            .GroupBy(s => (s.Kx, s.Ky, s.Kz, s.Kux, s.Kuy, s.Kuz, s.Origin))
            .OrderBy(g => g.Key.Kx).ThenBy(g => g.Key.Ky).ThenBy(g => g.Key.Kz)
            .ThenBy(g => g.Key.Kux).ThenBy(g => g.Key.Kuy).ThenBy(g => g.Key.Kuz).ThenBy(g => OriginOrder(g.Key.Origin))
            .Select(g =>
            {
                var list = g.ToList();
                FemBoundaryTargets targets = new(
                    list.Where(s => s.TargetKind == FemSpringTargetKinds.Node).Select(s => s.NodeTag).ToList(),
                    list.Where(s => s.TargetKind == FemSpringTargetKinds.MeshNode).Select(s => s.NodeTag).ToList(), []);
                return new FemBoundaryGroupItem(FemBoundaryKind.Springs, schema, SpringLabel(list[0].Stiffnesses), list.Count,
                    FemMeshNodeRow.DescribeOrigin(g.Key.Origin), () => targets);
            })
            .ToList();

    /// <summary>Группа КЭ с одинаковыми освобождениями концов (ГУ импортных КЭ — источник схемы).</summary>
    public static FemBoundaryGroupItem ReleaseGroup(FemSchema schema, int releaseI, int releaseJ, int count,
        Func<FemBoundaryTargets> targets) =>
        new(FemBoundaryKind.Releases, schema, ReleaseLabel(releaseI, releaseJ), count, SchemaOrigin(schema), targets);

    /// <summary>Группа КЭ с одинаковым коэффициентом постели (ГУ импортных КЭ — источник схемы).</summary>
    public static FemBoundaryGroupItem FoundationGroup(FemSchema schema, double c1, int count, Func<FemBoundaryTargets> targets) =>
        new(FemBoundaryKind.Foundation, schema, $"C1 = {Number(c1 / 1000)} {Loc.S("FemBoundaryUnitC1")}", count,
            SchemaOrigin(schema), targets);

    /// <summary>Жёсткие тела и объединения — по одному в дереве.</summary>
    public static List<FemBoundaryGroupItem> RigidBodyItems(FemSchema schema, IEnumerable<FemRigidBody> bodies) =>
        bodies.Select(b =>
        {
            string name = Loc.S(b.IsCoupled ? "FemBoundaryCoupled" : "FemBoundaryRigidBody");
            string number = b.SourceElemTag ?? b.Id.ToString(CultureInfo.InvariantCulture);
            string label = string.Format(Loc.S("FemBoundaryRigidBodyLabel"), name, number, b.MasterNodeTag,
                b.SlaveNodeTags.Count, FemBoundaryDofs.Describe(b.Mask));
            FemBoundaryTargets targets = new([], [b.MasterNodeTag, .. b.SlaveNodeTags], []);
            return new FemBoundaryGroupItem(FemBoundaryKind.RigidBodies, schema, label, 0,
                FemMeshNodeRow.DescribeOrigin(b.Origin), () => targets);
        }).ToList();

    /// <summary>Объединение объектов нескольких групп без повторов.</summary>
    public static FemBoundaryTargets Merge(IEnumerable<FemBoundaryTargets> parts)
    {
        var list = parts.ToList();
        return new(
            list.SelectMany(t => t.NodeTags).Distinct(StringComparer.Ordinal).ToList(),
            list.SelectMany(t => t.MeshNodeTags).Distinct(StringComparer.Ordinal).ToList(),
            list.SelectMany(t => t.MeshElemTags).Distinct(StringComparer.Ordinal).ToList());
    }

    /// <summary>«Заделка», «Шарнир» или перечень DOF (Tz, Tx Ty Tz Rz).</summary>
    public static string SupportLabel(int mask)
    {
        string text = FemNodeTreeItem.SupportsText(mask);
        return text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..];
    }

    /// <summary>Ненулевые жёсткости: «Kz = 5000 кН/м; Kux = 20 кН·м/рад».</summary>
    public static string SpringLabel(IReadOnlyList<double> k)
    {
        string[] names = ["Kx", "Ky", "Kz", "Kux", "Kuy", "Kuz"];
        var parts = Enumerable.Range(0, 6).Where(i => k[i] != 0)
            .Select(i => $"{names[i]} = {Number(k[i] / 1000)} {Loc.S(i < 3 ? "FemBoundaryUnitK" : "FemBoundaryUnitKr")}");
        return string.Join("; ", parts);
    }

    /// <summary>«I: My Mz; J: My Mz» в местных осях стержня; жёсткий конец — «—».</summary>
    public static string ReleaseLabel(int releaseI, int releaseJ) => $"I: {ReleaseText(releaseI)}; J: {ReleaseText(releaseJ)}";

    static string ReleaseText(int mask) => mask == 0
        ? "—"
        : string.Join(" ", ReleaseNames.Where((_, i) => (mask & (1 << i)) != 0));

    /// <summary>Ручные — первыми.</summary>
    static int OriginOrder(string origin) => origin == FemLoadOrigin.Manual ? 0 : 1;

    static string? SchemaOrigin(FemSchema schema) => schema.SourceType is "scad" or "lira"
        ? FemMeshNodeRow.DescribeOrigin(FemLoadOrigin.Import(schema.SourceType)) : null;

    /// <summary>Заделки, затем шарниры, затем прочие маски.</summary>
    static int SupportOrder(int mask) => mask switch { FemBoundaryDofs.All => 0, 7 => 1, _ => 2 };

    static string Number(double value) => value.ToString("G6", CultureInfo.CurrentCulture);
}
