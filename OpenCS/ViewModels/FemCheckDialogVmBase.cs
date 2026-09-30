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
    }

    /// <summary>Наборы усилий, доступные цели.</summary>
    public ObservableCollection<FemCheckForceSetItem> FilteredForceSets { get; } = [];

    /// <summary>Цель проверки: группа или конструктивный элемент.</summary>
    protected abstract IFemCheckable? Target { get; }
    /// <summary>Идентификатор выбранной схемы.</summary>
    protected abstract int? SchemaId { get; }
    /// <summary>Проверка пластин (иначе — стержней).</summary>
    protected abstract bool IsPlateCheck { get; }
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

    bool _useSectionSource = true, _useAssignedSource, _useSelectedSource;
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

    public bool AssignedSourceEnabled { get; private set; }
    public bool SelectedSourceEnabled { get; private set; }
    /// <summary>Чего не хватает источнику ТЗА (подсказка отключённого флажка).</summary>
    public string? AssignedSourceHint { get; private set; }
    /// <summary>Чего не хватает источнику ASP.</summary>
    public string? SelectedSourceHint { get; private set; }

    public Visibility RebarSourcesVisibility => IsPlateCheck ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Отмеченные источники в порядке расчёта; ничего не отмечено — сечение цели.</summary>
    protected string[] SelectedRebarSources()
    {
        var keys = new List<string>(3);
        if (UseSectionSource) keys.Add(FemCheckRebarSource.Section);
        if (UseAssignedSource && AssignedSourceEnabled) keys.Add(FemCheckRebarSource.Assigned);
        if (UseSelectedSource && SelectedSourceEnabled) keys.Add(FemCheckRebarSource.Selected);
        return keys.Count > 0 ? [.. keys] : [FemCheckRebarSource.Section];
    }

    /// <summary>Отметить источники сохранённой проверки.</summary>
    protected void LoadRebarSources(PlateCheckParams p)
    {
        var keys = p.GetRebarSources();
        _suspendReadiness = true;
        UseSectionSource  = keys.Contains(FemCheckRebarSource.Section);
        UseAssignedSource = keys.Contains(FemCheckRebarSource.Assigned) && AssignedSourceEnabled;
        UseSelectedSource = keys.Contains(FemCheckRebarSource.Selected) && SelectedSourceEnabled;
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
    /// <summary>Кнопка видна, когда у пластинчатой цели нет сечения, а у схемы есть подбор ЛИРЫ.</summary>
    public Visibility CreateLiraSectionVisibility
    {
        get => _createLiraSectionVisibility;
        private set { _createLiraSectionVisibility = value; OnPropertyChanged(); }
    }

    void RefreshCreateLiraSection()
    {
        bool offer = IsPlateCheck && Target is { } target && _schemaData is { Asp: not null } data && _scope is { } scope
            && scope.Elements.Any(e => e.Element.ElemType == "shell")
            && FemCheckContext.TargetPlateSectionId(target, scope, SchemaGroups(data.SchemaId), out _) == null;
        CreateLiraSectionVisibility = offer ? Visibility.Visible : Visibility.Collapsed;
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

            bool anyTza = _scope.Elements.Any(e => !string.IsNullOrWhiteSpace(e.Element.ReinforcementTypeIds));
            AssignedSourceEnabled = data.Rbt != null && anyTza;
            AssignedSourceHint = data.Rbt == null ? Loc.S("FemCheckNoRbt") : anyTza ? null : Loc.S("FemCheckNoTza");
            SelectedSourceEnabled = data.Asp != null;
            SelectedSourceHint = data.Asp == null ? Loc.S("FemCheckNoAsp") : null;
        }
        else
        {
            AssignedSourceEnabled = SelectedSourceEnabled = false;
            AssignedSourceHint = SelectedSourceHint = null;
        }
        if (!AssignedSourceEnabled) UseAssignedSource = false;
        if (!SelectedSourceEnabled) UseSelectedSource = false;
        OnPropertyChanged(nameof(AssignedSourceEnabled));
        OnPropertyChanged(nameof(SelectedSourceEnabled));
        OnPropertyChanged(nameof(AssignedSourceHint));
        OnPropertyChanged(nameof(SelectedSourceHint));
        OnPropertyChanged(nameof(RebarSourcesVisibility));

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
