using System.Collections.ObjectModel;
using System.Windows.Data;
using System.Windows.Input;
using CScore;
using CScore.Fem;
using CScore.Fem.Editing;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Строка состава выбранного определения нагрузки для редактора.</summary>
public sealed record FemLoadDefinitionTermView(int LoadCaseId, string LoadCaseTag, double Coefficient);

/// <summary>ViewModel редактора FEM-схемы: держит сессию, выбор, режимы создания и сохранение.
/// Nodes/Elements/Members/LoadCases — ObservableCollection-зеркала Session.* (та же ссылка на
/// доменные объекты), пересинхронизируемые после каждой команды, чтобы гриды видели изменения.</summary>
public sealed class FemSchemaEditorVM : ViewModelBase
{
    readonly DatabaseService _db;
    readonly ILogService _logService;

    public FemSchemaEditSession Session   { get; }
    public FemSchemaSelectionVM Selection { get; } = new();

    public ObservableCollection<FemNode>        Nodes        { get; } = [];
    public ObservableCollection<FemMember>      Members      { get; } = [];
    /// <summary>Конструктивные стержни, доступные для задания распределённых нагрузок.</summary>
    public IEnumerable<FemMember> BeamMembers => Members.Where(member => member.ElemType == "beam");
    public ObservableCollection<FemMemberGroup> MemberGroups { get; } = [];
    public ObservableCollection<FemLoadCase>    LoadCases    { get; } = [];
    public ObservableCollection<FemMemberLoad>  MemberLoads  { get; } = [];
    public ObservableCollection<FemLoadDefinition> LoadDefinitions { get; } = [];

    /// <summary>Пул проектных сечений — источник для назначения FemMember.CrossSectionId.</summary>
    public ObservableCollection<CrossSection> CrossSections { get; }
    /// <summary>Все расчётные задачи проекта — отсюда фильтруются задачи кручения для GJ = Saint-Venant.</summary>
    public ObservableCollection<CalcTask> AllCalcTasks { get; }

    bool _createNodeMode, _createBarMode;
    bool _isDiscretizing;
    public bool CreateNodeMode { get => _createNodeMode; set { _createNodeMode = value; if (value) CreateBarMode = false; OnPropertyChanged(); } }
    public bool CreateBarMode  { get => _createBarMode;  set { _createBarMode  = value; if (value) CreateNodeMode = false; OnPropertyChanged(); } }
    public bool IsDiscretizing
    {
        get => _isDiscretizing;
        private set
        {
            _isDiscretizing = value;
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }

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
            OnPropertyChanged(nameof(SelectedMemberRotationDeg));
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
        get => FemUnitConverter.NewtonMetersSquaredToKiloNewtonMetersSquared(SelectedMember?.GjManualValue ?? 0);
        set
        {
            if (SelectedMember == null) return;
            var valueInternal = FemUnitConverter.KiloNewtonMetersSquaredToNewtonMetersSquared(value);
            Session.Execute(new SetMemberGjCommand(SelectedMember, "manual", valueInternal, null));
            RefreshCollections();
            OnPropertyChanged();
        }
    }

