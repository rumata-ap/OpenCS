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

/// <summary>Корневой узел «Проверки» в дереве МКЭ.</summary>
class FemChecksGroupNode
{
    public ObservableCollection<FemCheck> Checks { get; }
    public FemChecksGroupNode(ObservableCollection<FemCheck> checks) => Checks = checks;
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
            new FemMembersSubNode(schema, schema.Members),
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

    /// <summary>Асинхронно загружает стержневые КЭ схемы из БД.</summary>
    internal Task<List<FemElement>> LoadBarsAsync()
        => Task.Run(() => _db.GetFemElements(Schema.Id)
            .Where(e => e.ElemType == "beam").ToList());

    /// <summary>Асинхронно загружает пластинчатые КЭ схемы из БД.</summary>
    internal Task<List<FemElement>> LoadShellsAsync()
        => Task.Run(() => _db.GetFemElements(Schema.Id)
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

/// <summary>Подузел «Конструктивные элементы» — содержит FemMember'ы схемы.</summary>
public class FemMembersSubNode : FemSubNode
{
    public FemSchema                        Schema  { get; }
    public ObservableCollection<FemMember>  Members { get; }
    public FemMembersSubNode(FemSchema schema, ObservableCollection<FemMember> members)
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
