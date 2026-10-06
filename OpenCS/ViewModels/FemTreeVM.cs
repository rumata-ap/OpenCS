using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CScore.Fem;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Корневой узел «Расчётные схемы» в дереве МКЭ.</summary>
class FemSchemasGroupNode
{
    public ObservableCollection<FemSchemaTreeVM> Schemas { get; } = [];

    public FemSchemasGroupNode(ObservableCollection<FemSchema> source, DatabaseService db,
                               ObservableCollection<CScore.ForceSet> forceSets)
    {
        foreach (var s in source)
            Schemas.Add(new FemSchemaTreeVM(s, db, forceSets));

        source.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                Schemas.Clear();
                return;
            }
            if (e.NewItems != null)
                foreach (FemSchema s in e.NewItems)
                    Schemas.Add(new FemSchemaTreeVM(s, db, forceSets));
            if (e.OldItems != null)
                foreach (FemSchema s in e.OldItems)
                {
                    var vm = Schemas.FirstOrDefault(x => x.Schema == s);
                    if (vm != null) Schemas.Remove(vm);
                }
        };
    }

    public void ReloadMeshSnapshot(int schemaId)
        => Schemas.FirstOrDefault(schema => schema.Schema.Id == schemaId)?.ReloadMeshSnapshot();
}

/// <summary>Подгруппа проверок по типу предельного состояния.</summary>
class FemChecksSubGroupNode
{
    public string                         GroupKey { get; }
    public string                         Label    { get; }
    public ObservableCollection<FemCheck> Checks   { get; } = [];

    public FemChecksSubGroupNode(string groupKey, string label)
    {
        GroupKey = groupKey;
        Label    = label;
    }
}

/// <summary>Корневой узел «МКЭ-проверки» — содержит 4 подгруппы (uls/sls/fire/other).</summary>
class FemChecksRootNode
{
    public FemChecksSubGroupNode[] Groups { get; }

    readonly FemChecksSubGroupNode _uls, _sls, _fire, _other;

    public FemChecksRootNode(ObservableCollection<FemCheck> source)
    {
        _uls   = new("uls",   Loc.S("FemGroupUls"));
        _sls   = new("sls",   Loc.S("FemGroupSls"));
        _fire  = new("fire",  Loc.S("FemGroupFire"));
        _other = new("other", Loc.S("FemGroupOther"));
        Groups = [_uls, _sls, _fire, _other];

        foreach (var c in source) Route(c);

        source.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                foreach (var g in Groups) g.Checks.Clear();
                return;
            }
            if (e.NewItems != null)
                foreach (FemCheck c in e.NewItems) Route(c);
            if (e.OldItems != null)
                foreach (FemCheck c in e.OldItems) Unroute(c);
        };
    }

    void Route(FemCheck c) => SubGroupFor(Classify(c)).Checks.Add(c);

    void Unroute(FemCheck c)
    {
        foreach (var g in Groups) g.Checks.Remove(c);
    }

    FemChecksSubGroupNode SubGroupFor(string key) => key switch
    {
        "uls"  => _uls,
        "sls"  => _sls,
        "fire" => _fire,
        _      => _other
    };

    static string Classify(FemCheck c) => c.NormCode switch
    {
        "steel_check" or "rc_check" => "uls",
        "rc_plate_check"             => ClassifyPlate(c),
        _                            => "other"
    };

    static string ClassifyPlate(FemCheck c)
    {
        var p = PlateCheckParams.Parse(c.ParamsJson);
        if (!string.IsNullOrEmpty(p.CheckGroup)) return p.CheckGroup;
        return p.Kind.EndsWith("_sls") ? "sls" : "uls";
    }
}

/// <summary>Обёртка над FemSchema для дерева МКЭ; экспонирует подузлы конструктивной модели и сетки.</summary>
class FemSchemaTreeVM
{
    public FemSchema    Schema   { get; }
    public FemSubNode[] SubNodes { get; }