    /// <summary>Угол поворота локальных осей выбранного стержня (β-угол), градусы.
    /// Изменение проходит через SetMemberRotationCommand (undo/redo).</summary>
    public double SelectedMemberRotationDeg
    {
        get => SelectedMember?.RotationDeg ?? 0;
        set
        {
            if (SelectedMember == null) return;
            Session.Execute(new SetMemberRotationCommand(SelectedMember, value));
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
    public FemLoadCase? SelectedLoadCase
    {
        get => _selectedLoadCase;
        set { _selectedLoadCase = value; OnPropertyChanged(); }
    }
    FemMember? _selectedLoadMember;
    /// <summary>Стержень, выбранный в редакторе распределённой нагрузки.</summary>
    public FemMember? SelectedLoadMember
    {
        get => _selectedLoadMember;
        set { _selectedLoadMember = value; OnPropertyChanged(); }
    }
    FemLoadDefinition? _selectedLoadDefinition;
    public FemLoadDefinition? SelectedLoadDefinition
    {
        get => _selectedLoadDefinition;
        set
        {
            _selectedLoadDefinition = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedLoadDefinitionTerms));
        }
    }
    FemLoadDefinitionTermView? _selectedLoadDefinitionTerm;
    public FemLoadDefinitionTermView? SelectedLoadDefinitionTerm { get => _selectedLoadDefinitionTerm; set { _selectedLoadDefinitionTerm = value; OnPropertyChanged(); } }
    public IReadOnlyList<FemLoadDefinitionTermView> SelectedLoadDefinitionTerms =>
        SelectedLoadDefinition?.GetExpression().Terms
            .Select(term => new FemLoadDefinitionTermView(
                term.LoadCaseId,
                Session.LoadCases.FirstOrDefault(loadCase => loadCase.Id == term.LoadCaseId)?.Tag ?? term.LoadCaseId.ToString(),
                term.Coefficient))
            .ToArray() ?? [];

    /// <summary>Возвращает нагрузку выбранного загружения на заданный конструктивный стержень.</summary>
    public FemMemberLoad? FindMemberLoad(FemMember member) => SelectedLoadCase is { } loadCase
        ? Session.MemberLoads.FirstOrDefault(load => load.LoadCaseId == loadCase.Id && load.MemberId == member.Id)
        : null;

    /// <summary>Удаляет нагрузку заданного загружения с заданного стержня (для 3D-глифов,
    /// где отображаемое загружение может отличаться от выбранного в панели свойств).</summary>
    public bool DeleteMemberLoad(FemMember member, FemLoadCase loadCase)
    {
        var load = Session.MemberLoads.FirstOrDefault(item => item.LoadCaseId == loadCase.Id && item.MemberId == member.Id);
        if (load == null) return false;
        Session.Execute(new DeleteMemberLoadCommand(load));
        RefreshCollections();
        return true;
    }

    /// <summary>Удаляет узловую нагрузку заданного загружения с заданного узла (для 3D-глифов).</summary>
    public bool DeleteNodeLoad(FemNode node, FemLoadCase loadCase)
    {
        var load = Session.NodeLoads.FirstOrDefault(item => item.LoadCaseId == loadCase.Id && item.NodeId == node.Id);
        if (load == null) return false;
        Session.Execute(new DeleteNodeLoadCommand(load));
        RefreshCollections();
        return true;
    }

    /// <summary>Создаёт или обновляет распределённую или сосредоточенную нагрузку конструктивного стержня.</summary>
    public bool ApplyMemberLoad(
        double startOffsetM, double endOffsetM, string coordinateSystem, string distributionType,
        double qxStart, double qyStart, double qzStart, double qxEnd, double qyEnd, double qzEnd,
        double mx = 0, double my = 0, double mz = 0)
    {
        if (SelectedLoadCase is not { } loadCase || SelectedLoadMember is not { } member || member.Id == 0)
            return false;

        var existing = FindMemberLoad(member);
        Session.Execute(new SetMemberLoadCommand(new FemMemberLoad
        {
            Id = existing?.Id ?? 0,
            SchemaId = Session.Schema.Id,
            LoadCaseId = loadCase.Id,
            MemberId = member.Id,
            CoordinateSystem = coordinateSystem,
            DistributionType = distributionType,
            StartOffsetM = startOffsetM,
            EndOffsetM = endOffsetM,
            QxStart = qxStart, QyStart = qyStart, QzStart = qzStart,
            QxEnd = qxEnd, QyEnd = qyEnd, QzEnd = qzEnd,
            Mx = mx, My = my, Mz = mz
        }));
        RefreshCollections();
        return true;
    }

