using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CScore;
using CScore.History;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Строка этапа нагружения в диалоге задачи load_history.</summary>
public sealed class LoadHistoryStageRowVM : ViewModelBase
{
    readonly Action _activationChanged;
    string _tag = "";
    int? _activatePart;
    bool _useManual = true;
    ForceSet? _set;
    LoadItem? _item;
    string _n = "0", _mx = "0", _my = "0";
    int _steps = 5;
    IReadOnlyList<int?> _partChoices = [null];

    internal LoadHistoryStageRowVM(IReadOnlyList<ForceSet> forceSets, Action activationChanged)
    {
        ForceSets = forceSets;
        _activationChanged = activationChanged;
    }

    /// <summary>Наборы усилий для режима «из набора».</summary>
    public IReadOnlyList<ForceSet> ForceSets { get; }

    /// <summary>Наименование этапа.</summary>
    public string Tag { get => _tag; set { _tag = value; OnPropertyChanged(); } }

    /// <summary>Вид этапа: строки таблицы — всегда нагружение (разрушение — отдельный блок).</summary>
    public StageKind Kind => StageKind.Load;

    /// <summary>Часть, включаемая в начале этапа; null — без включения.</summary>
    public int? ActivatePart
    {
        get => _activatePart;
        set
        {
            if (_activatePart == value) return;
            _activatePart = value;
            OnPropertyChanged();
            _activationChanged();
        }
    }

    /// <summary>Допустимые значения «включить часть»: null и ещё не включённые предыдущими этапами части.</summary>
    public IReadOnlyList<int?> PartChoices
    {
        get => _partChoices;
        internal set { _partChoices = value; OnPropertyChanged(); }
    }

    /// <summary>Усилие задаётся вручную (иначе — строкой набора).</summary>
    public bool UseManual
    {
        get => _useManual;
        set
        {
            _useManual = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UseSet));
            OnPropertyChanged(nameof(ForceText));
        }
    }

    /// <summary>Обратный к <see cref="UseManual"/> флаг для переключателя.</summary>
    public bool UseSet { get => !_useManual; set => UseManual = !value; }

    /// <summary>Набор усилий (режим «из набора»).</summary>
    public ForceSet? Set
    {
        get => _set;
        set
        {
            _set = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Items));
            Item = value?.Items.FirstOrDefault();
        }
    }

    /// <summary>Строки выбранного набора.</summary>
    public IReadOnlyList<LoadItem> Items => _set?.Items ?? [];

    /// <summary>Строка набора (режим «из набора»).</summary>
    public LoadItem? Item
    {
        get => _item;
        set { _item = value; OnPropertyChanged(); OnPropertyChanged(nameof(ForceText)); }
    }

    /// <summary>Ручное N, кН.</summary>
    public string N { get => _n; set { _n = value; OnPropertyChanged(); OnPropertyChanged(nameof(ForceText)); } }
    /// <summary>Ручной Mx, кН·м.</summary>
    public string Mx { get => _mx; set { _mx = value; OnPropertyChanged(); OnPropertyChanged(nameof(ForceText)); } }
    /// <summary>Ручной My, кН·м.</summary>
    public string My { get => _my; set { _my = value; OnPropertyChanged(); OnPropertyChanged(nameof(ForceText)); } }

    /// <summary>Число шагов этапа.</summary>
    public int Steps { get => _steps; set { _steps = value; OnPropertyChanged(); } }

    /// <summary>Краткая запись усилия для таблицы этапов.</summary>
    public string ForceText => _useManual
        ? $"N={_n}; Mx={_mx}; My={_my}"
        : _item == null ? "—" : $"{_set?.Tag}: {_item.Label}";
}

/// <summary>Роли бетона и арматуры части в критерии предела.</summary>
public sealed class PartRoleRowVM : ViewModelBase
{
    PartLimitRole _concrete, _rebar;

    /// <summary>Индекс части.</summary>
    public int PartIndex { get; init; }
    /// <summary>Наименование части.</summary>
    public string PartTag { get; init; } = "";
    /// <summary>Роль бетона части.</summary>
    public PartLimitRole Concrete { get => _concrete; set { _concrete = value; OnPropertyChanged(); } }
    /// <summary>Роль арматуры части.</summary>
    public PartLimitRole Rebar { get => _rebar; set { _rebar = value; OnPropertyChanged(); } }
}