    internal FemNodesSubNode    NodesSubNode    { get; }
    internal FemElementsSubNode ElementsSubNode { get; }
    internal FemMeshSnapshotSubNode MeshSnapshotSubNode { get; }
    internal FemForcesSubNode   ForcesSubNode   { get; }
    internal FemAnalysesSubNode AnalysesSubNode { get; }
    internal FemMeshGroupsSubNode   MeshGroupsSubNode   { get; }
    internal FemMemberGroupsSubNode MemberGroupsSubNode { get; }

    readonly DatabaseService _db;

    /// <summary>КонЭ дерева по Id — для раскладки проверок, нацеленных на одиночный элемент.</summary>
    Dictionary<int, FemMemberTreeItem> _memberItems = [];

    public FemSchemaTreeVM(FemSchema schema, DatabaseService db,
                           ObservableCollection<CScore.ForceSet> forceSets)
    {
        Schema = schema;
        _db    = db;

        NodesSubNode    = new FemNodesSubNode(this);
        ElementsSubNode = new FemElementsSubNode(this);
        MeshSnapshotSubNode = new FemMeshSnapshotSubNode(this);
        ForcesSubNode   = new FemForcesSubNode(schema, forceSets);
        AnalysesSubNode = new FemAnalysesSubNode(schema);
        MeshGroupsSubNode   = new FemMeshGroupsSubNode(schema);
        MemberGroupsSubNode = new FemMemberGroupsSubNode(schema);

        SubNodes =
        [
            NodesSubNode,
            ElementsSubNode,
            MeshSnapshotSubNode,
            MeshGroupsSubNode,
            MemberGroupsSubNode,
            ForcesSubNode,
            AnalysesSubNode,
        ];

        RefreshCounts();
        db.FemChecks.CollectionChanged += FemChecks_CollectionChanged;
    }

