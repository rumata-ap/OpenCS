using System.Collections.ObjectModel;
using System.Windows.Data;
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

    public ObservableCollection<FemNode>        Nodes        { get; } = [];
    public ObservableCollection<FemMember>      Members      { get; } = [];
    public ObservableCollection<FemMemberGroup> MemberGroups { get; } = [];
    public ObservableCollection<FemLoadCase>    LoadCases    { get; } = [];

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
        Session.Members.AddRange(_db.GetFemMembers(schema.Id));
        Session.MemberGroups.AddRange(schema.MemberGroups);
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
        Session.Execute(new AddNodeCommand(new FemNode { SchemaId = Session.Schema.Id, NodeTag = tag, X = x, Y = y, Z = z }));
        RefreshCollections();
    }

    public void CreateBarBetween(string nodeTagA, string nodeTagB)
    {
        // NodeIdsJson хранит NodeTag узлов как числа (соглашение всей кодовой базы —
        // см. Fem3DVM.GetBarPoints/nodeMap), а не БД-Id: для только что созданных узлов
        // Id ещё не назначен (=0) до сохранения, тогда как NodeTag стабилен с момента создания.
        var tag = FemTopologyValidator.NextElemTag(Session.Members);
        var json = System.Text.Json.JsonSerializer.Serialize(new[] { int.Parse(nodeTagA), int.Parse(nodeTagB) });
        Session.Execute(new AddMemberCommand(new FemMember
        {
            SchemaId = Session.Schema.Id, ElemTag = tag, ElemType = "beam", NodeIdsJson = json
        }));
        RefreshCollections();
    }

    /// <summary>Группирует выбранные конструктивные элементы в новую группу (FemMemberGroup).</summary>
    public void CreateMemberGroupFromElements(IEnumerable<FemMember> members)
    {
        var memberTags = members.Select(e => int.Parse(e.ElemTag)).ToArray();
        if (memberTags.Length == 0) return;
        var tag = $"M{MemberGroups.Count + 1}";
        var json = System.Text.Json.JsonSerializer.Serialize(memberTags);
        Session.Execute(new CreateMemberGroupCommand(new FemMemberGroup { SchemaId = Session.Schema.Id, Tag = tag, MemberTagsJson = json }));
        RefreshCollections();
    }

    public void AddLoadCase(string tagPrefix, string sp20Type)
    {
        var tag = tagPrefix;
        int n = 2;
        while (Session.LoadCases.Any(lc => lc.Tag == tag))
            tag = $"{tagPrefix} {n++}";
        Session.Execute(new AddLoadCaseCommand(new FemLoadCase { SchemaId = Session.Schema.Id, Tag = tag, Sp20Type = sp20Type }));
        RefreshCollections();
    }

    /// <summary>
    /// Применяет компоненты нагрузки к выбранным в 3D узлам для текущего загружения. FemNodeLoad.NodeId —
    /// это БД-Id узла (FK fem_node_loads.node_id), а не NodeTag; для ещё не сохранённых узлов (Id=0)
    /// такого Id не существует, поэтому они пропускаются — возвращённый список их тегов используется
    /// вызывающей стороной, чтобы сообщить пользователю «сначала сохраните схему».
    /// </summary>
    public IReadOnlyList<string> ApplyLoadToSelection(double fx, double fy, double fz, double mx, double my, double mz)
    {
        var skippedUnsaved = new List<string>();
        if (SelectedLoadCase is not { } loadCase) return skippedUnsaved;

        foreach (var tag in Selection.SelectedNodeTags)
        {
            var node = Session.Nodes.SingleOrDefault(n => n.NodeTag == tag);
            if (node == null) continue;
            if (node.Id == 0) { skippedUnsaved.Add(tag); continue; }
            Session.Execute(new SetNodeLoadCommand(loadCase.Id, node.Id, fx, fy, fz, mx, my, mz));
        }
        RefreshCollections();
        return skippedUnsaved;
    }

    FemFragmentSnapshot? _clipboard;
    public bool HasClipboard => _clipboard != null;

    public void CopySelection()
    {
        if (Selection.SelectedNodeTags.Count == 0 && Selection.SelectedElemTags.Count == 0) return;
        _clipboard = FemFragmentClipboard.Copy(Session,
            Selection.SelectedNodeTags.ToHashSet(), Selection.SelectedElemTags.ToHashSet());
        OnPropertyChanged(nameof(HasClipboard));
    }

    public void PasteClipboard(double dx, double dy, double dz)
    {
        if (_clipboard is not { } snapshot) return;
        Session.Execute(new PasteFragmentCommand(snapshot, dx, dy, dz));
        RefreshCollections();
    }

    /// <summary>Пересинхронизирует ObservableCollection-зеркала с текущим состоянием Session
    /// после Execute/Undo/Redo. Вызывается всеми командными операциями редактора.</summary>
    public void RefreshCollections()
    {
        SyncList(Nodes, Session.Nodes);
        SyncList(Members, Session.Members);
        SyncList(MemberGroups, Session.MemberGroups);
        SyncList(LoadCases, Session.LoadCases);
        OnPropertyChanged(nameof(Session));

        // Домены (FemNode/FemMember/FemMemberGroup/FemLoadCase) не реализуют INotifyPropertyChanged
        // (CScore — чистый доменный слой без ссылок на WPF), а SyncList не пересобирает
        // ObservableCollection, если набор объектов не изменился (та же ссылка, то же поле мутировано
        // командой, например назначение сечения/GJ). Без принудительного Refresh() гриды показывают
        // устаревшие значения таких полей до следующей структурной пересборки коллекции.
        CollectionViewSource.GetDefaultView(Nodes).Refresh();
        CollectionViewSource.GetDefaultView(Members).Refresh();
        CollectionViewSource.GetDefaultView(MemberGroups).Refresh();
        CollectionViewSource.GetDefaultView(LoadCases).Refresh();
    }

    static void SyncList<T>(ObservableCollection<T> target, List<T> source)
    {
        if (target.Count == source.Count && target.SequenceEqual(source)) return;
        target.Clear();
        foreach (var item in source) target.Add(item);
    }

    /// <summary>
    /// Срабатывает, когда «Сохранить» заблокировано ошибками валидации. Полноценная вкладка
    /// диагностики появится отдельной задачей — пока подписчик (FemSchemaPage) просто показывает
    /// список ошибок, чтобы «Сохранить» не выглядело молча неработающим.
    /// </summary>
    public event Action<IReadOnlyList<FemValidationDiagnostic>>? SaveBlocked;

    void Save()
    {
        Diagnostics = FemTopologyValidator.Validate(Session.Schema, Session.Nodes, Session.Members, Session.MemberGroups)
            .Concat(FemCanonicalValidator.Validate(Session.Schema, Session.LoadCases, Session.Nodes, Session.NodeLoads))
            .ToList();
        var errors = Diagnostics.Where(d => d.IsError).ToList();
        if (errors.Count > 0)
        {
            SaveBlocked?.Invoke(errors);
            return;
        }

        _db.SaveFemSchemaEdit(Session.Schema.Id, Session.Nodes, Session.Members, Session.MemberGroups,
            Session.LoadCases, Session.NodeLoads);
        Session.MarkSaved();
        RefreshCollections();
    }
}