    /// <summary>Удаляет нагрузку выбранного загружения с выбранного стержня.</summary>
    public void DeleteMemberLoad()
    {
        if (SelectedLoadMember is not { } member || FindMemberLoad(member) is not { } load) return;
        Session.Execute(new DeleteMemberLoadCommand(load));
        RefreshCollections();
    }

    IReadOnlyList<FemValidationDiagnostic> _diagnostics = [];
    public IReadOnlyList<FemValidationDiagnostic> Diagnostics { get => _diagnostics; private set { _diagnostics = value; OnPropertyChanged(); } }

    double? _defaultTargetMeshLengthM;
    public double? DefaultTargetMeshLengthM
    {
        get => _defaultTargetMeshLengthM;
        set { _defaultTargetMeshLengthM = value; OnPropertyChanged(); }
    }

    IReadOnlyList<FemValidationDiagnostic> _lastMeshDiagnostics = [];
    public IReadOnlyList<FemValidationDiagnostic> LastMeshDiagnostics
    {
        get => _lastMeshDiagnostics;
        private set { _lastMeshDiagnostics = value; OnPropertyChanged(); }
    }

    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DiscretizeCommand { get; }
    public ICommand MergeNodesCommand { get; }

    public FemSchemaEditorVM(FemSchema schema, AppViewModel app)
    {
        _db = app.db;
        _logService = app.LogService;
        CrossSections = app.CrossSections;
        AllCalcTasks  = app.CalcTasks;
        Session = new FemSchemaEditSession(schema);
        Session.Nodes.AddRange(_db.GetFemNodes(schema.Id));
        Session.Members.AddRange(_db.GetFemMembers(schema.Id));
        Session.MemberGroups.AddRange(schema.MemberGroups);
        Session.LoadCases.AddRange(schema.LoadCases);
        Session.NodeLoads.AddRange(_db.GetFemNodeLoads(schema.Id));
        Session.MemberLoads.AddRange(_db.GetFemMemberLoads(schema.Id));
        Session.LoadDefinitions.AddRange(schema.LoadDefinitions);
        RefreshCollections();

        UndoCommand = new RelayCommand(_ => { Session.Undo(); RefreshCollections(); }, _ => Session.CanUndo);
        RedoCommand = new RelayCommand(_ => { Session.Redo(); RefreshCollections(); }, _ => Session.CanRedo);
        SaveCommand = new RelayCommand(_ => Save(), _ => Session.IsDirty);
        DiscretizeCommand = new RelayCommand(_ => Discretize(), _ => !IsDiscretizing);
        MergeNodesCommand = new RelayCommand(_ => _logService.Info(MergeCoincidentNodes()));
    }

    public void CreateNodeAt(double x, double y, double z)
    {
        var tag = FemTopologyValidator.NextNodeTag(Session.Nodes);
        Session.Execute(new AddNodeCommand(new FemNode { SchemaId = Session.Schema.Id, NodeTag = tag, X = x, Y = y, Z = z }));
        RefreshCollections();
    }

    public void CreateBarBetween(string nodeTagA, string nodeTagB, string? sectionTag = null)
    {
        var tag = FemTopologyValidator.NextElemTag(Session.Members);
        var json = System.Text.Json.JsonSerializer.Serialize(new[] { int.Parse(nodeTagA), int.Parse(nodeTagB) });
        int? sectionId = null;
        if (sectionTag != null)
        {
            var cs = CrossSections.FirstOrDefault(s => s.Tag == sectionTag);
            if (cs != null) sectionId = cs.Id;
        }
        Session.Execute(new AddMemberCommand(new FemMember
        {
            SchemaId = Session.Schema.Id, ElemTag = tag, ElemType = "beam", NodeIdsJson = json,
            CrossSectionId = sectionId
        }));
        RefreshCollections();
    }

    public void DeleteMemberByTag(string elemTag)
    {
        var member = Session.Members.FirstOrDefault(m => m.ElemTag == elemTag);
        if (member == null) return;
        Session.Execute(new DeleteMemberCommand(member));
        Selection.ToggleElement(elemTag, additive: false);
        RefreshCollections();
    }