    void FemChecks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var item in _memberItems.Values) item.Checks.Clear();
            return;
        }
        if (e.OldItems != null)
            foreach (FemCheck c in e.OldItems)
                if (c.ElementId is int id && _memberItems.TryGetValue(id, out var item)) item.Checks.Remove(c);
        if (e.NewItems != null)
            foreach (FemCheck c in e.NewItems) RouteCheck(c);
    }

    void RouteCheck(FemCheck c)
    {
        if (c.TargetsElement && c.SchemaId == Schema.Id
            && _memberItems.TryGetValue(c.ElementId!.Value, out var item) && !item.Checks.Contains(c))
            item.Checks.Add(c);
    }

    /// <summary>Перечитывает КонЭ схемы в узлы «Стержни» и «Пластины» и раскладывает по ним их проверки.</summary>
    void ReloadMembers()
    {
        var members = _db.GetFemMembers(Schema.Id);
        var bars    = members.Where(m => m.ElemType == "beam").Select(m => new FemMemberTreeItem(m, Schema)).ToList();
        var shells  = members.Where(m => m.ElemType == "shell").Select(m => new FemMemberTreeItem(m, Schema)).ToList();
        ElementsSubNode.Bars.Members.Reset(bars);
        ElementsSubNode.Shells.Members.Reset(shells);
        NodesSubNode.Nodes.Reset(_db.GetFemNodes(Schema.Id).Select(n => new FemNodeTreeItem(n, this)));
        _memberItems = bars.Concat(shells).ToDictionary(i => i.Member.Id);
        foreach (var c in _db.FemChecks) RouteCheck(c);
    }

    void RefreshCounts()
    {
        var (nodes, bars, shells) = _db.GetFemTopologyCounts(Schema.Id);
        NodesSubNode.Count             = nodes;
        ElementsSubNode.BarCount       = bars;
        ElementsSubNode.ShellCount     = shells;
        ElementsSubNode.Bars.Count     = bars;
        ElementsSubNode.Shells.Count   = shells;
        ReloadMembers();
        RefreshMeshSnapshotCounts();
    }

    /// <summary>Какие узлы групп показывать у схемы без групп: «Группы КЭ» — при импортированной сетке,
    /// «Группы КонЭ» — если есть КонЭ или схема не чистый импорт сетки (КонЭ можно построить).</summary>
    void RefreshGroupNodes()
    {
        bool importedMesh = _db.HasFemImportedMesh(Schema.Id);
        MeshGroupsSubNode.CanHaveGroups   = importedMesh;
        MemberGroupsSubNode.CanHaveGroups = !importedMesh || ElementsSubNode.BarCount + ElementsSubNode.ShellCount > 0;
    }

    void RefreshMeshSnapshotCounts()
    {
        var (nodes, bars, shells) = _db.GetFemMeshSnapshotCounts(Schema.Id);
        MeshSnapshotSubNode.Nodes.Count             = nodes;
        MeshSnapshotSubNode.Elements.BarCount        = bars;
        MeshSnapshotSubNode.Elements.ShellCount      = shells;
        MeshSnapshotSubNode.Elements.Bars.Count      = bars;
        MeshSnapshotSubNode.Elements.Shells.Count    = shells;
        RefreshGroupNodes();
    }

    /// <summary>Асинхронно загружает узлы схемы из БД.</summary>
    internal Task<List<FemNode>> LoadNodesAsync()
        => Task.Run(() => _db.GetFemNodes(Schema.Id));

    /// <summary>Асинхронно загружает стержневые конструктивные элементы схемы из БД.</summary>
    internal Task<List<FemMember>> LoadBarsAsync()
        => Task.Run(() => _db.GetFemMembers(Schema.Id)
            .Where(e => e.ElemType == "beam").ToList());

    /// <summary>Асинхронно загружает пластинчатые конструктивные элементы схемы из БД.</summary>
    internal Task<List<FemMember>> LoadShellsAsync()
        => Task.Run(() => _db.GetFemMembers(Schema.Id)
            .Where(e => e.ElemType == "shell").ToList());

    /// <summary>Асинхронно загружает узлы сохранённой расчётной сетки схемы из БД.</summary>
    internal Task<List<FemMeshNode>> LoadMeshNodesAsync()
        => Task.Run(() => _db.GetFemMeshNodes(Schema.Id));

    /// <summary>Асинхронно загружает стержневые элементы сохранённой расчётной сетки схемы из БД.</summary>
    internal Task<List<FemElement>> LoadMeshBarsAsync()
        => Task.Run(() => _db.GetFemMeshElements(Schema.Id)
            .Where(e => e.ElemType == "beam").ToList());

    /// <summary>Асинхронно загружает пластинчатые элементы сохранённой расчётной сетки схемы из БД.</summary>
    internal Task<List<FemElement>> LoadMeshShellsAsync()
        => Task.Run(() => _db.GetFemMeshElements(Schema.Id)
            .Where(e => e.ElemType == "shell").ToList());

    /// <summary>Перечитывает счётчики после SaveFemTopology.</summary>
    public void ReloadTopology() => RefreshCounts();

    /// <summary>Перечитывает счётчики после сохранения mesh-снимка.</summary>
    public void ReloadMeshSnapshot() => RefreshMeshSnapshotCounts();
}

/// <summary>Базовый класс подузла расчётной схемы.</summary>
public abstract class FemSubNode
{
    /// <summary>Показывать ли подузел в дереве.</summary>
    public virtual bool IsVisible => true;
}

/// <summary>Подузел сохранённой расчётной сетки с дочерними узлами её узлов и элементов.</summary>
public class FemMeshSnapshotSubNode : FemSubNode
{
    public FemMeshNodesSubNode Nodes { get; }
    public FemMeshElementsSubNode Elements { get; }
    public FemSubNode[] Children { get; }

    internal FemSchemaTreeVM Owner { get; }
    internal FemMeshSnapshotSubNode(FemSchemaTreeVM owner)
    {
        Owner = owner;
        Nodes = new FemMeshNodesSubNode(owner);
        Elements = new FemMeshElementsSubNode(owner);
        Children = [Nodes, Elements];
    }
}

/// <summary>Подузел узлов сохранённой расчётной сетки.</summary>
public class FemMeshNodesSubNode : FemSubNode, System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    int _count;
    public int Count { get => _count; internal set { _count = value; PropertyChanged?.Invoke(this, new(nameof(Count))); } }
    internal FemSchemaTreeVM Owner { get; }
    internal FemMeshNodesSubNode(FemSchemaTreeVM owner) => Owner = owner;
}

