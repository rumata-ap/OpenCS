using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Input;
using CScore;
using CScore.Fem;
using CScore.Fem.Editing;
using CScore.Submodel;
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
    readonly AppViewModel _app;
    readonly DatabaseService _db;
    readonly ILogService _logService;
    readonly FemMemberFactory _memberFactory;
    readonly FemGjBatchPlanner _gjBatchPlanner;

    public FemSchemaEditSession Session   { get; }
    public FemSchemaSelectionVM Selection { get; } = new();

    public ObservableCollection<FemNode>        Nodes        { get; } = [];
    public ObservableCollection<FemMember>      Members      { get; } = [];
    /// <summary>Конструктивные стержни, доступные для задания распределённых нагрузок.</summary>
    public IEnumerable<FemMember> BeamMembers => Members.Where(member => member.ElemType == "beam");
    public ObservableCollection<FemMemberGroup> MemberGroups { get; } = [];
    public ObservableCollection<FemLoadCase>    LoadCases    { get; } = [];
    public ObservableCollection<FemMemberLoad>  MemberLoads  { get; } = [];
    public ObservableCollection<FemKinematicLoad> KinematicLoads { get; } = [];
    public ObservableCollection<FemLoadDefinition> LoadDefinitions { get; } = [];

    /// <summary>Пул проектных сечений — источник для назначения FemMember.CrossSectionId.</summary>
    public ObservableCollection<CrossSection> CrossSections { get; }
    /// <summary>Все расчётные задачи проекта — отсюда фильтруются задачи кручения для GJ = Saint-Venant.</summary>
    public ObservableCollection<CalcTask> AllCalcTasks { get; }

    bool _createNodeMode, _createBarMode, _createPlateMode, _createWallMode, _createSpatialPlateMode;
    bool _isDiscretizing;
    public bool CreateNodeMode
    {
        get => _createNodeMode;
        set { _createNodeMode = value; if (value) { CreateBarMode = CreatePlateMode = CreateWallMode = CreateSpatialPlateMode = false; } OnPropertyChanged(); }
    }
    public bool CreateBarMode
    {
        get => _createBarMode;
        set { _createBarMode = value; if (value) { CreateNodeMode = CreatePlateMode = CreateWallMode = CreateSpatialPlateMode = false; } OnPropertyChanged(); }
    }
    public bool CreatePlateMode
    {
        get => _createPlateMode;
        set { _createPlateMode = value; if (value) { CreateNodeMode = CreateBarMode = CreateWallMode = CreateSpatialPlateMode = false; } OnPropertyChanged(); }
    }
    public bool CreateWallMode
    {
        get => _createWallMode;
        set { _createWallMode = value; if (value) { CreateNodeMode = CreateBarMode = CreatePlateMode = CreateSpatialPlateMode = false; } OnPropertyChanged(); }
    }
    public bool CreateSpatialPlateMode
    {
        get => _createSpatialPlateMode;
        set { _createSpatialPlateMode = value; if (value) { CreateNodeMode = CreateBarMode = CreatePlateMode = CreateWallMode = false; } OnPropertyChanged(); }
    }
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
        set
        {
            _selectedLoadCase = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedCaseElementLoads));
        }
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

    /// <summary>Создаёт или обновляет заданные перемещения/повороты выбранных узлов.</summary>
    public int ApplyKinematicLoads(FemLoadCase loadCase, IEnumerable<FemNode> nodes,
        IReadOnlyDictionary<int, double> values)
    {
        int applied = 0;
        foreach (var node in nodes.Where(node => node.Id != 0))
        {
            for (int dof = 1; dof <= 6; dof++)
            {
                if (values.TryGetValue(dof, out var value))
                    Session.Execute(new SetKinematicLoadCommand(loadCase.Id, node.Id, dof, value));
                else if (Session.KinematicLoads.FirstOrDefault(load =>
                             load.LoadCaseId == loadCase.Id && load.NodeId == node.Id && load.Dof == dof) is { } existing)
                    Session.Execute(new DeleteKinematicLoadCommand(existing));
            }
            applied++;
        }
        if (applied > 0) RefreshCollections();
        return applied;
    }

    /// <summary>Удаляет заданное перемещение или поворот узла.</summary>
    public bool DeleteKinematicLoad(FemNode node, FemLoadCase loadCase, int dof)
    {
        var load = Session.KinematicLoads.FirstOrDefault(item =>
            item.LoadCaseId == loadCase.Id && item.NodeId == node.Id && item.Dof == dof);
        if (load == null) return false;
        Session.Execute(new DeleteKinematicLoadCommand(load));
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

    /// <summary>Нагрузки на КЭ выбранного загружения со строкой для списка.</summary>
    public IReadOnlyList<FemElementLoadView> SelectedCaseElementLoads => SelectedLoadCase is { } lc
        ? Session.ElementLoads.Where(l => l.LoadCaseId == lc.Id).Select(l => new FemElementLoadView(l, Session.MemberGroups)).ToList()
        : [];

    FemElementLoadView? _selectedElementLoad;
    /// <summary>Нагрузка на КЭ, выбранная в списке загружения.</summary>
    public FemElementLoadView? SelectedElementLoad
    {
        get => _selectedElementLoad;
        set { _selectedElementLoad = value; OnPropertyChanged(); }
    }

    /// <summary>Режимы цели нагрузки на пластины в редакторе.</summary>
    public const string AreaTargetSelection = "selection", AreaTargetGroup = "group", AreaTargetList = "list";

    /// <summary>
    /// Равномерная нагрузка на пластины выбранного загружения (новая или вместо выбранной в списке). Цель: выделенное
    /// (КЭ сетки, иначе КонЭ), группа или список КЭ «1-50, 75». Возвращает текст ошибки или null.
    /// </summary>
    public string? ApplyAreaLoad(string targetMode, FemMemberGroup? group, string? tagsText, double pressurePa,
        string coordinateSystem, string axis, bool replaceSelected)
    {
        if (SelectedLoadCase is not { } lc) return Loc.S("FemAreaLoadNoCase");
        if (!double.IsFinite(pressurePa) || pressurePa == 0) return Loc.S("FemAreaLoadZero");
        var load = new FemElementLoad
        {
            SchemaId = Session.Schema.Id, LoadCaseId = lc.Id, LoadKind = FemElementLoadKinds.Uniform,
            CoordinateSystem = coordinateSystem, Axis = axis,
        };
        load.SetValues([pressurePa]);
        switch (targetMode)
        {
            case AreaTargetSelection when Selection.SelectedMeshElemTags.Count > 0:
                load.TargetKind = FemLoadTargetKinds.Elements;
                load.SetTargetTags(Selection.SelectedMeshElemTags);
                break;
            case AreaTargetSelection when Selection.SelectedElemTags.Count > 0:
                load.TargetKind = FemLoadTargetKinds.Members;
                load.SetTargetTags(Selection.SelectedElemTags);
                break;
            case AreaTargetSelection:
                return Loc.S("FemAreaLoadNoSelection");
            case AreaTargetGroup when group is { Id: > 0 }:
                load.TargetKind = FemLoadTargetKinds.Group;
                load.GroupId = group.Id;
                break;
            case AreaTargetGroup:
                return Loc.S("FemAreaLoadNoGroup");
            default:
                var tags = FemTagList.Parse(tagsText, out var error);
                if (error != null) return error;
                if (tags.Count == 0) return Loc.S("FemAreaLoadNoTags");
                load.TargetKind = FemLoadTargetKinds.Elements;
                load.SetTargetTags(tags);
                break;
        }
        if (replaceSelected && SelectedElementLoad?.Load is { } old && Session.ElementLoads.Contains(old))
            Session.Execute(new ReplaceElementLoadCommand(old, load));
        else
            Session.Execute(new AddElementLoadCommand(load));
        RefreshCollections();
        SelectedElementLoad = SelectedCaseElementLoads.FirstOrDefault(v => ReferenceEquals(v.Load, load));
        return null;
    }

    /// <summary>Удаляет нагрузку на КЭ, выбранную в списке.</summary>
    public void DeleteSelectedElementLoad()
    {
        if (SelectedElementLoad?.Load is not { } load) return;
        Session.Execute(new DeleteElementLoadCommand(load));
        SelectedElementLoad = null;
        RefreshCollections();
    }

    /// <summary>Коэффициент собственного веса выбранного загружения; null — без собственного веса.</summary>
    public void SetSelectedLoadCaseSelfWeight(double? factor)
    {
        if (SelectedLoadCase is not { } lc || lc.SelfWeightFactor == factor) return;
        Session.Execute(new EditLoadCaseCommand(lc, new FemLoadCase
        {
            Tag = lc.Tag, LoadType = lc.LoadType, Sp20Type = lc.Sp20Type, Sp20Group = lc.Sp20Group,
            GammaFUnfav = lc.GammaFUnfav, GammaFFav = lc.GammaFFav, Psi1 = lc.Psi1, Psi2 = lc.Psi2,
            SelfWeightFactor = factor,
        }));
        RefreshCollections();
    }

    IReadOnlyList<FemValidationDiagnostic> _diagnostics = [];
    public IReadOnlyList<FemValidationDiagnostic> Diagnostics { get => _diagnostics; private set { _diagnostics = value; OnPropertyChanged(); } }

    StraightBeamChainAnalysis? _chainAnalysis;
    /// <summary>Выделение, с которым выполнена проверка цепочки: извлекается только оно.</summary>
    IReadOnlyList<string>? _chainSelection;
    IReadOnlyList<FemValidationDiagnostic> _extractDiagnostics = [];

    /// <summary>Результат проверки выделенной прямой цепочки, отдельно от диагностики схемы.
    /// Сбрасывается при любом изменении выделения стержней.</summary>
    public StraightBeamChainAnalysis? ChainAnalysis
    {
        get => _chainAnalysis;
        private set
        {
            _chainAnalysis = value;
            _extractDiagnostics = [];
            if (value is null) _chainSelection = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ChainVerdictText));
            OnPropertyChanged(nameof(ChainDiagnostics));
            OnPropertyChanged(nameof(ExtractBlockReason));
        }
    }

    public string ChainVerdictText
    {
        get
        {
            if (ChainAnalysis is not { } analysis) return "";
            string verdict = Loc.S(analysis.Verdict switch
            {
                ChainVerdict.Extractable => "SubmodelVerdictExtractable",
                ChainVerdict.ExtractableWithWarnings => "SubmodelVerdictExtractableWithWarnings",
                _ => "SubmodelVerdictNotExtractable"
            });
            return analysis.Chain is { } chain
                ? string.Format(Loc.S("SubmodelVerdictSummary"), verdict, chain.Segments.Count, chain.LengthM)
                : verdict;
        }
    }

    /// <summary>Диагностика проверки цепочки и попытки извлечения, ошибки первыми.</summary>
    public IReadOnlyList<FemValidationDiagnostic> ChainDiagnostics =>
        (ChainAnalysis?.Diagnostics ?? []).Concat(_extractDiagnostics).OrderByDescending(d => d.IsError).ToList();

    /// <summary>Линейные постановки схемы с результатом — родители извлечения; обновляются при проверке цепочки.</summary>
    public ObservableCollection<FemAnalysis> ParentAnalyses { get; } = [];

    FemAnalysis? _selectedParentAnalysis;
    public FemAnalysis? SelectedParentAnalysis
    {
        get => _selectedParentAnalysis;
        set { _selectedParentAnalysis = value; OnPropertyChanged(); OnPropertyChanged(nameof(ExtractBlockReason)); }
    }

    bool ChainSelectionMatches => _chainSelection is { } snapshot
        && snapshot.ToHashSet(StringComparer.Ordinal).SetEquals(Selection.SelectedElemTags);

    /// <summary>Причина недоступности «Извлечь субмодель» (текст из ресурсов); null — можно извлекать.</summary>
    public string? ExtractBlockReason =>
        SubmodelExtractGate.Reason(ChainAnalysis?.Verdict, ChainSelectionMatches, SelectedParentAnalysis is not null, Session.IsDirty)
            is { } key ? Loc.S(key) : null;

    public ICommand ExtractSubmodelCommand { get; }

    /// <summary>Схема — извлечённая субмодель: показывается вкладка «Субмодель».</summary>
    public bool IsSubmodel { get; }

    /// <summary>Просьба открыть схему (дочернюю после извлечения) — выполняет страница через AppViewModel.</summary>
    public event Action<FemSchema>? OpenSchemaRequested;

    /// <summary>Загружает снимок сетки, анализирует выбранные стержневые элементы и обновляет список
    /// родительских постановок.</summary>
    public void AnalyzeStraightChain()
    {
        RefreshParentAnalyses();
        AnalyzeStraightChain(_db.GetFemMeshElements(Session.Schema.Id), _db.GetFemMeshNodes(Session.Schema.Id), Session.Members.ToList());
    }

    public void AnalyzeStraightChain(IReadOnlyList<FemElement> elements, IReadOnlyList<FemMeshNode> nodes, IReadOnlyList<FemMember> members)
    {
        var selection = Selection.SelectedElemTags.ToList();
        ChainAnalysis = StraightBeamSubmodelExtractionService.Analyze(selection, elements, nodes, members);
        _chainSelection = selection;
        OnPropertyChanged(nameof(ExtractBlockReason));
    }

    void RefreshParentAnalyses()
    {
        var selectedId = SelectedParentAnalysis?.Id;
        ParentAnalyses.Clear();
        foreach (var analysis in new StraightBeamSubmodelExtractionService(_db).EligibleParentAnalyses(Session.Schema.Id))
            ParentAnalyses.Add(analysis);
        SelectedParentAnalysis = ParentAnalyses.FirstOrDefault(a => a.Id == selectedId)
                                 ?? (ParentAnalyses.Count == 1 ? ParentAnalyses[0] : null);
    }

    void ExtractSubmodel()
    {
        if (ExtractBlockReason is not null || SelectedParentAnalysis is not { } analysis
            || ChainAnalysis?.Chain is not { } chain || _chainSelection is not { } selection) return;

        string tag = SubmodelExtractGate.DefaultTag(Loc.S("SubmodelDefaultTag"), Loc.S("SubmodelDefaultTagSingle"),
            Session.Schema.Tag, chain);
        var outcome = new StraightBeamSubmodelExtractionService(_db)
            .Extract(Session.Schema.Id, analysis.Id, selection, Session.Members.ToList(), tag);
        if (outcome.Extraction is { } extraction
            && _db.FemSchemas.FirstOrDefault(s => s.Id == extraction.SubmodelSchemaId) is { } child)
        {
            _logService.Info(string.Format(Loc.S("SubmodelExtractedLog"), child.Tag, extraction.Segments.Count));
            OpenSchemaRequested?.Invoke(child);
            return;
        }

        if (outcome.Chain is { } reanalyzed)
        {
            ChainAnalysis = reanalyzed;
            _chainSelection = selection;
        }
        _extractDiagnostics = outcome.Diagnostics
            .Where(d => ChainAnalysis?.Diagnostics.Contains(d) != true).ToList();
        OnPropertyChanged(nameof(ChainDiagnostics));
        OnPropertyChanged(nameof(ExtractBlockReason));
        foreach (var error in _extractDiagnostics.Where(d => d.IsError)) _logService.Error(error.Message);
    }

    /// <summary>Общий шаг стержней схемы, м (null — делятся только узлами). Хранится в схеме; локальный шаг КонЭ важнее.</summary>
    public double? DefaultTargetMeshLengthM
    {
        get => Session.Schema.MeshBarStepM;
        set
        {
            value = value is > 0 ? value : null;
            if (Session.Schema.MeshBarStepM == value) return;
            Session.Schema.MeshBarStepM = value;
            _db.UpdateFemSchemaMeshSteps(Session.Schema);
            OnPropertyChanged();
        }
    }

    /// <summary>Общий размер КЭ пластин схемы, м (null — размер из области). Хранится в схеме; локальный шаг КонЭ важнее.</summary>
    public double? DefaultPlateMeshStepM
    {
        get => Session.Schema.MeshPlateStepM;
        set
        {
            value = value is > 0 ? value : null;
            if (Session.Schema.MeshPlateStepM == value) return;
            Session.Schema.MeshPlateStepM = value;
            _db.UpdateFemSchemaMeshSteps(Session.Schema);
            OnPropertyChanged();
        }
    }

    /// <summary>Задаёт локальный шаг сетки КонЭ (стержней и пластин); null — общий шаг схемы. Отменяется как правка.</summary>
    public void SetMembersMeshStep(IReadOnlyList<string> tags, double? stepM)
    {
        var members = Session.Members.Where(m => tags.Contains(m.ElemTag) && !m.IsMeshLocked).ToList();
        if (members.Count == 0) return;
        Session.Execute(new SetMembersMeshStepCommand(members, stepM is > 0 ? stepM : null));
        RefreshCollections();
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

    /// <summary>«КонЭ из выделенных КЭ» — через приложение: элементы пишутся в БД мимо сеанса, страница перезагружается.</summary>
    public Func<IReadOnlyList<string>, bool> CreateMembersFromMeshElements { get; }

    /// <summary>После сохранения: дерево схемы перечитывает узлы и КонЭ (подписи, проверки, счётчики).</summary>
    readonly Action? _afterSave;

    public FemSchemaEditorVM(FemSchema schema, AppViewModel app)
    {
        _app = app;
        _db = app.db;
        _logService = app.LogService;
        CreateMembersFromMeshElements = tags => app.CreateFemMembersFromMeshElements(schema, tags);
        _afterSave = () => app.RefreshFemSchemaTreeCounts(schema);
        var gjResolver = new FemGjDefaultResolver(() => app.CalcSettings);
        _memberFactory = new FemMemberFactory(gjResolver);
        _gjBatchPlanner = new FemGjBatchPlanner(gjResolver);
        CrossSections = app.CrossSections;
        AllCalcTasks  = app.CalcTasks;
        Session = new FemSchemaEditSession(schema);
        Session.Nodes.AddRange(_db.GetFemNodes(schema.Id));
        Session.Members.AddRange(_db.GetFemMembers(schema.Id));
        Session.MemberGroups.AddRange(schema.MemberGroups);
        Session.LoadCases.AddRange(schema.LoadCases);
        Session.NodeLoads.AddRange(_db.GetFemNodeLoads(schema.Id));
        Session.MemberLoads.AddRange(_db.GetFemMemberLoads(schema.Id));
        Session.KinematicLoads.AddRange(_db.GetFemKinematicLoads(schema.Id));
        Session.ElementLoads.AddRange(_db.GetFemElementLoads(schema.Id));
        Session.MeshNodeLoads.AddRange(_db.GetFemMeshNodeLoads(schema.Id));
        Session.LoadDefinitions.AddRange(schema.LoadDefinitions);
        RefreshCollections();

        UndoCommand = new RelayCommand(_ => UndoHistoryStep(), _ => Session.CanUndo);
        RedoCommand = new RelayCommand(_ => RedoHistoryStep(), _ => Session.CanRedo);
        SaveCommand = new RelayCommand(_ => Save(), _ => Session.IsDirty);
        DiscretizeCommand = new RelayCommand(async _ => await DiscretizeAsync(), _ => !IsDiscretizing);
        MergeNodesCommand = new RelayCommand(_ => _logService.Info(MergeCoincidentNodes()));
        ExtractSubmodelCommand = new RelayCommand(_ => ExtractSubmodel(), _ => ExtractBlockReason is null);
        IsSubmodel = schema.SourceType == "submodel" && _db.GetSubmodelExtractionBySubmodelSchema(schema.Id) is not null;
        Selection.SelectedElemTags.CollectionChanged += (_, _) =>
        {
            if (ChainAnalysis is not null) ChainAnalysis = null;
        };
    }

    void UndoHistoryStep()
    {
        var command = Session.Undo();
        if (command is DeleteNodeCommand or DeleteNodesCommand)
            Selection.Clear();
        if (IsGroupOnly(command)) RefreshGroups();
        else RefreshCollections();
    }

    void RedoHistoryStep()
    {
        var command = Session.Redo();
        if (command is DeleteNodeCommand or DeleteNodesCommand)
            Selection.Clear();
        if (IsGroupOnly(command)) RefreshGroups();
        else RefreshCollections();
    }

    /// <summary>Команда меняет только группы — отмена не перестраивает 3D-вид (и не сбрасывает камеру).</summary>
    static bool IsGroupOnly(IFemEditCommand? command) => command is EditMemberGroupTagsCommand or CreateMemberGroupCommand;

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
        CrossSection? section = null;
        if (sectionTag != null)
        {
            var cs = CrossSections.FirstOrDefault(s => s.Tag == sectionTag);
            if (cs != null)
            {
                sectionId = cs.Id;
                section = cs;
            }
        }
        Session.Execute(new AddMemberCommand(
            _memberFactory.CreateBeam(Session.Schema.Id, tag, json, sectionId, section)));
        RefreshCollections();
    }

    /// <summary>Строит предварительный план массового назначения GJ без изменения схемы.</summary>
    public FemGjBatchPlan PreviewGjBatch(IEnumerable<FemMember> members, FemGjBatchMode mode)
    {
        var sections = CrossSections
            .GroupBy(section => section.Id)
            .ToDictionary(group => group.Key, group => group.First());
        return _gjBatchPlanner.Build(members, sections, mode);
    }

    /// <summary>Применяет заранее подготовленный план массового назначения GJ одной undo-командой.</summary>
    public void ApplyGjBatch(FemGjBatchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Assignments.Count == 0) return;
        Session.Execute(new SetMembersGjCommand(plan.Assignments));
        RefreshCollections();
    }

    List<FemNode> ResolveNodes(IEnumerable<string> nodeTags) => nodeTags
        .Where(tag => !string.IsNullOrWhiteSpace(tag))
        .Distinct(StringComparer.Ordinal)
        .Select(tag => Session.Nodes.FirstOrDefault(node => node.NodeTag == tag))
        .OfType<FemNode>()
        .ToList();

    /// <summary>Возвращает количество узлов и элементов, затрагиваемых удалением заданных тегов.</summary>
    public FemNodeDeletionImpact GetNodeDeletionImpact(IEnumerable<string> nodeTags)
        => DeleteNodesCommand.Preview(Session, ResolveNodes(nodeTags));

    /// <summary>Удаляет заданные узлы вместе с примыкающими элементами и связанными данными.
    /// Операция записывается в историю одной командой.</summary>
    public bool DeleteNodesByTags(IEnumerable<string> nodeTags)
    {
        var nodes = ResolveNodes(nodeTags);
        if (nodes.Count == 0) return false;
        if (RejectLockedNodes(nodes.Select(n => n.NodeTag))) return false;

        Session.Execute(nodes.Count == 1
            ? new DeleteNodeCommand(nodes[0])
            : new DeleteNodesCommand(nodes));
        Selection.Clear();
        RefreshCollections();
        return true;
    }

    public CScore.Planar.Frame3D? BuildPlateFrame(string nodeTag)
    {
        var node = Session.Nodes.FirstOrDefault(n => n.NodeTag == nodeTag);
        if (node == null) return null;
        return CScore.Planar.PlanarFrameBuilder.BuildPlateFrame(
            new CScore.Planar.PlanarVector3(node.X, node.Y, node.Z));
    }

    public CScore.Planar.Frame3D? BuildWallFrame(string nodeTagA, string nodeTagB)
    {
        var a = Session.Nodes.FirstOrDefault(n => n.NodeTag == nodeTagA);
        var b = Session.Nodes.FirstOrDefault(n => n.NodeTag == nodeTagB);
        if (a == null || b == null) return null;
        try
        {
            return CScore.Planar.PlanarFrameBuilder.BuildWallFrame(
                new CScore.Planar.PlanarVector3(a.X, a.Y, a.Z),
                new CScore.Planar.PlanarVector3(b.X, b.Y, b.Z));
        }
        catch (InvalidOperationException ex)
        {
            _logService.Error(ex.Message);
            return null;
        }
    }

    public CScore.Planar.Frame3D? BuildSpatialPlateFrame(string nodeTagA, string nodeTagB, string nodeTagC)
    {
        var a = Session.Nodes.FirstOrDefault(n => n.NodeTag == nodeTagA);
        var b = Session.Nodes.FirstOrDefault(n => n.NodeTag == nodeTagB);
        var c = Session.Nodes.FirstOrDefault(n => n.NodeTag == nodeTagC);
        if (a == null || b == null || c == null) return null;
        try
        {
            return CScore.Planar.PlanarFrameBuilder.BuildSpatialPlateFrame(
                new CScore.Planar.PlanarVector3(a.X, a.Y, a.Z),
                new CScore.Planar.PlanarVector3(b.X, b.Y, b.Z),
                new CScore.Planar.PlanarVector3(c.X, c.Y, c.Z));
        }
        catch (InvalidOperationException ex)
        {
            _logService.Error(ex.Message);
            return null;
        }
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

    /// <summary>Сдвигает узел. False — узел не найден или на него опирается элемент с сеткой ЛИРЫ.</summary>
    public bool MoveNodeByTag(string nodeTag, double dx, double dy, double dz)
    {
        var node = Session.Nodes.FirstOrDefault(n => n.NodeTag == nodeTag);
        if (node == null) return false;
        if (RejectLockedNodes([nodeTag])) return false;
        Session.Execute(new MoveNodeCommand(node, node.X + dx, node.Y + dy, node.Z + dz));
        RefreshCollections();
        return true;
    }

    /// <summary>Сообщает (<see cref="GeometryLocked"/>) и возвращает true, если среди узлов есть узлы
    /// элементов с импортированной сеткой — их геометрию менять нельзя.</summary>
    bool RejectLockedNodes(IEnumerable<string> nodeTags)
    {
        var locked = FemMeshLock.LockedMembersOf(Session.Members, nodeTags);
        if (locked.Count == 0) return false;
        GeometryLocked?.Invoke(string.Format(Loc.S("FemMemberMeshLockedNodes"),
            string.Join(", ", locked.Take(5).Select(m => $"«{m.ElemTag}»")) + (locked.Count > 5 ? ", …" : "")));
        return true;
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
        var memberTags = members.Select(e => e.ElemTag).ToList();
        if (memberTags.Count == 0) return;
        var group = FemGroupComposition.NewMembersGroup(Session.Schema.Id, memberTags, $"M{MemberGroups.Count + 1}", null);
        Session.Execute(new CreateMemberGroupCommand(group));
        RefreshCollections();
    }

    // ── Группы из выбора в 3D: в сеансе (сохранение редактора переписывает группы схемы) и с отменой ──

    /// <summary>Группы сеанса заданного вида.</summary>
    public IEnumerable<FemMemberGroup> GroupsOfKind(string kind) => Session.MemberGroups.Where(g => g.Kind == kind);

    HashSet<string>? _importedMeshTags;

    /// <summary>Годные для группы теги: у группы КонЭ — конструктивные элементы сеанса, у группы КЭ —
    /// импортированные КЭ сетки (номера КЭ дискретизации меняются при пересетке). Отброшенное — в журнал.</summary>
    public List<string> AcceptGroupTags(string kind, IEnumerable<string> tags)
    {
        var distinct = tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        HashSet<string> known;
        if (kind == FemMemberGroup.KindMesh)
            // Импортированная сетка в редакторе не меняется — номера читаются один раз.
            known = _importedMeshTags ??= _db.GetFemMeshElements(Session.Schema.Id)
                .Where(e => e.Origin == FemMember.MeshSourceImported)
                .Select(e => e.ElemTag).ToHashSet(StringComparer.Ordinal);
        else
            known = Session.Members.Select(m => m.ElemTag).ToHashSet(StringComparer.Ordinal);
        var rejected = distinct.Where(t => !known.Contains(t)).ToList();
        if (rejected.Count > 0)
            _logService.Warning(string.Format(Loc.S(kind == FemMemberGroup.KindMesh ? "FemGroupMeshUnknown" : "FemGroupMembersUnknown"),
                rejected.Count, string.Join(", ", rejected.Take(20)) + (rejected.Count > 20 ? ", …" : "")));
        return distinct.Where(known.Contains).ToList();
    }

    /// <summary>Новая группа КЭ или КонЭ в сеансе. Null — ни один тег не годится (см. журнал).</summary>
    public FemMemberGroup? CreateGroup(string kind, IEnumerable<string> tags, string? tag, string? memberType)
    {
        var accepted = AcceptGroupTags(kind, tags);
        if (accepted.Count == 0) return null;
        var group = kind == FemMemberGroup.KindMesh
            ? FemGroupComposition.NewMeshGroup(Session.Schema.Id, accepted, tag, memberType)
            : FemGroupComposition.NewMembersGroup(Session.Schema.Id, accepted, tag, memberType);
        Session.Execute(new CreateMemberGroupCommand(group));
        RefreshGroups();
        _logService.Info(string.Format(Loc.S("FemGroupCreated"), group.Tag, group.Tags.Count));
        return group;
    }

    /// <summary>Добавляет теги в состав группы сеанса (с проверкой по виду) или убирает их. Возвращает число изменённых.</summary>
    public int EditGroupTags(FemMemberGroup group, IEnumerable<string> tags, bool remove)
    {
        var list = remove ? tags.ToList() : AcceptGroupTags(group.Kind, tags);
        var present = group.Tags.ToHashSet(StringComparer.Ordinal);
        if (!list.Any(t => present.Contains(t) == remove)) return 0;
        var command = new EditMemberGroupTagsCommand(group, list, remove);
        Session.Execute(command);
        RefreshGroups();
        _logService.Info(string.Format(Loc.S(remove ? "FemGroupTagsRemoved" : "FemGroupTagsAdded"), command.Changed, group.Tag));
        return command.Changed;
    }

    /// <summary>После правки только групп: геометрия не менялась — 3D-вид не перестраивается.</summary>
    void RefreshGroups()
    {
        SyncList(MemberGroups, Session.MemberGroups);
        CollectionViewSource.GetDefaultView(MemberGroups).Refresh();
        OnPropertyChanged(nameof(ExtractBlockReason));
        CommandManager.InvalidateRequerySuggested();
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
            Psi2 = psi2,
            SelfWeightFactor = loadCase.SelfWeightFactor
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
            GammaFFav = loadCase.GammaFFav, Psi1 = loadCase.Psi1, Psi2 = loadCase.Psi2,
            SelfWeightFactor = loadCase.SelfWeightFactor
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
        SyncList(KinematicLoads, Session.KinematicLoads);
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
        CollectionViewSource.GetDefaultView(KinematicLoads).Refresh();
        CollectionViewSource.GetDefaultView(LoadDefinitions).Refresh();
        OnPropertyChanged(nameof(SelectedLoadDefinitionTerms));
        OnPropertyChanged(nameof(SelectedCaseElementLoads));
        OnPropertyChanged(nameof(ExtractBlockReason));
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
    /// <summary>Правка отклонена: затронута геометрия элемента с импортированной сеткой (текст — причина).</summary>
    public event Action<string>? GeometryLocked;
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

    /// <summary>
    /// «Построить сетку схемы» (CSfea 4г): стержни и пластины областей своей схемы (Gmsh, общие узлы). Сетка
    /// импорта (ЛИРА/SCAD) и элементы на ней сохраняются как есть. Изменившаяся сетка сбрасывает результаты
    /// постановок схемы; наборы усилий по прежней сетке перечисляются в журнале.
    /// </summary>
    public async Task DiscretizeAsync()
    {
        if (IsDiscretizing) return;
        IsDiscretizing = true;
        var schema = Session.Schema;
        bool planar = Session.Members.Any(m => m.PlanarRegionId != null && !m.IsMeshLocked);
        var cts = planar ? _app.BeginBusyWithCancellation(Loc.S("FemSchemaMeshBuilding")) : null;
        try
        {
            var service = new FemSchemaMeshService(_db, _app.GmshSettings);
            var result = await service.BuildAsync(schema.Id, Session.Nodes, Session.Members, DefaultTargetMeshLengthM,
                _logService.Info, cts?.Token ?? CancellationToken.None);
            LastMeshDiagnostics = result.Diagnostics;
            foreach (var d in result.Diagnostics)
                if (d.IsError) _logService.Error(d.Message); else _logService.Warning(d.Message);
            if (result.Mesh is not { } mesh || result.HasErrors)
            {
                if (planar) _logService.Warning(Loc.S("FemSchemaMeshNotSaved"));
                return;
            }

            bool changed = !service.IsSameAsStored(schema.Id, mesh);
            service.Save(schema.Id, mesh);
            if (planar)
                _logService.Info(string.Format(Loc.S("FemSchemaMeshDone"), mesh.Nodes.Count,
                    mesh.Elements.Count(e => e.ElemType == "beam"), mesh.Elements.Count(e => e.ElemType == "shell"),
                    result.RegionCount, result.RebuiltRegionCount, mesh.SharedNodeCount, mesh.SplitBeamCount));
            if (changed) ReportMeshChanged(schema);
            MeshDiscretized?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            _logService.Info(Loc.S("FemSchemaMeshCancelled"));
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or InvalidDataException or TimeoutException)
        {
            _logService.Error(ex.Message);
        }
        finally
        {
            if (cts != null) _app.EndBusy();
            IsDiscretizing = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Сетка схемы изменилась: результаты постановок сбрасываются, наборы усилий схемы (номера КЭ прежней
    /// сетки) перечисляются в журнале — не удаляются.</summary>
    void ReportMeshChanged(FemSchema schema)
    {
        int invalidated = 0;
        foreach (var analysis in schema.Analyses.Where(a => a.ResultId != null))
        {
            analysis.InvalidateResult();
            _db.SaveFemAnalysis(analysis);
            invalidated++;
        }
        if (invalidated > 0) _logService.Info(string.Format(Loc.S("FemSchemaMeshAnalysesInvalidated"), invalidated));
        var stale = _db.ForceSets.Where(f => f.SourceType == "fea" && f.SourceSchemaId == schema.Id).Select(f => f.Tag).ToList();
        if (stale.Count > 0) _logService.Warning(string.Format(Loc.S("FemSchemaMeshForceSetsStale"), string.Join(", ", stale)));
    }

    public bool Save()
    {
        // Схема с импортированной сеткой (ЛИРА): группы ссылаются на номера КЭ сетки, а узлы элементов
        // с заблокированной сеткой сверяются с узлами сетки.
        bool imported = _db.HasImportedMesh(Session.Schema.Id);
        var importedNodes = imported
            ? _db.GetFemMeshNodes(Session.Schema.Id).Where(n => n.Origin == FemMember.MeshSourceImported).ToList()
            : null;
        var importedElementTags = imported
            ? _db.GetFemMeshElements(Session.Schema.Id).Where(e => e.Origin == FemMember.MeshSourceImported)
                .Select(e => e.ElemTag).ToHashSet(StringComparer.Ordinal)
            : null;
        Diagnostics = FemTopologyValidator.Validate(Session.Schema, Session.Nodes, Session.Members, Session.MemberGroups,
                importedNodes, importedElementTags)
            .Concat(FemCanonicalValidator.Validate(Session.Schema, Session.LoadCases, Session.Nodes,
                Session.NodeLoads, Session.Members, Session.MemberLoads, Session.KinematicLoads))
            .Concat(FemLoadDefinitionValidator.Validate(Session.Schema, Session.LoadDefinitions, Session.LoadCases))
            .ToList();
        var errors = Diagnostics.Where(d => d.IsError).ToList();
        if (errors.Count > 0)
        {
            SaveBlocked?.Invoke(errors);
            return false;
        }

        _db.SaveFemSchemaEdit(Session.Schema.Id, Session.Nodes, Session.Members, Session.MemberGroups,
            Session.LoadCases, Session.NodeLoads, Session.MemberLoads, Session.KinematicLoads, Session.LoadDefinitions,
            Session.ElementLoads, Session.MeshNodeLoads);
        Session.MarkSaved();
        RefreshCollections();
        _afterSave?.Invoke();
        return true;
    }
}