    public void SplitMemberByTag(string elemTag)
    {
        var member = Session.Members.FirstOrDefault(m => m.ElemTag == elemTag);
        if (member == null) return;
        var ids = System.Text.Json.JsonSerializer.Deserialize<int[]>(member.NodeIdsJson) ?? [];
        if (ids.Length != 2) return;
        var n1 = Session.Nodes.FirstOrDefault(n => n.NodeTag == ids[0].ToString());
        var n2 = Session.Nodes.FirstOrDefault(n => n.NodeTag == ids[1].ToString());
        if (n1 == null || n2 == null) return;

        var midTag = FemTopologyValidator.NextNodeTag(Session.Nodes);
        var midNode = new FemNode
        {
            SchemaId = Session.Schema.Id, NodeTag = midTag,
            X = (n1.X + n2.X) / 2, Y = (n1.Y + n2.Y) / 2, Z = (n1.Z + n2.Z) / 2,
        };
        Session.Execute(new AddNodeCommand(midNode));

        Session.Execute(new DeleteMemberCommand(member));

        var tag1 = FemTopologyValidator.NextElemTag(Session.Members);
        Session.Execute(new AddMemberCommand(new FemMember
        {
            SchemaId = Session.Schema.Id, ElemTag = tag1, ElemType = "beam",
            NodeIdsJson = System.Text.Json.JsonSerializer.Serialize(new[] { ids[0], int.Parse(midTag) }),
            CrossSectionId = member.CrossSectionId, GjStrategy = member.GjStrategy,
            GjManualValue = member.GjManualValue, GjTorsionTaskId = member.GjTorsionTaskId,
        }));
        var tag2 = FemTopologyValidator.NextElemTag(Session.Members);
        Session.Execute(new AddMemberCommand(new FemMember
        {
            SchemaId = Session.Schema.Id, ElemTag = tag2, ElemType = "beam",
            NodeIdsJson = System.Text.Json.JsonSerializer.Serialize(new[] { int.Parse(midTag), ids[1] }),
            CrossSectionId = member.CrossSectionId, GjStrategy = member.GjStrategy,
            GjManualValue = member.GjManualValue, GjTorsionTaskId = member.GjTorsionTaskId,
        }));

        RefreshCollections();
    }

    public void MoveNodeByTag(string nodeTag, double dx, double dy, double dz)
    {
        var node = Session.Nodes.FirstOrDefault(n => n.NodeTag == nodeTag);
        if (node == null) return;
        Session.Execute(new MoveNodeCommand(node, node.X + dx, node.Y + dy, node.Z + dz));
        RefreshCollections();
    }

