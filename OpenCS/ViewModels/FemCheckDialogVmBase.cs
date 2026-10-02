using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CScore;
using CScore.Fem;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Набор усилий в списке диалога проверки: сам набор и подпись с числом строк для цели.</summary>
public sealed class FemCheckForceSetItem(ForceSet forceSet, string display)
{
    public ForceSet ForceSet => forceSet;
    public string Display => display;
}

/// <summary>
/// Общая часть диалогов нормативной проверки: наборы усилий цели (в т.ч. наборы схемы со строками
/// по её КЭ), источники армирования КЭ пластины и строка готовности цели к проверке.
/// </summary>
public abstract class FemCheckDialogVmBase : ViewModelBase
{
    protected readonly AppViewModel App;
    protected readonly ListBox SetsBox;

    FemCheckSchemaData? _schemaData;
    FemCheckScope? _scope;
    bool _suspendReadiness;

    protected FemCheckDialogVmBase(AppViewModel app, ListBox setsBox)
    {
        App = app;
        SetsBox = setsBox;
        SetsBox.SelectionChanged += (_, _) => RefreshReadiness();
        CreateLiraSectionCommand = new RelayCommand(_ => CreateLiraSection());
        CreateLiraMaterialsCommand = new RelayCommand(_ => CreateLiraMaterials());
    }

    /// <summary>Наборы усилий, доступные цели.</summary>
    public ObservableCollection<FemCheckForceSetItem> FilteredForceSets { get; } = [];

    /// <summary>Цель проверки: группа или конструктивный элемент.</summary>
    protected abstract IFemCheckable? Target { get; }
    /// <summary>Идентификатор выбранной схемы.</summary>
    protected abstract int? SchemaId { get; }
    /// <summary>Проверка пластин (иначе — стержней).</summary>
    protected abstract bool IsPlateCheck { get; }
    /// <summary>Проверка железобетонных стержней: сечение КЭ можно собрать по данным ЛИРЫ.</summary>
    protected virtual bool IsBarRcCheck => false;
    /// <summary>Проверка по текущему состоянию диалога; редактируемую проверку не меняет.</summary>
    protected abstract FemCheck DraftCheck();