/// <summary>Подузел конечных элементов сохранённой расчётной сетки — содержит два дочерних узла: Стержни и Пластины.</summary>
public class FemMeshElementsSubNode : FemSubNode, System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public FemMeshBarsSubNode   Bars     { get; }
    public FemMeshShellsSubNode Shells   { get; }
    public FemSubNode[]         Children { get; }

    internal FemSchemaTreeVM Owner { get; }

    int _barCount, _shellCount;
    public int BarCount   { get => _barCount;   internal set { _barCount   = value; PropertyChanged?.Invoke(this, new(nameof(BarCount)));   } }
    public int ShellCount { get => _shellCount; internal set { _shellCount = value; PropertyChanged?.Invoke(this, new(nameof(ShellCount))); } }

    internal FemMeshElementsSubNode(FemSchemaTreeVM owner)
    {
        Owner    = owner;
        Bars     = new FemMeshBarsSubNode(owner);
        Shells   = new FemMeshShellsSubNode(owner);
        Children = [Bars, Shells];
    }
}

/// <summary>Подузел «Стержни» сохранённой расчётной сетки — листовой, данные в DataGrid загружаются асинхронно.</summary>
public class FemMeshBarsSubNode : FemSubNode, System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    int _count;
    public int Count { get => _count; internal set { _count = value; PropertyChanged?.Invoke(this, new(nameof(Count))); } }
    internal FemSchemaTreeVM Owner { get; }
    internal FemMeshBarsSubNode(FemSchemaTreeVM owner) => Owner = owner;
}

/// <summary>Подузел «Пластины» сохранённой расчётной сетки — листовой, данные в DataGrid загружаются асинхронно.</summary>
public class FemMeshShellsSubNode : FemSubNode, System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    int _count;
    public int Count { get => _count; internal set { _count = value; PropertyChanged?.Invoke(this, new(nameof(Count))); } }
    internal FemSchemaTreeVM Owner { get; }
    internal FemMeshShellsSubNode(FemSchemaTreeVM owner) => Owner = owner;
}

/// <summary>Подузел «Узлы» — узлы конструктивной модели; таблица в DataGrid загружается асинхронно.</summary>
public class FemNodesSubNode : FemSubNode, System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    int _count;
    public int Count { get => _count; internal set { _count = value; PropertyChanged?.Invoke(this, new(nameof(Count))); } }
    public FemTreeItems<FemNodeTreeItem> Nodes { get; } = [];
    internal FemSchemaTreeVM Owner { get; }
    internal FemNodesSubNode(FemSchemaTreeVM owner) => Owner = owner;
}

/// <summary>Подузел «Конечные элементы» — содержит два дочерних узла: Стержни и Пластины.</summary>
public class FemElementsSubNode : FemSubNode, System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public FemBarsSubNode   Bars     { get; }
    public FemShellsSubNode Shells   { get; }
    public FemSubNode[]     Children { get; }

    internal FemSchemaTreeVM Owner { get; }

    // Счётчики дублируются здесь, чтобы показывать в заголовке без раскрытия
    int _barCount, _shellCount;
    public int BarCount   { get => _barCount;   internal set { _barCount   = value; PropertyChanged?.Invoke(this, new(nameof(BarCount)));   } }
    public int ShellCount { get => _shellCount; internal set { _shellCount = value; PropertyChanged?.Invoke(this, new(nameof(ShellCount))); } }

    internal FemElementsSubNode(FemSchemaTreeVM owner)
    {
        Owner    = owner;
        Bars     = new FemBarsSubNode(owner);
        Shells   = new FemShellsSubNode(owner);
        Children = [Bars, Shells];
    }
}