    public void CopyNodeByTag(string nodeTag, double dx, double dy, double dz)
    {
        var node = Session.Nodes.FirstOrDefault(n => n.NodeTag == nodeTag);
        if (node == null) return;
        var newTag = FemTopologyValidator.NextNodeTag(Session.Nodes);
        Session.Execute(new AddNodeCommand(new FemNode
        {
            SchemaId = Session.Schema.Id, NodeTag = newTag,
            X = node.X + dx, Y = node.Y + dy, Z = node.Z + dz, DofMask = node.DofMask
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

    /// <summary>Обновляет имя и параметры комбинаторики СП 20 выбранного исходного загружения.</summary>
    public bool UpdateSelectedLoadCase(
        string tag, string sp20Type, string? sp20Group,
        double? gammaFUnfav, double? gammaFFav, double? psi1, double? psi2)
    {
        if (SelectedLoadCase is not { } loadCase || !CanUseLoadCaseTag(tag, loadCase)) return false;
        Session.Execute(new EditLoadCaseCommand(loadCase, new FemLoadCase
        {
            Tag = tag.Trim(),
            LoadType = loadCase.LoadType,
            Sp20Type = sp20Type,
            Sp20Group = string.IsNullOrWhiteSpace(sp20Group) ? null : sp20Group,
            GammaFUnfav = gammaFUnfav,
            GammaFFav = gammaFFav,
            Psi1 = psi1,
            Psi2 = psi2
        }));
        RefreshCollections();
        return true;
    }

    /// <summary>Переименовывает выбранное исходное загружение с проверкой уникальности.</summary>
    public bool TryRenameSelectedLoadCase(string tag)
    {
        if (SelectedLoadCase is not { } loadCase || !CanUseLoadCaseTag(tag, loadCase)) return false;
        Session.Execute(new EditLoadCaseCommand(loadCase, new FemLoadCase
        {
            Tag = tag.Trim(), LoadType = loadCase.LoadType, Sp20Type = loadCase.Sp20Type,
            Sp20Group = loadCase.Sp20Group, GammaFUnfav = loadCase.GammaFUnfav,
            GammaFFav = loadCase.GammaFFav, Psi1 = loadCase.Psi1, Psi2 = loadCase.Psi2
        }));
        RefreshCollections();
        return true;
    }

    /// <summary>Переименовывает выбранную комбинацию с проверкой уникальности.</summary>
    public bool TryRenameSelectedLoadDefinition(string tag)
    {
        if (SelectedLoadDefinition is not { } definition || !CanUseDefinitionTag(tag, definition)) return false;
        Session.Execute(new EditLoadDefinitionCommand(definition, new FemLoadDefinition
        {
            Tag = tag.Trim(), Description = definition.Description, ExpressionJson = definition.ExpressionJson,
            SourceKind = definition.SourceKind, CombinationType = definition.CombinationType
        }));
        RefreshCollections();
        return true;
    }

    bool CanUseLoadCaseTag(string tag, FemLoadCase target) =>
        !string.IsNullOrWhiteSpace(tag) &&
        Session.LoadCases.All(loadCase => ReferenceEquals(loadCase, target) ||
            !string.Equals(loadCase.Tag, tag.Trim(), StringComparison.Ordinal));

    bool CanUseDefinitionTag(string tag, FemLoadDefinition target) =>
        !string.IsNullOrWhiteSpace(tag) &&
        Session.LoadDefinitions.All(definition => ReferenceEquals(definition, target) ||
            !string.Equals(definition.Tag, tag.Trim(), StringComparison.Ordinal));

    /// <summary>Создаёт ручное определение, начав его текущим выбранным загружением.</summary>
    public void AddManualLoadDefinition(string tagPrefix)
    {
        var tag = tagPrefix;
        int index = 2;
        while (Session.LoadDefinitions.Any(definition => definition.Tag == tag))
            tag = $"{tagPrefix} {index++}";

        var definition = new FemLoadDefinition
        {
            SchemaId = Session.Schema.Id,
            Tag = tag,
            SourceKind = "manual"
        };
        definition.SetExpression(new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sum,
            Terms = SelectedLoadCase == null ? [] :
                [new FemLoadTerm { LoadCaseId = SelectedLoadCase.Id, Coefficient = 1 }]
        });
        Session.Execute(new AddLoadDefinitionCommand(definition));
        SelectedLoadDefinition = definition;
        RefreshCollections();
    }

    /// <summary>Добавляет текущее исходное загружение в выбранное ручное определение.</summary>
    public void AddSelectedLoadCaseToDefinition()
    {
        if (SelectedLoadDefinition is not { } definition || SelectedLoadCase is not { } loadCase) return;
        var expression = definition.GetExpression();
        var terms = expression.Terms.ToList();
        terms.Add(new FemLoadTerm { LoadCaseId = loadCase.Id, Coefficient = 1 });
        SetDefinitionExpression(definition, new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sum,
            LoadCaseIds = expression.LoadCaseIds,
            Terms = terms,
            CombinationType = expression.CombinationType
        });
    }

    /// <summary>Удаляет выбранное определение нагрузки.</summary>
    public void DeleteSelectedLoadDefinition()
    {
        if (SelectedLoadDefinition is not { } definition) return;
        Session.Execute(new DeleteLoadDefinitionCommand(definition));
        SelectedLoadDefinition = null;
        RefreshCollections();
    }

    /// <summary>Удаляет выбранное слагаемое из ручного определения.</summary>
    public void DeleteSelectedLoadDefinitionTerm()
    {
        if (SelectedLoadDefinition is not { } definition || SelectedLoadDefinitionTerm is not { } term) return;
        var expression = definition.GetExpression();
        var terms = expression.Terms.ToList();
        var index = terms.FindIndex(item => item.LoadCaseId == term.LoadCaseId && item.Coefficient == term.Coefficient);
        if (index < 0) return;
        terms.RemoveAt(index);
        SetDefinitionExpression(definition, new FemLoadExpression
        {
            Mode = terms.Count == 1 ? FemLoadExpressionMode.Single : FemLoadExpressionMode.Sum,
            LoadCaseIds = expression.LoadCaseIds,
            Terms = terms,
            CombinationType = expression.CombinationType
        });
        SelectedLoadDefinitionTerm = null;
    }

    /// <summary>Изменяет коэффициент выбранного слагаемого ручного определения.</summary>
    public void UpdateSelectedLoadDefinitionTermCoefficient(double coefficient)
    {
        if (SelectedLoadDefinition is not { } definition || SelectedLoadDefinitionTerm is not { } selected) return;
        var expression = definition.GetExpression();
        var terms = expression.Terms.ToList();
        var index = terms.FindIndex(item => item.LoadCaseId == selected.LoadCaseId && item.Coefficient == selected.Coefficient);
        if (index < 0) return;
        terms[index] = new FemLoadTerm { LoadCaseId = selected.LoadCaseId, Coefficient = coefficient };
        SetDefinitionExpression(definition, new FemLoadExpression
        {
            Mode = terms.Count == 1 ? FemLoadExpressionMode.Single : FemLoadExpressionMode.Sum,
            LoadCaseIds = expression.LoadCaseIds,
            Terms = terms,
            CombinationType = expression.CombinationType
        });
    }

    /// <summary>Создаёт материализованные сочетания СП 20 для всех исходных загружений с параметрами из их карточек.</summary>
    public void GenerateSp20LoadDefinitions(string combinationType)
    {
        var definitions = FemLoadDefinitionFactory.CreateSp20(
            Session.Schema, Session.LoadCases, Session.Nodes, Session.NodeLoads, combinationType, Session.MemberLoads);
        foreach (var definition in definitions)
        {
            var baseTag = definition.Tag;
            int index = 2;
            while (Session.LoadDefinitions.Any(existing => existing.Tag == definition.Tag))
                definition.Tag = $"{baseTag} {index++}";
            Session.Execute(new AddLoadDefinitionCommand(definition));
        }
        RefreshCollections();
    }

    void SetDefinitionExpression(FemLoadDefinition definition, FemLoadExpression expression)
    {
        var values = new FemLoadDefinition
        {
            Tag = definition.Tag,
            Description = definition.Description,
            SourceKind = definition.SourceKind,
            CombinationType = definition.CombinationType,
            ExpressionJson = expression.ToJson()
        };
        Session.Execute(new EditLoadDefinitionCommand(definition, values));
        RefreshCollections();
    }

    /// <summary>
    /// Применяет компоненты нагрузки к выбранным в 3D узлам для текущего загружения.
    /// </summary>
    public IReadOnlyList<string> ApplyLoadToSelection(double fx, double fy, double fz, double mx, double my, double mz)
    {
        var skipped = new List<string>();
        if (SelectedLoadCase is not { } loadCase) return skipped;

        foreach (var tag in Selection.SelectedNodeTags)
        {
            var node = Session.Nodes.SingleOrDefault(n => n.NodeTag == tag);
            if (node == null) continue;
            Session.Execute(new SetNodeLoadCommand(loadCase.Id, node.Id, fx, fy, fz, mx, my, mz));
        }
        RefreshCollections();
        NodeLoadsApplied?.Invoke(loadCase);
        return skipped;
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
        SyncList(MemberLoads, Session.MemberLoads);
        SyncList(LoadDefinitions, Session.LoadDefinitions);
        OnPropertyChanged(nameof(Session));

        // Домены (FemNode/FemMember/FemMemberGroup/FemLoadCase) не реализуют INotifyPropertyChanged
        // (CScore — чистый доменный слой без ссылок на WPF), а SyncList не пересобирает
        // ObservableCollection, если набор объектов не изменился (та же ссылка, то же поле мутировано
        // командой, например назначение сечения/GJ). Без принудительного Refresh() гриды показывают
        // устаревшие значения таких полей до следующей структурной пересборки коллекции.
        CollectionViewSource.GetDefaultView(Nodes).Refresh();
        CollectionViewSource.GetDefaultView(Members).Refresh();
        OnPropertyChanged(nameof(BeamMembers));
        CollectionViewSource.GetDefaultView(MemberGroups).Refresh();
        CollectionViewSource.GetDefaultView(LoadCases).Refresh();
        CollectionViewSource.GetDefaultView(MemberLoads).Refresh();
        CollectionViewSource.GetDefaultView(LoadDefinitions).Refresh();
        OnPropertyChanged(nameof(SelectedLoadDefinitionTerms));
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
    public event EventHandler? MeshDiscretized;
    public event Action<FemLoadCase>? NodeLoadsApplied;

    /// <summary>Сшивает совпадающие по координатам узлы конструктивного слоя. Возвращает короткий
    /// отчёт для лога.</summary>
    public string MergeCoincidentNodes()
    {
        var command = new MergeCoincidentNodesCommand();
        Session.Execute(command);
        RefreshCollections();
        if (command.LastResult.Count == 0)
            return Loc.S("FemMergeNodesNone");
        int totalMerged = command.LastResult.Sum(group => group.MergedTags.Count);
        return string.Format(Loc.S("FemMergeNodesDone"), totalMerged, command.LastResult.Count);
    }

    public void Discretize()
    {
        if (IsDiscretizing) return;
        IsDiscretizing = true;
        try
        {
            var mesh = FemMeshDiscretizer.Discretize(
                Session.Schema.Id, Session.Nodes, Session.Members, DefaultTargetMeshLengthM);
            LastMeshDiagnostics = FemTopologyValidator.ValidateMesh(mesh.Nodes, mesh.Elements);
            if (LastMeshDiagnostics.Any(diagnostic => diagnostic.IsError)) return;

            _db.SaveFemMeshSnapshot(Session.Schema.Id, mesh.Nodes, mesh.Elements);
            MeshDiscretized?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsDiscretizing = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool Save()
    {
        Diagnostics = FemTopologyValidator.Validate(Session.Schema, Session.Nodes, Session.Members, Session.MemberGroups)
            .Concat(FemCanonicalValidator.Validate(Session.Schema, Session.LoadCases, Session.Nodes,
                Session.NodeLoads, Session.Members, Session.MemberLoads))
            .Concat(FemLoadDefinitionValidator.Validate(Session.Schema, Session.LoadDefinitions, Session.LoadCases))
            .ToList();
        var errors = Diagnostics.Where(d => d.IsError).ToList();
        if (errors.Count > 0)
        {
            SaveBlocked?.Invoke(errors);
            return false;
        }

        _db.SaveFemSchemaEdit(Session.Schema.Id, Session.Nodes, Session.Members, Session.MemberGroups,
            Session.LoadCases, Session.NodeLoads, Session.MemberLoads, Session.LoadDefinitions);
        Session.MarkSaved();
        RefreshCollections();
        return true;
    }
}