/// <summary>Вариант роли части для выпадающего списка.</summary>
public sealed record PartRoleChoice(PartLimitRole Value, string Label);

/// <summary>
/// Параметры задачи load_history в диалоге: этапы по нагрузке с включением частей, доведение до разрушения,
/// роли частей, сравнение «без истории» и способ продолжения несошедшегося шага.
/// </summary>
public sealed class LoadHistoryParamsVM : ViewModelBase
{
    /// <summary>Код ошибки разбора числа (ключ строки LoadHistoryErr_number).</summary>
    public const string NumberError = "number";
    /// <summary>Код ошибки: не выбрана строка набора (ключ LoadHistoryErr_force_item).</summary>
    public const string ForceItemError = "force_item";

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    readonly IReadOnlyList<ForceSet> _forceSets;
    readonly Dictionary<int, (PartLimitRole Concrete, PartLimitRole Rebar)> _roles = [];
    StagedSection? _section;
    LoadHistoryStageRowVM? _selectedStage;
    bool _hasFailure = true, _compare = true, _recalc;
    int _failureSteps = 50;
    string _failureMx = "-1", _failureMy = "0", _dropRatio = "0.85", _cMax = "", _softening = "0.0005";
    LoadStepFallback _fallback = LoadStepFallback.CurvatureControl;

    public LoadHistoryParamsVM(IReadOnlyList<ForceSet> forceSets)
    {
        _forceSets = forceSets;
        AddStageCommand = new RelayCommand(_ => AddStage());
        RemoveStageCommand = new RelayCommand(_ => RemoveStage(), _ => SelectedStage != null);
        MoveUpCommand = new RelayCommand(_ => MoveStage(-1), _ => SelectedStage != null && Stages.IndexOf(SelectedStage) > 0);
        MoveDownCommand = new RelayCommand(_ => MoveStage(+1),
            _ => SelectedStage != null && Stages.IndexOf(SelectedStage) < Stages.Count - 1);
    }

    /// <summary>Этапы по нагрузке.</summary>
    public ObservableCollection<LoadHistoryStageRowVM> Stages { get; } = [];
    /// <summary>Роли частей выбранного сечения.</summary>
    public ObservableCollection<PartRoleRowVM> Roles { get; } = [];

    /// <summary>Варианты роли для выпадающих списков.</summary>
    public IReadOnlyList<PartRoleChoice> RoleChoices { get; } =
    [
        new(PartLimitRole.Limits, Loc.S("LoadHistory_RoleLimits")),
        new(PartLimitRole.Ignore, Loc.S("LoadHistory_RoleIgnore")),
        new(PartLimitRole.Fails, Loc.S("LoadHistory_RoleFails")),
    ];