/// <summary>Подузел «Стержни» — стержневые КонЭ схемы; таблица в DataGrid загружается асинхронно.</summary>
public class FemBarsSubNode : FemSubNode, System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    int _count;
    public int Count { get => _count; internal set { _count = value; PropertyChanged?.Invoke(this, new(nameof(Count))); } }
    public FemMemberTreeItems Members { get; } = [];
    internal FemSchemaTreeVM Owner { get; }
    internal FemBarsSubNode(FemSchemaTreeVM owner) => Owner = owner;
}

/// <summary>Подузел «Пластины» — пластинчатые КонЭ схемы; таблица в DataGrid загружается асинхронно.</summary>
public class FemShellsSubNode : FemSubNode, System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    int _count;
    public int Count { get => _count; internal set { _count = value; PropertyChanged?.Invoke(this, new(nameof(Count))); } }
    public FemMemberTreeItems Members { get; } = [];
    internal FemSchemaTreeVM Owner { get; }
    public FemSchema Schema => Owner.Schema;
    internal FemShellsSubNode(FemSchemaTreeVM owner) => Owner = owner;
}

/// <summary>Конструктивный элемент в дереве схемы: сам элемент и проверки, нацеленные на него
/// (<see cref="FemCheck.ElementId"/>).</summary>
public class FemMemberTreeItem
{
    public FemMember Member { get; }
    public FemSchema Schema { get; }
    public ObservableCollection<FemCheck> Checks { get; } = [];

    public bool IsShell => Member.ElemType == "shell";

    /// <summary>Код типа для подписи: у пластин — плита/стена; у стержней не показывается.</summary>
    public string? TypeCode => IsShell ? Member.Kind ?? FemMemberTypes.Shell : null;

    internal FemMemberTreeItem(FemMember member, FemSchema schema)
    {
        Member = member;
        Schema = schema;
    }
}

/// <summary>Узел конструктивной модели в дереве схемы.</summary>
public class FemNodeTreeItem
{
    public FemNode Node { get; }
    internal FemSchemaTreeVM Owner { get; }
    public FemSchema Schema => Owner.Schema;

    /// <summary>Закрепления для подписи: «заделка», «шарнир» или перечень (Tx Tz Ry); пусто — свободен.</summary>
    public string Supports => SupportsText(Node.DofMask);

    internal FemNodeTreeItem(FemNode node, FemSchemaTreeVM owner)
    {
        Node  = node;
        Owner = owner;
    }

    static readonly string[] DofNames = ["Tx", "Ty", "Tz", "Rx", "Ry", "Rz"];

    public static string SupportsText(int mask) => mask switch
    {
        0  => "",
        63 => Loc.S("FemNodeSupportFixed"),
        7  => Loc.S("FemNodeSupportPinned"),
        _  => string.Join(" ", DofNames.Where((_, i) => (mask & (1 << i)) != 0)),
    };

    /// <summary>КонЭ, которым принадлежит узел (по тегу узла в <see cref="FemMember.NodeIdsJson"/>).</summary>
    public IReadOnlyList<FemMemberTreeItem> AdjacentMembers()
    {
        if (!int.TryParse(Node.NodeTag, out int tag)) return [];
        return Owner.ElementsSubNode.Bars.Members.Concat(Owner.ElementsSubNode.Shells.Members)
            .Where(i => NodeTags(i.Member).Contains(tag))
            .ToList();
    }

    static int[] NodeTags(FemMember member)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<int[]>(member.NodeIdsJson) ?? []; }
        catch (System.Text.Json.JsonException) { return []; }
    }
}

/// <summary>Список КонЭ узла дерева; перечитывается целиком одним уведомлением Reset.</summary>
public class FemMemberTreeItems : FemTreeItems<FemMemberTreeItem>;

