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

/// <summary>Обёртка над FemSchema для дерева МКЭ; экспонирует 4 подузла.</summary>
class FemSchemaTreeVM
{
    public FemSchema    Schema   { get; }
    public FemSubNode[] SubNodes { get; }

    internal FemNodesSubNode    NodesSubNode    { get; }
    internal FemElementsSubNode ElementsSubNode { get; }
    internal FemForcesSubNode   ForcesSubNode   { get; }

    readonly DatabaseService _db;

    public FemSchemaTreeVM(FemSchema schema, DatabaseService db,
                           ObservableCollection<CScore.ForceSet> forceSets)
    {
        Schema = schema;
        _db    = db;

        NodesSubNode    = new FemNodesSubNode(this);
        ElementsSubNode = new FemElementsSubNode(this);
        ForcesSubNode   = new FemForcesSubNode(schema, forceSets);

        SubNodes =
        [
            NodesSubNode,
            ElementsSubNode,
            new FemMembersSubNode(schema, schema.MemberGroups),
            ForcesSubNode,
        ];

        RefreshCounts();
    }

    void RefreshCounts()
    {
        var (nodes, bars, shells) = _db.GetFemTopologyCounts(Schema.Id);
        NodesSubNode.Count             = nodes;
        ElementsSubNode.BarCount       = bars;
        ElementsSubNode.ShellCount     = shells;
        ElementsSubNode.Bars.Count     = bars;
        ElementsSubNode.Shells.Count   = shells;
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

    /// <summary>Перечитывает счётчики после SaveFemTopology.</summary>
    public void ReloadTopology() => RefreshCounts();
}

/// <summary>Базовый класс подузла расчётной схемы.</summary>
public abstract class FemSubNode { }

/// <summary>Подузел «Узлы» — листовой, данные в DataGrid загружаются асинхронно.</summary>
public class FemNodesSubNode : FemSubNode, System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    int _count;
    public int Count { get => _count; internal set { _count = value; PropertyChanged?.Invoke(this, new(nameof(Count))); } }
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

/// <summary>Подузел «Стержни» — листовой, данные в DataGrid загружаются асинхронно.</summary>
public class FemBarsSubNode : FemSubNode, System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    int _count;
    public int Count { get => _count; internal set { _count = value; PropertyChanged?.Invoke(this, new(nameof(Count))); } }
    internal FemSchemaTreeVM Owner { get; }
    internal FemBarsSubNode(FemSchemaTreeVM owner) => Owner = owner;
}

/// <summary>Подузел «Пластины» — листовой, данные в DataGrid загружаются асинхронно.</summary>
public class FemShellsSubNode : FemSubNode, System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    int _count;
    public int Count { get => _count; internal set { _count = value; PropertyChanged?.Invoke(this, new(nameof(Count))); } }
    internal FemSchemaTreeVM Owner { get; }
    internal FemShellsSubNode(FemSchemaTreeVM owner) => Owner = owner;
}

/// <summary>Подузел «Группы конструктивных элементов» — содержит FemMemberGroup'ы схемы.</summary>
public class FemMembersSubNode : FemSubNode
{
    public FemSchema                             Schema  { get; }
    public ObservableCollection<FemMemberGroup>  Members { get; }
    public FemMembersSubNode(FemSchema schema, ObservableCollection<FemMemberGroup> members)
    {
        Schema  = schema;
        Members = members;
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