    /// <summary>Выбранный этап (редактор усилия под таблицей).</summary>
    public LoadHistoryStageRowVM? SelectedStage
    {
        get => _selectedStage;
        set { _selectedStage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelectedStage)); }
    }

    /// <summary>Есть выбранный этап.</summary>
    public bool HasSelectedStage => _selectedStage != null;

    /// <summary>Последний этап — доведение до разрушения по кривизне.</summary>
    public bool HasFailure { get => _hasFailure; set { _hasFailure = value; OnPropertyChanged(); } }
    /// <summary>Mx направления изгиба этапа разрушения.</summary>
    public string FailureMx { get => _failureMx; set { _failureMx = value; OnPropertyChanged(); } }
    /// <summary>My направления изгиба этапа разрушения.</summary>
    public string FailureMy { get => _failureMy; set { _failureMy = value; OnPropertyChanged(); } }
    /// <summary>Число шагов по кривизне.</summary>
    public int FailureSteps { get => _failureSteps; set { _failureSteps = value; OnPropertyChanged(); } }
    /// <summary>Доля пикового момента, при падении ниже которой расчёт останавливается.</summary>
    public string DropRatio { get => _dropRatio; set { _dropRatio = value; OnPropertyChanged(); } }
    /// <summary>Предельная кривизна, 1/м; пусто — авто.</summary>
    public string CMax { get => _cMax; set { _cMax = value; OnPropertyChanged(); } }
    /// <summary>Длина спада Δε для роли «Разрушается».</summary>
    public string SofteningStrain { get => _softening; set { _softening = value; OnPropertyChanged(); } }

    /// <summary>Считать путь «без истории».</summary>
    public bool CompareWithoutHistory { get => _compare; set { _compare = value; OnPropertyChanged(); } }

    /// <summary>Продолжение несошедшегося шага по нагрузке.</summary>
    public LoadStepFallback Fallback
    {
        get => _fallback;
        set
        {
            _fallback = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FallbackCurvature));
            OnPropertyChanged(nameof(FallbackNoTension));
        }
    }

    /// <summary>Продолжение по кривизне (для переключателя).</summary>
    public bool FallbackCurvature
    {
        get => _fallback == LoadStepFallback.CurvatureControl;
        set { if (value) Fallback = LoadStepFallback.CurvatureControl; }
    }

    /// <summary>Продолжение без растяжения бетона (для переключателя).</summary>
    public bool FallbackNoTension
    {
        get => _fallback == LoadStepFallback.WithoutConcreteTension;
        set { if (value) Fallback = LoadStepFallback.WithoutConcreteTension; }
    }

    public ICommand AddStageCommand { get; }
    public ICommand RemoveStageCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    int PartCount => _section?.Parts.Count ?? 0;

    /// <summary>Сечение задачи: пересобирает роли (заданные сохраняются по индексу) и выбор частей.</summary>
    public void SetSection(StagedSection? section)
    {
        StoreRoles();
        _section = section;
        Roles.Clear();
        if (section != null)
            foreach (var part in section.Parts)
            {
                var (c, r) = _roles.TryGetValue(part.Index, out var v) ? v : (PartLimitRole.Limits, PartLimitRole.Limits);
                Roles.Add(new PartRoleRowVM { PartIndex = part.Index, PartTag = part.Tag, Concrete = c, Rebar = r });
            }
        RecalcPartChoices();
    }

    /// <summary>Заполняет форму параметрами задачи.</summary>
    public void Load(LoadHistoryParams p)
    {
        Stages.Clear();
        HasFailure = false;
        foreach (var st in p.Stages)
        {
            if (st.Kind == StageKind.Failure)
            {
                HasFailure = true;
                FailureMx = st.Force.Mx.ToString("G6", Inv);
                FailureMy = st.Force.My.ToString("G6", Inv);
                continue;
            }
            var row = NewRow();
            row.Tag = st.Tag;
            row.Steps = st.Steps;
            if (st.Force.Mode == "manual")
            {
                row.UseManual = true;
                row.N = st.Force.N.ToString("G6", Inv);
                row.Mx = st.Force.Mx.ToString("G6", Inv);
                row.My = st.Force.My.ToString("G6", Inv);
            }
            else
            {
                row.UseManual = false;
                row.Set = _forceSets.FirstOrDefault(f => f.Id == st.Force.ForceSetId);
                row.Item = row.Set?.Items.FirstOrDefault(i => i.Id == st.Force.ForceItemId);
            }
            Stages.Add(row);
            row.ActivatePart = st.ActivatePart;
        }
        FailureSteps = p.Failure.Steps;
        DropRatio = p.Failure.DropRatio.ToString("G6", Inv);
        CMax = p.Failure.CMax?.ToString("G6", Inv) ?? "";
        SofteningStrain = p.Failure.SofteningStrain.ToString("G6", Inv);
        CompareWithoutHistory = p.CompareWithoutHistory;
        Fallback = p.Fallback;

        _roles.Clear();
        foreach (var r in p.PartRoles) _roles[r.PartIndex] = (r.Concrete, r.Rebar);
        foreach (var row in Roles)
            (row.Concrete, row.Rebar) = _roles.TryGetValue(row.PartIndex, out var v) ? v : (PartLimitRole.Limits, PartLimitRole.Limits);
        RecalcPartChoices();
        SelectedStage = Stages.FirstOrDefault();
    }

    /// <summary>Собирает параметры; <paramref name="errors"/> — коды ошибок (ключи LoadHistoryErr_&lt;код&gt;).</summary>
    public LoadHistoryParams Build(out IReadOnlyList<string> errors)
    {
        var list = new List<string>();
        void Add(string code) { if (!list.Contains(code)) list.Add(code); }
        double Num(string text)
        {
            if (double.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, Inv, out var v) && double.IsFinite(v))
                return v;
            Add(NumberError);
            return 0;
        }

        var p = new LoadHistoryParams
        {
            CompareWithoutHistory = CompareWithoutHistory,
            Fallback = Fallback,
            Failure = new FailureOptions
            {
                Steps = FailureSteps,
                DropRatio = Num(DropRatio),
                CMax = string.IsNullOrWhiteSpace(CMax) ? null : Num(CMax),
                SofteningStrain = Num(SofteningStrain),
            },
        };
        foreach (var row in Stages)
        {
            StageForce force;
            if (row.UseManual)
                force = new StageForce { Mode = "manual", N = Num(row.N), Mx = Num(row.Mx), My = Num(row.My) };
            else
            {
                if (row.Set == null || row.Item == null) Add(ForceItemError);
                force = new StageForce { Mode = "item", ForceSetId = row.Set?.Id ?? 0, ForceItemId = row.Item?.Id ?? 0 };
            }
            p.Stages.Add(new LoadHistoryStageParams
            {
                Tag = row.Tag, Kind = StageKind.Load, ActivatePart = row.ActivatePart, Force = force, Steps = row.Steps,
            });
        }
        if (HasFailure)
            p.Stages.Add(new LoadHistoryStageParams
            {
                Tag = "failure", Kind = StageKind.Failure, Steps = 1,
                Force = new StageForce { Mode = "manual", Mx = Num(FailureMx), My = Num(FailureMy) },
            });

        StoreRoles();
        foreach (var (index, (c, r)) in _roles.OrderBy(kv => kv.Key))
            if (index < PartCount && (c != PartLimitRole.Limits || r != PartLimitRole.Limits))
                p.PartRoles.Add(new PartRolesParams { PartIndex = index, Concrete = c, Rebar = r });

        foreach (var code in p.Validate(PartCount)) Add(code);
        errors = list;
        return p;
    }

    LoadHistoryStageRowVM NewRow() => new(_forceSets, RecalcPartChoices) { Set = _forceSets.FirstOrDefault() };

    void AddStage()
    {
        var row = NewRow();
        row.Tag = (Stages.Count + 1).ToString(Inv);
        int at = SelectedStage == null ? Stages.Count : Stages.IndexOf(SelectedStage) + 1;
        Stages.Insert(at, row);
        SelectedStage = row;
        RecalcPartChoices();
    }

    void RemoveStage()
    {
        if (SelectedStage == null) return;
        int i = Stages.IndexOf(SelectedStage);
        Stages.RemoveAt(i);
        SelectedStage = Stages.Count == 0 ? null : Stages[Math.Min(i, Stages.Count - 1)];
        RecalcPartChoices();
    }

    void MoveStage(int delta)
    {
        if (SelectedStage == null) return;
        int i = Stages.IndexOf(SelectedStage), j = i + delta;
        if (j < 0 || j >= Stages.Count) return;
        var row = SelectedStage;
        Stages.Move(i, j);
        SelectedStage = row;
        RecalcPartChoices();
    }

    /// <summary>Пересчитывает выбор частей: строка видит части ≥ 1, не включённые предыдущими строками.</summary>
    void RecalcPartChoices()
    {
        if (_recalc) return;
        _recalc = true;
        try
        {
            var used = new HashSet<int>();
            foreach (var row in Stages)
            {
                var choices = new List<int?> { null };
                for (int k = 1; k < PartCount; k++)
                    if (!used.Contains(k)) choices.Add(k);
                row.PartChoices = choices;
                // Недопустимый выбор не сбрасывается (сечение могло ещё не быть задано) — его отметит Validate.
                if (row.ActivatePart is int p) used.Add(p);
            }
        }
        finally { _recalc = false; }
    }

    void StoreRoles()
    {
        foreach (var row in Roles) _roles[row.PartIndex] = (row.Concrete, row.Rebar);
    }
}
