using System.Collections.ObjectModel;
using System.Windows.Input;
using CScore;
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

    /// <summary>Пул проектных сечений — источник для назначения FemMember.CrossSectionId.</summary>
    public ObservableCollection<CrossSection> CrossSections { get; }
    /// <summary>Все расчётные задачи проекта — отсюда фильтруются задачи кручения для GJ = Saint-Venant.</summary>
    public ObservableCollection<CalcTask> AllCalcTasks { get; }

    bool _createNodeMode, _createBarMode;
    public bool CreateNodeMode { get => _createNodeMode; set { _createNodeMode = value; if (value) CreateBarMode = false; OnPropertyChanged(); } }
    public bool CreateBarMode  { get => _createBarMode;  set { _createBarMode  = value; if (value) CreateNodeMode = false; OnPropertyChanged(); } }

    FemMember? _selectedMember;
    public FemMember? SelectedMember
    {
        get => _selectedMember;
        set
        {
            _selectedMember = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedMemberCrossSection));
            OnPropertyChanged(nameof(SelectedMemberTorsionTasks));
            OnPropertyChanged(nameof(SelectedMemberGjIsManual));
            OnPropertyChanged(nameof(SelectedMemberGjIsSaintVenant));
            OnPropertyChanged(nameof(SelectedMemberGjManualValue));
            OnPropertyChanged(nameof(SelectedMemberTorsionTask));
        }
    }

    /// <summary>Сечение выбранного члена. Изменение проходит через SetMemberSectionCommand (undo/redo).</summary>
    public CrossSection? SelectedMemberCrossSection
    {
        get => SelectedMember == null ? null : CrossSections.FirstOrDefault(s => s.Id == SelectedMember.CrossSectionId);
        set
        {
            if (SelectedMember == null) return;
            Session.Execute(new SetMemberSectionCommand(SelectedMember, value?.Id));
            RefreshCollections();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedMemberTorsionTasks));
        }
    }

    /// <summary>Задачи кручения (torsion_bem/torsion_fem), считанные для текущего сечения члена.</summary>
    public IEnumerable<CalcTask> SelectedMemberTorsionTasks => SelectedMember == null
        ? []
        : AllCalcTasks.Where(t => t.Kind is "torsion_bem" or "torsion_fem" && t.SectionId == SelectedMember.CrossSectionId);

    public bool SelectedMemberGjIsManual
    {
        get => SelectedMember == null || SelectedMember.GjStrategy != "saint_venant";
        set
        {
            if (SelectedMember == null || !value) return;
            Session.Execute(new SetMemberGjCommand(SelectedMember, "manual", SelectedMember.GjManualValue ?? 0, null));
            RefreshCollections();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedMemberGjIsSaintVenant));
        }
    }

    public bool SelectedMemberGjIsSaintVenant
    {
        get => SelectedMember?.GjStrategy == "saint_venant";
        set
        {
            if (SelectedMember == null || !value) return;
            Session.Execute(new SetMemberGjCommand(SelectedMember, "saint_venant", null, SelectedMember.GjTorsionTaskId));
            RefreshCollections();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedMemberGjIsManual));
        }
    }

    public double SelectedMemberGjManualValue
    {
        get => SelectedMember?.GjManualValue ?? 0;
        set
        {
            if (SelectedMember == null) return;
            Session.Execute(new SetMemberGjCommand(SelectedMember, "manual", value, null));
            RefreshCollections();
            OnPropertyChanged();
        }
    }

    public CalcTask? SelectedMemberTorsionTask
    {
        get => SelectedMember == null ? null : AllCalcTasks.FirstOrDefault(t => t.Id == SelectedMember.GjTorsionTaskId);
        set
        {
            if (SelectedMember == null) return;
            Session.Execute(new SetMemberGjCommand(SelectedMember, "saint_venant", null, value?.Id));
            RefreshCollections();
            OnPropertyChanged();
        }
    }

    FemLoadCase? _selectedLoadCase;
    public FemLoadCase? SelectedLoadCase { get => _selectedLoadCase; set { _selectedLoadCase = value; OnPropertyChanged(); } }

    IReadOnlyList<FemValidationDiagnostic> _diagnostics = [];
    public IReadOnlyList<FemValidationDiagnostic> Diagnostics { get => _diagnostics; private set { _diagnostics = value; OnPropertyChanged(); } }

    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand SaveCommand { get; }

    public FemSchemaEditorVM(FemSchema schema, AppViewModel app)
    {
        _db = app.db;
        CrossSections = app.CrossSections;
        AllCalcTasks  = app.CalcTasks;
        Session = new FemSchemaEditSession(schema);
        Session.Nodes.AddRange(_db.GetFemNodes(schema.Id));
        Session.Elements.AddRange(_db.GetFemElements(schema.Id));
        Session.Members.AddRange(schema.Members);
        Session.LoadCases.AddRange(schema.LoadCases);
        Session.NodeLoads.AddRange(_db.GetFemNodeLoads(schema.Id));
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

    /// <summary>Группирует выбранные стержни в новый конструктивный элемент (FemMember).</summary>
    public void CreateMemberFromElements(IEnumerable<FemElement> elements)
    {
        var elemTags = elements.Select(e => int.Parse(e.ElemTag)).ToArray();
        if (elemTags.Length == 0) return;
        var tag = $"M{Members.Count + 1}";
        var json = System.Text.Json.JsonSerializer.Serialize(elemTags);
        Session.Execute(new CreateMemberCommand(new FemMember { Tag = tag, ElemIdsJson = json }));
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