/// <summary>Элементы узла дерева; перечитываются целиком одним уведомлением Reset.</summary>
public class FemTreeItems<T> : ObservableCollection<T>
{
    internal void Reset(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

/// <summary>Подузел групп схемы одного вида (<see cref="FemMemberGroup.Kind"/>) — фильтр над
/// <see cref="FemSchema.MemberGroups"/>. Вид группы после создания не меняется, поэтому достаточно
/// следить за составом коллекции.</summary>
public abstract class FemGroupsSubNode : FemSubNode, System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public FemSchema                            Schema { get; }
    public string                               Kind   { get; }
    public ObservableCollection<FemMemberGroup> Groups { get; } = [];
    public int Count => Groups.Count;

    bool _canHaveGroups = true;
    /// <summary>Схема может иметь группы этого вида; пустой узел без этой возможности скрыт.</summary>
    internal bool CanHaveGroups
    {
        get => _canHaveGroups;
        set { if (_canHaveGroups == value) return; _canHaveGroups = value; Notify(nameof(IsVisible)); }
    }

    public override bool IsVisible => Groups.Count > 0 || _canHaveGroups;

    protected FemGroupsSubNode(FemSchema schema, string kind)
    {
        Schema = schema;
        Kind   = kind;
        Rebuild();
        schema.MemberGroups.CollectionChanged += (_, e) =>
        {
            if (e.Action is NotifyCollectionChangedAction.Reset or NotifyCollectionChangedAction.Move)
                Rebuild();
            else
            {
                if (e.OldItems != null)
                    foreach (FemMemberGroup g in e.OldItems) Groups.Remove(g);
                if (e.NewItems != null)
                    foreach (FemMemberGroup g in e.NewItems)
                        if (g.Kind == Kind) Groups.Add(g);
            }
            Notify(nameof(Count));
            Notify(nameof(IsVisible));
        };
    }

    void Rebuild()
    {
        Groups.Clear();
        foreach (var g in Schema.MemberGroups)
            if (g.Kind == Kind) Groups.Add(g);
    }

    void Notify(string name) => PropertyChanged?.Invoke(this, new(name));
}

/// <summary>Подузел «Группы КЭ» — состав групп задан номерами КЭ сетки (группы ЛИРЫ, SCAD).</summary>
public class FemMeshGroupsSubNode(FemSchema schema) : FemGroupsSubNode(schema, FemMemberGroup.KindMesh);

/// <summary>Подузел «Группы КонЭ» — состав групп задан конструктивными элементами.</summary>
public class FemMemberGroupsSubNode(FemSchema schema) : FemGroupsSubNode(schema, FemMemberGroup.KindMembers);

/// <summary>Подузел «Расчёты OpenSees» — постановки линейного расчёта схемы.</summary>
public class FemAnalysesSubNode : FemSubNode, System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public FemSchema Schema { get; }
    public ObservableCollection<FemAnalysis> Analyses { get; }
    int _count;
    public int Count { get => _count; private set { _count = value; PropertyChanged?.Invoke(this, new(nameof(Count))); } }

    public FemAnalysesSubNode(FemSchema schema)
    {
        Schema = schema;
        Analyses = schema.Analyses;
        Count = Analyses.Count;
        Analyses.CollectionChanged += (_, __) => Count = Analyses.Count;
    }
}

/// <summary>Подузел «Усилия схемы» — наборы усилий, источник которых данная схема.</summary>
public class FemForcesSubNode : FemSubNode, System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<CScore.ForceSet> ForceSets { get; } = [];

    int _count;
    public int Count { get => _count; private set { _count = value; PropertyChanged?.Invoke(this, new(nameof(Count))); } }

    public CScore.Fem.FemSchema Schema { get; }

    public FemForcesSubNode(CScore.Fem.FemSchema schema, ObservableCollection<CScore.ForceSet> allForceSets)
    {
        Schema = schema;
        int schemaId = schema.Id;

        foreach (var fs in allForceSets.Where(f => f.SourceSchemaId == schemaId))
            ForceSets.Add(fs);
        Count = ForceSets.Count;

        allForceSets.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                ForceSets.Clear();
                Count = 0;
                return;
            }
            if (e.NewItems != null)
                foreach (CScore.ForceSet fs in e.NewItems)
                    if (fs.SourceSchemaId == schemaId) { ForceSets.Add(fs); Count = ForceSets.Count; }
            if (e.OldItems != null)
                foreach (CScore.ForceSet fs in e.OldItems)
                    if (ForceSets.Remove(fs)) Count = ForceSets.Count;
        };
    }
}
