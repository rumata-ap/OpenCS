using System.Collections.ObjectModel;
using System.Windows.Input;
using CScore.Fem;
using CScore.Fem.Editing;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>ViewModel редактора FEM-схемы: держит сессию, выбор, режимы создания и сохранение.
/// Nodes/Elements/Members/LoadCases — ObservableCollection-зеркала Session.* (та же ссылка на
/// доменные объекты), пересинхронизируемые после каждой команды, чтобы гриды видели изменения.</summary>
public sealed class FemSchemaEditorVM : ViewModelBase
{
    readonly DatabaseService _db;

    public FemSchemaEditSession Session   { get; }
    public FemSchemaSelectionVM Selection { get; } = new();

    public ObservableCollection<FemNode>     Nodes     { get; } = [];
    public ObservableCollection<FemElement>  Elements  { get; } = [];
    public ObservableCollection<FemMember>   Members   { get; } = [];
    public ObservableCollection<FemLoadCase> LoadCases { get; } = [];

    bool _createNodeMode, _createBarMode;
    public bool CreateNodeMode { get => _createNodeMode; set { _createNodeMode = value; if (value) CreateBarMode = false; OnPropertyChanged(); } }
    public bool CreateBarMode  { get => _createBarMode;  set { _createBarMode  = value; if (value) CreateNodeMode = false; OnPropertyChanged(); } }

    FemMember? _selectedMember;
    public FemMember? SelectedMember { get => _selectedMember; set { _selectedMember = value; OnPropertyChanged(); } }

    FemLoadCase? _selectedLoadCase;
    public FemLoadCase? SelectedLoadCase { get => _selectedLoadCase; set { _selectedLoadCase = value; OnPropertyChanged(); } }

    IReadOnlyList<FemValidationDiagnostic> _diagnostics = [];
    public IReadOnlyList<FemValidationDiagnostic> Diagnostics { get => _diagnostics; private set { _diagnostics = value; OnPropertyChanged(); } }

    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand SaveCommand { get; }

    public FemSchemaEditorVM(FemSchema schema, DatabaseService db)
    {
        _db = db;
        Session = new FemSchemaEditSession(schema);
        Session.Nodes.AddRange(db.GetFemNodes(schema.Id));
        Session.Elements.AddRange(db.GetFemElements(schema.Id));
        Session.Members.AddRange(schema.Members);
        Session.LoadCases.AddRange(schema.LoadCases);
        Session.NodeLoads.AddRange(db.GetFemNodeLoads(schema.Id));
        RefreshCollections();

        UndoCommand = new RelayCommand(_ => { Session.Undo(); RefreshCollections(); }, _ => Session.CanUndo);
        RedoCommand = new RelayCommand(_ => { Session.Redo(); RefreshCollections(); }, _ => Session.CanRedo);
        SaveCommand = new RelayCommand(_ => Save(), _ => Session.IsDirty);
    }

    public void CreateNodeAt(double x, double y, double z)
    {
        var tag = FemTopologyValidator.NextNodeTag(Session.Nodes);
        Session.Execute(new AddNodeCommand(new FemNode { NodeTag = tag, X = x, Y = y, Z = z }));
        RefreshCollections();
    }

    public void CreateBarBetween(string nodeTagA, string nodeTagB)
    {
        // NodeIdsJson хранит NodeTag узлов как числа (соглашение всей кодовой базы —
        // см. Fem3DVM.GetBarPoints/nodeMap), а не БД-Id: для только что созданных узлов
        // Id ещё не назначен (=0) до сохранения, тогда как NodeTag стабилен с момента создания.
        var tag = FemTopologyValidator.NextElemTag(Session.Elements);
        var json = System.Text.Json.JsonSerializer.Serialize(new[] { int.Parse(nodeTagA), int.Parse(nodeTagB) });
        Session.Execute(new AddElementCommand(new FemElement { ElemTag = tag, ElemType = "beam", NodeIdsJson = json }));
        RefreshCollections();
    }

    /// <summary>Пересинхронизирует ObservableCollection-зеркала с текущим состоянием Session
    /// после Execute/Undo/Redo. Вызывается всеми командными операциями редактора.</summary>
    public void RefreshCollections()
    {
        SyncList(Nodes, Session.Nodes);
        SyncList(Elements, Session.Elements);
        SyncList(Members, Session.Members);
        SyncList(LoadCases, Session.LoadCases);
        OnPropertyChanged(nameof(Session));
    }

    static void SyncList<T>(ObservableCollection<T> target, List<T> source)
    {
        if (target.Count == source.Count && target.SequenceEqual(source)) return;
        target.Clear();
        foreach (var item in source) target.Add(item);
    }

    void Save()
    {
        Diagnostics = FemTopologyValidator.Validate(Session.Schema, Session.Nodes, Session.Elements, Session.Members)
            .Concat(FemCanonicalValidator.Validate(Session.Schema, Session.LoadCases, Session.Nodes, Session.NodeLoads))
            .ToList();
        if (Diagnostics.Any(d => d.IsError)) return;

        _db.SaveFemSchemaEdit(Session.Schema.Id, Session.Nodes, Session.Elements, Session.Members,
            Session.LoadCases, Session.NodeLoads);
        Session.MarkSaved();
        RefreshCollections();
    }
}