    bool _allSets = true;
    public bool AllSets
    {
        get => _allSets;
        set
        {
            _allSets = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSelectSets));
            if (value) SetsBox.SelectAll();
            RefreshReadiness();
        }
    }

    public bool CanSelectSets => !_allSets;

    // ── Источники армирования КЭ ─────────────────────────────────────────────────────────────

    bool _useSectionSource = true, _useAssignedSource, _useSelectedSource, _useLayoutSource;
    public bool UseSectionSource
    {
        get => _useSectionSource;
        set { _useSectionSource = value; OnPropertyChanged(); RefreshReadiness(); }
    }
    public bool UseAssignedSource
    {
        get => _useAssignedSource;
        set { _useAssignedSource = value; OnPropertyChanged(); RefreshReadiness(); }
    }
    public bool UseSelectedSource
    {
        get => _useSelectedSource;
        set { _useSelectedSource = value; OnPropertyChanged(); RefreshReadiness(); }
    }

    public bool UseLayoutSource
    {
        get => _useLayoutSource;
        set { _useLayoutSource = value; OnPropertyChanged(); RefreshReadiness(); }
    }

    public bool AssignedSourceEnabled { get; private set; }
    public bool SelectedSourceEnabled { get; private set; }
    /// <summary>Чего не хватает источнику ТЗА (подсказка отключённого флажка).</summary>
    public string? AssignedSourceHint { get; private set; }
    /// <summary>Чего не хватает источнику ASP.</summary>
    public string? SelectedSourceHint { get; private set; }
    /// <summary>У цели есть КЭ плоских конструктивных элементов — раскладку OpenCS есть куда наложить.</summary>
    public bool LayoutSourceEnabled { get; private set; }
    /// <summary>Чего не хватает источнику «раскладка OpenCS».</summary>
    public string? LayoutSourceHint { get; private set; }

    public Visibility RebarSourcesVisibility => IsPlateCheck || IsBarRcCheck ? Visibility.Visible : Visibility.Collapsed;
    /// <summary>Раскладка OpenCS — источник только для пластин.</summary>
    public Visibility LayoutSourceVisibility => IsPlateCheck ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Отмеченные источники в порядке расчёта; ничего не отмечено — сечение цели.</summary>
    protected string[] SelectedRebarSources()
    {
        var keys = new List<string>(4);
        if (UseSectionSource) keys.Add(FemCheckRebarSource.Section);
        if (UseAssignedSource && AssignedSourceEnabled) keys.Add(FemCheckRebarSource.Assigned);
        if (UseSelectedSource && SelectedSourceEnabled) keys.Add(FemCheckRebarSource.Selected);
        if (UseLayoutSource && LayoutSourceEnabled) keys.Add(FemCheckRebarSource.Layout);
        return keys.Count > 0 ? [.. keys] : [FemCheckRebarSource.Section];
    }

    /// <summary>Отметить источники сохранённой проверки.</summary>
    protected void LoadRebarSources(PlateCheckParams p) => LoadRebarSources(p.GetRebarSources());

    /// <summary>Отметить источники сохранённой проверки; пусто — сечение проекта.</summary>
    protected void LoadRebarSources(IReadOnlyCollection<string> keys)
    {
        if (keys.Count == 0) keys = [FemCheckRebarSource.Section];
        _suspendReadiness = true;
        UseSectionSource  = keys.Contains(FemCheckRebarSource.Section);
        UseAssignedSource = keys.Contains(FemCheckRebarSource.Assigned) && AssignedSourceEnabled;
        UseSelectedSource = keys.Contains(FemCheckRebarSource.Selected) && SelectedSourceEnabled;
        UseLayoutSource   = keys.Contains(FemCheckRebarSource.Layout) && LayoutSourceEnabled;
        _suspendReadiness = false;
        RefreshReadiness();
    }

    // ── Готовность цели ──────────────────────────────────────────────────────────────────────

    string _readinessText = "";
    /// <summary>Покрытие цели усилиями и сечениями/армированием.</summary>
    public string ReadinessText
    {
        get => _readinessText;
        private set { _readinessText = value; OnPropertyChanged(); }
    }

    // ── Сечение цели по данным ЛИРЫ ──────────────────────────────────────────────────────────

    /// <summary>Создать пластинчатое сечение цели по данным ЛИРЫ (подбор ASP + фоновые ТЗА).</summary>
    public ICommand CreateLiraSectionCommand { get; }

    Visibility _createLiraSectionVisibility = Visibility.Collapsed;
    /// <summary>Кнопка видна, когда у пластинчатой цели нет сечения, а у схемы есть подбор ЛИРЫ или ЖБ-группы SCAD.</summary>
    public Visibility CreateLiraSectionVisibility
    {
        get => _createLiraSectionVisibility;
        private set { _createLiraSectionVisibility = value; OnPropertyChanged(); }
    }

    /// <summary>Подпись кнопки создания сечения цели (по программе схемы).</summary>
    public string CreateSectionLabel { get; private set; } = Loc.S("FemCheckCreateLiraSection");

    void RefreshCreateLiraSection()
    {
        bool offer = IsPlateCheck && Target is { } target && _schemaData is { } data
            && (data.IsScad ? data.ScadConcreteGroups != null : data.Asp != null) && _scope is { } scope
            && scope.Elements.Any(e => e.Element.ElemType == "shell")
            && FemCheckContext.TargetPlateSectionId(target, scope, SchemaGroups(data.SchemaId), out _) == null;
        CreateLiraSectionVisibility = offer ? Visibility.Visible : Visibility.Collapsed;
        CreateSectionLabel = Loc.S(_schemaData?.IsScad == true ? "FemCheckCreateScadSection" : "FemCheckCreateLiraSection");
        OnPropertyChanged(nameof(CreateSectionLabel));
    }

    /// <summary>Создать недостающие материалы стержней по классам подбора ЛИРЫ.</summary>
    public ICommand CreateLiraMaterialsCommand { get; }

    Visibility _createLiraMaterialsVisibility = Visibility.Collapsed;
    /// <summary>Кнопка видна, когда у проверки железобетонных стержней в проекте нет материалов классов из подбора ЛИРЫ.</summary>
    public Visibility CreateLiraMaterialsVisibility
    {
        get => _createLiraMaterialsVisibility;
        private set { _createLiraMaterialsVisibility = value; OnPropertyChanged(); }
    }

    /// <summary>Подпись кнопки создания материалов (по программе схемы).</summary>
    public string CreateMaterialsLabel { get; private set; } = Loc.S("FemCheckCreateLiraMaterials");
    /// <summary>Подсказка кнопки создания материалов.</summary>
    public string CreateMaterialsHint { get; private set; } = Loc.S("FemCheckCreateLiraMaterialsHint");

    List<(string Class, bool Concrete)> MissingBarMaterials() =>
        IsBarRcCheck && _schemaData is { } data && _scope is { } scope
            ? LiraBarMaterialCreator.MissingClasses(App.Materials, data, scope.Elements)
            : [];

    void CreateLiraMaterials()
    {
        var missing = MissingBarMaterials();
        if (missing.Count == 0) return;
        var report = LiraBarMaterialCreator.Create(App.db, missing);
        foreach (string tag in report.Created)
            App.LogService.Info(string.Format(Loc.S("LiraSectionsMaterialCreated"), tag));
        foreach (string cls in report.NotInCatalog)
            App.LogService.Warning(string.Format(Loc.S("LiraBarMaterialNotInCatalog"), cls));
        if (report.NotInCatalog.Count > 0)
            MessageBox.Show(string.Format(Loc.S("LiraBarMaterialNotInCatalog"), string.Join(", ", report.NotInCatalog)),
                CreateMaterialsLabel, MessageBoxButton.OK, MessageBoxImage.Warning);
        RefreshReadiness();
    }

    IEnumerable<FemMemberGroup> SchemaGroups(int schemaId) =>
        App.FemSchemas.FirstOrDefault(s => s.Id == schemaId)?.MemberGroups ?? [];

    void CreateLiraSection()
    {
        if (Target is not { } target || App.FemSchemas.FirstOrDefault(s => s.Id == SchemaId) is not { } schema) return;
        if (!App.CreateLiraPlateSections(schema, [target])) return;
        // Сечение назначено цели; у группы оно ищется и через конструктивные элементы схемы — перечитываем.
        ReloadSchema();
        RefreshTarget();
    }

    /// <summary>Выбранные в списке наборы усилий.</summary>
    protected IEnumerable<ForceSet> SelectedForceSets() =>
        AllSets ? FilteredForceSets.Select(i => i.ForceSet)
                : SetsBox.SelectedItems.OfType<FemCheckForceSetItem>().Select(i => i.ForceSet);

    /// <summary>Отметить в списке наборы с заданными id.</summary>
    protected void SelectForceSets(IEnumerable<int> ids)
    {
        var set = ids.ToHashSet();
        // Пока действовало «Все наборы», в списке было отмечено всё.
        SetsBox.UnselectAll();
        foreach (var item in FilteredForceSets.Where(i => set.Contains(i.ForceSet.Id)))
            SetsBox.SelectedItems.Add(item);
    }

    /// <summary>Перечитать данные схемы (после смены схемы в диалоге).</summary>
    protected void ReloadSchema()
    {
        _schemaData = SchemaId is int id ? FemCheckSchemaData.Load(App.db, id) : null;
    }

    /// <summary>Есть ли в составе цели пластинчатые КЭ (по данным выбранной схемы).</summary>
    protected bool HasShellElements(IFemCheckable target) =>
        _schemaData is { } data && data.Scope(target).Elements.Any(e => e.Element.ElemType == "shell");

    /// <summary>Пересобрать наборы усилий, доступность источников и готовность после смены цели или вида проверки.</summary>
    protected void RefreshTarget()
    {
        _suspendReadiness = true;
        FilteredForceSets.Clear();
        _scope = null;
        if (Target is { } target && _schemaData is { } data)
        {
            _scope = data.Scope(target);
            foreach (var (fs, rows) in FemCheckContext.TargetForceSets(App.ForceSets, target, data.SchemaId, _scope, IsPlateCheck))
                if (AcceptsForceSet(fs))
                    FilteredForceSets.Add(new FemCheckForceSetItem(fs, string.Format(Loc.S("FemCheckDlgSetRows"), fs.Tag, rows)));

            // У стержней ТЗА разбираются только простые брусовые (ряды у нижней и верхней грани).
            string elemType = IsPlateCheck ? "shell" : "beam";
            bool anyTza = _scope.Elements.Any(e =>
                e.Element.ElemType == elemType && !string.IsNullOrWhiteSpace(e.Element.ReinforcementTypeIds));
            if (data.IsScad)
            {
                // Заданное армирование SCAD — группы из .SPR; подобранное — выгрузка плагина.
                bool anyAssigned = _scope.Elements.Any(e => e.Element.ElemType == elemType && e.ElemNum is int n
                    && (IsPlateCheck ? data.ScadAssigned?.Plate(n) : (object?)data.ScadAssigned?.Rod(n)) != null);
                AssignedSourceEnabled = anyAssigned;
                AssignedSourceHint = anyAssigned ? null
                    : data.ScadAssigned is not { IsEmpty: false } ? Loc.S("FemCheckNoScadAssigned")
                    : Loc.S("FemCheckScadAssignedNotInTarget");
                SelectedSourceEnabled = data.ScadSelected != null;
                SelectedSourceHint = data.ScadSelected == null ? Loc.S("FemCheckNoScadSelected") : null;
            }
            else
            {
                AssignedSourceEnabled = data.Rbt != null && anyTza && (IsPlateCheck || data.Rbt.BarTypes.Count > 0);
                AssignedSourceHint = data.Rbt == null ? Loc.S("FemCheckNoRbt") : anyTza ? null : Loc.S("FemCheckNoTza");
                SelectedSourceEnabled = data.Asp != null;
                SelectedSourceHint = data.Asp == null ? Loc.S("FemCheckNoAsp") : null;
            }
            LayoutSourceEnabled = IsPlateCheck && _scope.Elements.Any(e =>
                e.Element.ElemType == "shell" && e.Member?.PlanarRegionId != null);
            LayoutSourceHint = LayoutSourceEnabled ? null : Loc.S("FemCheckNoLayout");
        }
        else
        {
            AssignedSourceEnabled = SelectedSourceEnabled = LayoutSourceEnabled = false;
            AssignedSourceHint = SelectedSourceHint = LayoutSourceHint = null;
        }
        if (!AssignedSourceEnabled) UseAssignedSource = false;
        if (!SelectedSourceEnabled) UseSelectedSource = false;
        if (!LayoutSourceEnabled) UseLayoutSource = false;
        OnPropertyChanged(nameof(AssignedSourceEnabled));
        OnPropertyChanged(nameof(SelectedSourceEnabled));
        OnPropertyChanged(nameof(AssignedSourceHint));
        OnPropertyChanged(nameof(SelectedSourceHint));
        OnPropertyChanged(nameof(LayoutSourceEnabled));
        OnPropertyChanged(nameof(LayoutSourceHint));
        OnPropertyChanged(nameof(RebarSourcesVisibility));
        OnPropertyChanged(nameof(LayoutSourceVisibility));

        OnForceSetsRefreshed();
        if (AllSets) SetsBox.SelectAll();
        _suspendReadiness = false;
        RefreshReadiness();
    }

    /// <summary>Подходит ли набор этому диалогу (по виду набора).</summary>
    protected virtual bool AcceptsForceSet(ForceSet fs) => true;

    /// <summary>Список наборов пересобран — наследник обновляет зависимые списки.</summary>
    protected virtual void OnForceSetsRefreshed() { }

    /// <summary>Пересчитать строку готовности по текущему выбору.</summary>
    public void RefreshReadiness()
    {
        if (_suspendReadiness) return;
        RefreshCreateLiraSection();
        CreateLiraMaterialsVisibility = MissingBarMaterials().Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        bool scad = _schemaData?.IsScad == true;
        CreateMaterialsLabel = Loc.S(scad ? "FemCheckCreateScadMaterials" : "FemCheckCreateLiraMaterials");
        CreateMaterialsHint = Loc.S(scad ? "FemCheckCreateScadMaterialsHint" : "FemCheckCreateLiraMaterialsHint");
        OnPropertyChanged(nameof(CreateMaterialsLabel));
        OnPropertyChanged(nameof(CreateMaterialsHint));
        if (Target is not { } target || _schemaData is not { } data || _scope is not { } scope)
        {
            ReadinessText = "";
            return;
        }

        var check = DraftCheck();
        var sets = SelectedForceSets().ToList();
        if (sets.Count == 0)
        {
            ReadinessText = Loc.S("FemCheckReadyNoSets");
            return;
        }
        if (!FemCheckRunner.HasElementNumbers(check, sets) || FemCheckRunner.ScopeElements(check, scope).Count == 0)
        {
            ReadinessText = Loc.S("FemCheckReadyNoElemNums");
            return;
        }

        var inputs = FemCheckContext.BuildInputs(App, check, target, data, scope, sets);
        var readiness = FemCheckRunner.EvaluateReadiness(check, scope, sets, inputs);
        ReadinessText = FemCheckContext.ReadinessLine(readiness, IsPlateCheck);
    }
}
