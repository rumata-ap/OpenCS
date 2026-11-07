using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CScore.En1993;
using CScore.Fem.Imperfections;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Подписи элементов несовершенства для дерева и диалога.</summary>
public static class FemImperfectionText
{
    /// <summary>Сводка загружения-несовершенства: «крен 1, погибей 3»; null — элементов нет.</summary>
    public static string? CaseSummary(IReadOnlyCollection<FemImperfection> items)
    {
        if (items.Count == 0) return Loc.S("FemImperfectionCaseEmpty");
        int sways = items.Count(i => i.Kind == FemImperfectionKinds.Sway), bows = items.Count(i => i.Kind == FemImperfectionKinds.Bow);
        int modes = items.Count(i => i.Kind == FemImperfectionKinds.Modes);
        string text = string.Format(CultureInfo.CurrentCulture, Loc.S("FemImperfectionCaseSummary"), sways, bows);
        return modes == 0 ? text : text + string.Format(CultureInfo.CurrentCulture, Loc.S("FemImperfectionCaseSummaryModes"), modes);
    }

    /// <summary>Сводка элемента серым: охват и способ задания φ (крен) или число элементов и e₀ (погибь).</summary>
    public static string Summary(FemImperfection item)
    {
        var c = CultureInfo.CurrentCulture;
        switch (item.Kind)
        {
            case FemImperfectionKinds.Sway:
            {
                var p = FemSwayParams.Parse(item.ParamsJson);
                string scope = p.Scope == FemSwayParams.ScopeAll ? Loc.S("FemImperfectionScopeAllShort")
                    : string.Format(c, Loc.S("FemImperfectionScopeListShort"), p.MemberTags.Count, p.ElementTags.Count);
                string phi = p.PhiMode == FemSwayParams.PhiEn1993
                    ? string.Format(c, Loc.S("FemImperfectionPhiNormShort"), p.M)
                    : string.Format(c, "1/φ = {0:0.#}", p.PhiInv ?? 0);
                return string.Format(c, "{0}; {1}; {2:0.#}°", scope, phi, p.AngleDeg);
            }
            case FemImperfectionKinds.Bow:
            {
                var p = FemBowParams.Parse(item.ParamsJson);
                string targets = string.Format(c, Loc.S("FemImperfectionBowTargetsShort"), p.MemberTags.Count, p.ElementChains.Count);
                string e0 = p.E0Mode switch
                {
                    FemBowParams.E0Table => string.Format(c, Loc.S("FemImperfectionE0TableShort"),
                        p.Curve == FemBowParams.CurveAuto ? Loc.S("FemImperfectionCurveAuto") : p.Curve),
                    FemBowParams.E0Absolute => string.Format(c, "e₀ = {0:0.##} {1}", p.E0Mm ?? 0, Loc.S("UnitMm")),
                    _ => string.Format(c, "L/e₀ = {0:0.#}", p.LOverE0 ?? 0),
                };
                return $"{targets}; {p.Plane}; {e0}";
            }
            case FemImperfectionKinds.Modes:
            {
                var p = FemModesParams.Parse(item.ParamsJson);
                return string.Format(c, Loc.S("FemImperfectionModesShort"), p.CaseIndex + 1,
                    string.Join(", ", p.Modes.Select(m => string.Format(c, "{0}: {1:0.##}", m.Mode, m.AmplitudeMm))));
            }
            default:
                return item.Kind;
        }
    }
}

/// <summary>
/// Диалог элемента несовершенства (по образцу RFEM «Задать или изменить несовершенство», спека 4.2): имя, вид (крен |
/// погибь), охват (вся схема | перечень КонЭ и КЭ) и выбор из выделения 3D, направление (угол крена в плане, местная ось
/// погиби), норма EN 1993-1-1:2005+AC:2009, φ по (5.5) с кнопкой хода вычисления или 1/φ вручную, e₀ по табл. 5.1 (кривая
/// авто/a0…d, столбец упругий/пластический), L/e₀ или мм вручную, знак, критерий активности, комментарий.
/// Результат — <see cref="TryBuild"/>.
/// </summary>
public sealed class FemImperfectionDialogVM : ViewModelBase
{
    readonly FemImperfection _source;
    readonly Func<FemSwayParams, double?>? _spanZ;

    /// <param name="item">Правимый элемент (Id 0 — новый); вид и параметры — начальные значения полей.</param>
    /// <param name="spanZ">Размах z охвата крена, м (h по умолчанию для (5.5)); null или null-ответ — не определён.</param>
    /// <param name="mHint">Подсказка m по последнему расчёту (стойки с N_Ed больше 50 % средней); null — нет.</param>
    /// <param name="selection">Выделение 3D редактора схемы: КонЭ и КЭ сетки; null — редактор не открыт.</param>
    /// <param name="bucklingSources">Постановки устойчивости схемы для вида «Формы» (состояние форм, задачи, λ).</param>
    public FemImperfectionDialogVM(FemImperfection item, Func<FemSwayParams, double?>? spanZ = null, int? mHint = null,
        Func<(IReadOnlyCollection<string> Members, IReadOnlyCollection<string> Elements)>? selection = null,
        IReadOnlyList<FemBucklingSourceChoice>? bucklingSources = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        _source = item;
        _spanZ = spanZ;
        _selection = selection;
        MHint = mHint;
        BucklingSources = bucklingSources ?? [];
        _kind = item.Kind is FemImperfectionKinds.Bow or FemImperfectionKinds.Modes ? item.Kind : FemImperfectionKinds.Sway;
        _tag = item.Tag;
        _comment = item.Comment ?? "";
        var c = CultureInfo.CurrentCulture;
        if (_kind == FemImperfectionKinds.Sway)
        {
            var p = FemSwayParams.Parse(item.ParamsJson);
            _scopeAll = p.Scope != FemSwayParams.ScopeList;
            _membersText = Join(p.MemberTags);
            _elementsText = Join(p.ElementTags);
            _angleText = p.AngleDeg.ToString("0.###", c);
            _phiNorm = p.PhiMode == FemSwayParams.PhiEn1993;
            _hText = p.HeightM?.ToString("0.###", c) ?? "";
            _mText = p.M.ToString(c);
            _phiInvText = p.PhiInv?.ToString("0.#", c) ?? "200";
        }
        else if (_kind == FemImperfectionKinds.Modes)
        {
            var p = FemModesParams.Parse(item.ParamsJson);
            _bucklingSource = BucklingSources.FirstOrDefault(s => s.AnalysisId == p.AnalysisId) ?? BucklingSources.FirstOrDefault();
            _bucklingCase = _bucklingSource?.Cases.FirstOrDefault(k => k.Index == p.CaseIndex) ?? _bucklingSource?.Cases.FirstOrDefault();
            foreach (var m in p.Modes) ModeRows.Add(new FemModeAmplitudeRow(m.Mode.ToString(c), m.AmplitudeMm.ToString("0.##", c)));
        }
        else
        {
            var p = FemBowParams.Parse(item.ParamsJson);
            _membersText = Join(p.MemberTags);
            _chainsText = string.Join("; ", p.ElementChains.Select(Join));
            _plane = p.Plane == FemBowParams.PlaneZ ? FemBowParams.PlaneZ : FemBowParams.PlaneY;
            _e0Mode = p.E0Mode is FemBowParams.E0Table or FemBowParams.E0Absolute ? p.E0Mode : FemBowParams.E0Relative;
            _curve = Curves.Any(k => k.Key == p.Curve) ? p.Curve : FemBowParams.CurveAuto;
            _plastic = p.Plastic;
            _lOverE0Text = p.LOverE0?.ToString("0.#", c) ?? "";
            _e0MmText = p.E0Mm?.ToString("0.##", c) ?? "";
            _sign = p.Sign is FemBowParams.SignPlus or FemBowParams.SignMinus ? p.Sign : FemBowParams.SignAuto;
            _activity = p.Activity == FemBowParams.ActivityAlways ? FemBowParams.ActivityAlways : FemBowParams.ActivityEn1993;
        }
        SetAngleCommand = new RelayCommand(a => { if (a is string s) AngleText = s; });
        ComputePhiCommand = new RelayCommand(_ => ComputePhi());
        TakeSelectionCommand = new RelayCommand(_ => TakeSelection(), _ => _selection != null);
        AddModeCommand = new RelayCommand(_ => AddMode());
        RemoveModeCommand = new RelayCommand(r => { if (r is FemModeAmplitudeRow row) ModeRows.Remove(row); });
        if (_kind == FemImperfectionKinds.Modes && ModeRows.Count == 0) AddMode();
    }

    // ------------------------------------------------------------ общее

    public bool IsNew => _source.Id == 0;

    string _kind;
    /// <summary><see cref="FemImperfectionKinds.Sway"/> | <see cref="FemImperfectionKinds.Bow"/>.</summary>
    public string Kind
    {
        get => _kind;
        set
        {
            if (value is not (FemImperfectionKinds.Sway or FemImperfectionKinds.Bow or FemImperfectionKinds.Modes) || value == _kind) return;
            _kind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSway));
            OnPropertyChanged(nameof(IsBow));
            OnPropertyChanged(nameof(IsModes));
            OnPropertyChanged(nameof(HasTargets));
            OnPropertyChanged(nameof(IsListScope));
            OnPropertyChanged(nameof(Title));
            if (IsModes)
            {
                if (_bucklingSource == null) BucklingSource = BucklingSources.FirstOrDefault();
                if (ModeRows.Count == 0) AddMode();
            }
        }
    }

    public bool IsSway { get => _kind == FemImperfectionKinds.Sway; set { if (value) Kind = FemImperfectionKinds.Sway; } }
    public bool IsBow { get => _kind == FemImperfectionKinds.Bow; set { if (value) Kind = FemImperfectionKinds.Bow; } }
    public bool IsModes { get => _kind == FemImperfectionKinds.Modes; set { if (value) Kind = FemImperfectionKinds.Modes; } }

    /// <summary>Блок «Относительно» (охват крена, элементы погиби) виден — не у форм.</summary>
    public bool HasTargets => !IsModes;

    public string Title => Loc.S(IsSway ? "FemImperfectionDialogTitleSway" : IsBow ? "FemImperfectionDialogTitleBow" : "FemImperfectionDialogTitleModes");

    /// <summary>Норма (одна): EN 1993-1-1:2005+AC:2009.</summary>
    public static string NormText => "EN 1993-1-1:2005+AC:2009";

    string _tag;
    public string Tag { get => _tag; set { _tag = value ?? ""; OnPropertyChanged(); } }

    string _comment;
    public string Comment { get => _comment; set { _comment = value ?? ""; OnPropertyChanged(); } }

    // ------------------------------------------------------------ охват и выбор

    bool _scopeAll = true;
    /// <summary>Крен: вся схема (иначе перечень).</summary>
    public bool ScopeAll
    {
        get => _scopeAll;
        set
        {
            if (_scopeAll == value) return;
            _scopeAll = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScopeList));
            OnPropertyChanged(nameof(IsListScope));
        }
    }

    public bool ScopeList { get => !_scopeAll; set => ScopeAll = !value; }

    /// <summary>Поля перечня видны: погибь или крен по перечню.</summary>
    public bool IsListScope => IsBow || (IsSway && !_scopeAll);

    string _membersText = "";
    /// <summary>Теги КонЭ через запятую или пробел.</summary>
    public string MembersText { get => _membersText; set { _membersText = value ?? ""; OnPropertyChanged(); } }

    string _elementsText = "";
    /// <summary>Крен: теги КЭ сетки (диапазоны «10-20»).</summary>
    public string ElementsText { get => _elementsText; set { _elementsText = value ?? ""; OnPropertyChanged(); } }

    string _chainsText = "";
    /// <summary>Погибь: цепочки КЭ сетки через «;» (каждая — своя погибь), теги в цепочке — через запятую или пробел.</summary>
    public string ChainsText { get => _chainsText; set { _chainsText = value ?? ""; OnPropertyChanged(); } }

    readonly Func<(IReadOnlyCollection<string> Members, IReadOnlyCollection<string> Elements)>? _selection;

    /// <summary>Выделение 3D доступно (открыт редактор схемы).</summary>
    public bool CanTakeSelection => _selection != null;

    /// <summary>«Из выделения 3D»: КонЭ — в перечень КонЭ; КЭ — в перечень КЭ (крен) или одной цепочкой (погибь).</summary>
    public ICommand TakeSelectionCommand { get; }

    string _selectionNote = "";
    public string SelectionNote { get => _selectionNote; private set { _selectionNote = value; OnPropertyChanged(); } }

    void TakeSelection()
    {
        if (_selection == null) return;
        var (members, elements) = _selection();
        if (members.Count + elements.Count == 0)
        {
            SelectionNote = Loc.S("FemImperfectionSelectionEmpty");
            return;
        }
        MembersText = Join(members);
        if (IsSway)
        {
            ElementsText = Join(elements);
            ScopeAll = false;
        }
        else ChainsText = elements.Count > 0 ? Join(elements) : "";
        SelectionNote = string.Format(CultureInfo.CurrentCulture, Loc.S("FemImperfectionSelectionTaken"), members.Count, elements.Count);
    }

    // ------------------------------------------------------------ крен

    string _angleText = "0";
    /// <summary>Направление крена в плане — угол от +X против часовой стрелки, град.</summary>
    public string AngleText { get => _angleText; set { _angleText = value ?? ""; OnPropertyChanged(); } }

    /// <summary>Кнопки ±X, ±Y: параметр — угол «0», «90», «180», «270».</summary>
    public ICommand SetAngleCommand { get; }

    bool _phiNorm = true;
    /// <summary>φ по EN 1993-1-1 (5.5) (иначе 1/φ вручную).</summary>
    public bool PhiNorm
    {
        get => _phiNorm;
        set { if (_phiNorm == value) return; _phiNorm = value; OnPropertyChanged(); OnPropertyChanged(nameof(PhiManual)); }
    }

    public bool PhiManual { get => !_phiNorm; set => PhiNorm = !value; }

    string _hText = "";
    /// <summary>h по (5.5), м; пусто — размах z охвата.</summary>
    public string HText { get => _hText; set { _hText = value ?? ""; OnPropertyChanged(); } }

    string _mText = "1";
    /// <summary>m по (5.5) — число колонн в ряду (по умолчанию 1 — в запас).</summary>
    public string MText { get => _mText; set { _mText = value ?? ""; OnPropertyChanged(); } }

    string _phiInvText = "200";
    /// <summary>1/φ вручную.</summary>
    public string PhiInvText { get => _phiInvText; set { _phiInvText = value ?? ""; OnPropertyChanged(); } }

    /// <summary>Подсказка m по последнему расчёту; null — нет.</summary>
    public int? MHint { get; }

    public string MHintText => MHint is { } m ? string.Format(CultureInfo.CurrentCulture, Loc.S("FemImperfectionMHint"), m)
        : Loc.S("FemImperfectionMHintNone");

    /// <summary>«по (5.5)»: ход вычисления φ по h и m полей (пустое h — размах z охвата); в режиме «вручную» — 1/φ в поле.</summary>
    public ICommand ComputePhiCommand { get; }

    string _phiDescription = "";
    /// <summary>Ход вычисления φ по (5.5) или почему не посчитан.</summary>
    public string PhiDescription { get => _phiDescription; private set { _phiDescription = value; OnPropertyChanged(); } }

    /// <summary>Считает φ по (5.5) по полям h и m; при «1/φ вручную» подставляет 1/φ. false — данных не хватает (текст — почему).</summary>
    public bool ComputePhi()
    {
        var c = CultureInfo.CurrentCulture;
        if (!int.TryParse(MText.Trim(), NumberStyles.Integer, c, out int m) || m < 1)
        {
            PhiDescription = Loc.S("FemImperfectionBadM");
            return false;
        }
        double h;
        string source;
        if (HText.Trim().Length > 0)
        {
            if (!Pars.ParseAny(HText.Trim(), out h) || !double.IsFinite(h) || h <= 0)
            {
                PhiDescription = Loc.S("FemImperfectionBadH");
                return false;
            }
            source = Loc.S("FemImperfectionHManual");
        }
        else
        {
            var p = BuildSway(out _);
            if (p == null || _spanZ?.Invoke(p) is not { } span || !(span > 1e-9))
            {
                PhiDescription = Loc.S("FemImperfectionHUnknown");
                return false;
            }
            h = span;
            source = string.Format(c, Loc.S("FemImperfectionHSpan"), span);
        }
        var phi = En1993Imperfections.Phi(h, m);
        PhiDescription = $"{source}: {phi.Describe()}";
        if (PhiManual) PhiInvText = (1 / phi.Phi).ToString("0.#", c);
        return true;
    }

    // ------------------------------------------------------------ погибь

    public static IReadOnlyList<FemLoadsChoice> Planes { get; } =
    [
        new(FemBowParams.PlaneY, Loc.S("FemImperfectionPlaneY")),
        new(FemBowParams.PlaneZ, Loc.S("FemImperfectionPlaneZ")),
    ];

    public static IReadOnlyList<FemLoadsChoice> E0Modes { get; } =
    [
        new(FemBowParams.E0Table, Loc.S("FemImperfectionE0Table")),
        new(FemBowParams.E0Relative, Loc.S("FemImperfectionE0Relative")),
        new(FemBowParams.E0Absolute, Loc.S("FemImperfectionE0Absolute")),
    ];

    public static IReadOnlyList<FemLoadsChoice> Curves { get; } =
    [
        new(FemBowParams.CurveAuto, Loc.S("FemImperfectionCurveAutoLong")),
        new("a0", "a₀"), new("a", "a"), new("b", "b"), new("c", "c"), new("d", "d"),
    ];

    public static IReadOnlyList<FemLoadsChoice> Signs { get; } =
    [
        new(FemBowParams.SignAuto, Loc.S("FemImperfectionSignAuto")),
        new(FemBowParams.SignPlus, "+"),
        new(FemBowParams.SignMinus, "−"),
    ];

    public static IReadOnlyList<FemLoadsChoice> Activities { get; } =
    [
        new(FemBowParams.ActivityEn1993, Loc.S("FemImperfectionActivityEn1993")),
        new(FemBowParams.ActivityAlways, Loc.S("FemImperfectionActivityAlways")),
    ];

    string _plane = FemBowParams.PlaneY;
    public string Plane { get => _plane; set { if (value != null) { _plane = value; OnPropertyChanged(); } } }

    string _e0Mode = FemBowParams.E0Table;
    public string E0Mode
    {
        get => _e0Mode;
        set
        {
            if (value == null || value == _e0Mode) return;
            _e0Mode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsE0Table));
            OnPropertyChanged(nameof(IsE0Relative));
            OnPropertyChanged(nameof(IsE0Absolute));
            OnPropertyChanged(nameof(TableValueText));
        }
    }

    public bool IsE0Table => _e0Mode == FemBowParams.E0Table;
    public bool IsE0Relative => _e0Mode == FemBowParams.E0Relative;
    public bool IsE0Absolute => _e0Mode == FemBowParams.E0Absolute;

    string _curve = FemBowParams.CurveAuto;
    public string Curve
    {
        get => _curve;
        set { if (value == null) return; _curve = value; OnPropertyChanged(); OnPropertyChanged(nameof(TableValueText)); }
    }

    bool _plastic;
    /// <summary>Столбец табл. 5.1: пластический расчёт (иначе упругий).</summary>
    public bool Plastic
    {
        get => _plastic;
        set { if (_plastic == value) return; _plastic = value; OnPropertyChanged(); OnPropertyChanged(nameof(Elastic)); OnPropertyChanged(nameof(TableValueText)); }
    }

    public bool Elastic { get => !_plastic; set => Plastic = !value; }

    /// <summary>L/e₀ табл. 5.1 для выбранной кривой и столбца; при «авто» — пояснение (кривая по сечению при расчёте).</summary>
    public string TableValueText => En1993Imperfections.ParseCurve(_curve) is { } curve
        ? string.Format(CultureInfo.CurrentCulture, Loc.S("FemImperfectionTableValue"), En1993Imperfections.LOverE0(curve, _plastic))
        : Loc.S("FemImperfectionTableAuto");

    string _lOverE0Text = "";
    public string LOverE0Text { get => _lOverE0Text; set { _lOverE0Text = value ?? ""; OnPropertyChanged(); } }

    string _e0MmText = "";
    public string E0MmText { get => _e0MmText; set { _e0MmText = value ?? ""; OnPropertyChanged(); } }

    string _sign = FemBowParams.SignAuto;
    public string Sign { get => _sign; set { if (value != null) { _sign = value; OnPropertyChanged(); } } }

    string _activity = FemBowParams.ActivityEn1993;
    public string Activity { get => _activity; set { if (value != null) { _activity = value; OnPropertyChanged(); } } }

    // ------------------------------------------------------------ формы потери устойчивости

    /// <summary>Постановки «Расчётные длины — CSfea» схемы.</summary>
    public IReadOnlyList<FemBucklingSourceChoice> BucklingSources { get; }

    public bool HasBucklingSources => BucklingSources.Count > 0;

    FemBucklingSourceChoice? _bucklingSource;
    public FemBucklingSourceChoice? BucklingSource
    {
        get => _bucklingSource;
        set
        {
            if (value == _bucklingSource) return;
            _bucklingSource = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BucklingCases));
            OnPropertyChanged(nameof(BucklingStatus));
            BucklingCase = value?.Cases.FirstOrDefault();
        }
    }

    /// <summary>Задачи выбранной постановки (сочетания или огибающая).</summary>
    public IReadOnlyList<FemBucklingCaseChoice> BucklingCases => _bucklingSource?.Cases ?? [];

    FemBucklingCaseChoice? _bucklingCase;
    public FemBucklingCaseChoice? BucklingCase
    {
        get => _bucklingCase;
        set
        {
            if (value == _bucklingCase) return;
            _bucklingCase = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LambdaText));
        }
    }

    /// <summary>Состояние форм постановки: не посчитана / устарела / нет постановок; пусто — годятся.</summary>
    public string BucklingStatus => !HasBucklingSources ? Loc.S("FemImperfectionModesNoSources") : _bucklingSource?.Status ?? "";

    /// <summary>λ форм выбранной задачи: «1: 3,21; 2: 5,4; …».</summary>
    public string LambdaText => _bucklingCase is { Lambda.Count: > 0 } k
        ? string.Format(CultureInfo.CurrentCulture, Loc.S("FemImperfectionModesLambda"),
            string.Join("; ", k.Lambda.Take(12).Select((l, i) => string.Format(CultureInfo.CurrentCulture, "{0}: {1:0.###}", i + 1, l)))
            + (k.Lambda.Count > 12 ? "; …" : ""))
        : "";

    /// <summary>Формы и амплитуды (мм со знаком).</summary>
    public ObservableCollection<FemModeAmplitudeRow> ModeRows { get; } = [];

    public ICommand AddModeCommand { get; }
    public ICommand RemoveModeCommand { get; }

    void AddMode()
    {
        var c = CultureInfo.CurrentCulture;
        int next = ModeRows.Select(r => int.TryParse(r.ModeText, NumberStyles.Integer, c, out int m) ? m : 0).DefaultIfEmpty(0).Max() + 1;
        ModeRows.Add(new FemModeAmplitudeRow(next.ToString(c), ""));
    }

    FemModesParams? BuildModes(out string? error)
    {
        error = null;
        if (_bucklingSource == null) { error = Loc.S("FemImperfectionModesNoSource"); return null; }
        if (_bucklingCase == null) { error = Loc.S("FemImperfectionModesNoCase"); return null; }
        var c = CultureInfo.CurrentCulture;
        var modes = new List<FemModeAmplitude>();
        foreach (var r in ModeRows)
        {
            if (string.IsNullOrWhiteSpace(r.ModeText) && string.IsNullOrWhiteSpace(r.AmplitudeText)) continue;
            if (!int.TryParse(r.ModeText.Trim(), NumberStyles.Integer, c, out int mode) || mode < 1
                || (_bucklingCase.Lambda.Count > 0 && mode > _bucklingCase.Lambda.Count))
            {
                error = string.Format(c, Loc.S("FemImperfectionModesBadMode"), r.ModeText, _bucklingCase.Lambda.Count);
                return null;
            }
            if (!Pars.ParseAny(r.AmplitudeText.Trim(), out double a) || !double.IsFinite(a) || a == 0)
            {
                error = string.Format(c, Loc.S("FemImperfectionModesBadAmplitude"), mode);
                return null;
            }
            if (modes.Any(m => m.Mode == mode)) { error = string.Format(c, Loc.S("FemImperfectionModesDuplicate"), mode); return null; }
            modes.Add(new FemModeAmplitude { Mode = mode, AmplitudeMm = a });
        }
        if (modes.Count == 0) { error = Loc.S("FemImperfectionModesEmpty"); return null; }
        return new FemModesParams { AnalysisId = _bucklingSource.AnalysisId, CaseIndex = _bucklingCase.Index, Modes = modes };
    }

    // ------------------------------------------------------------ результат

    /// <summary>
    /// Элемент несовершенства по полям (Id, схема и загружение — от исходного); null и <paramref name="error"/> — что
    /// исправить.
    /// </summary>
    public FemImperfection? TryBuild(out string? error)
    {
        string json;
        if (IsSway)
        {
            if (BuildSway(out error) is not { } p) return null;
            json = p.ToJson();
        }
        else if (IsModes)
        {
            if (BuildModes(out error) is not { } p) return null;
            json = p.ToJson();
        }
        else
        {
            if (BuildBow(out error) is not { } p) return null;
            json = p.ToJson();
        }
        var result = _source.Clone();
        result.Kind = _kind;
        result.Tag = Tag.Trim();
        result.Comment = string.IsNullOrWhiteSpace(Comment) ? null : Comment.Trim();
        result.ParamsJson = json;
        return result;
    }

    FemSwayParams? BuildSway(out string? error)
    {
        error = null;
        var members = SplitTags(MembersText);
        var elements = ExpandTags(SplitTags(ElementsText));
        if (!_scopeAll && members.Count + elements.Count == 0) { error = Loc.S("FemImperfectionScopeEmpty"); return null; }
        if (!Pars.ParseAny(AngleText.Trim(), out double angle) || !double.IsFinite(angle)) { error = Loc.S("FemImperfectionBadAngle"); return null; }
        double? h = null, phiInv = null;
        int m = 1;
        if (_phiNorm)
        {
            if (!int.TryParse(MText.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out m) || m < 1)
            {
                error = Loc.S("FemImperfectionBadM");
                return null;
            }
            if (HText.Trim().Length > 0)
            {
                if (!Pars.ParseAny(HText.Trim(), out double hv) || !double.IsFinite(hv) || hv <= 0) { error = Loc.S("FemImperfectionBadH"); return null; }
                h = hv;
            }
        }
        else
        {
            if (!Pars.ParseAny(PhiInvText.Trim(), out double inv) || !double.IsFinite(inv) || inv <= 0) { error = Loc.S("FemImperfectionBadPhiInv"); return null; }
            phiInv = inv;
        }
        return new FemSwayParams
        {
            Scope = _scopeAll ? FemSwayParams.ScopeAll : FemSwayParams.ScopeList,
            MemberTags = _scopeAll ? [] : members,
            ElementTags = _scopeAll ? [] : elements,
            AngleDeg = angle,
            PhiMode = _phiNorm ? FemSwayParams.PhiEn1993 : FemSwayParams.PhiManual,
            HeightM = h,
            M = _phiNorm ? m : 1,
            PhiInv = phiInv,
        };
    }

    FemBowParams? BuildBow(out string? error)
    {
        error = null;
        var members = SplitTags(MembersText);
        var chains = ChainsText.Split([';', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(ch => ExpandTags(SplitTags(ch))).Where(ch => ch.Count > 0).ToList();
        if (members.Count + chains.Count == 0) { error = Loc.S("FemImperfectionBowEmpty"); return null; }
        double? lOverE0 = null, e0Mm = null;
        if (IsE0Relative)
        {
            if (!Pars.ParseAny(LOverE0Text.Trim(), out double v) || !double.IsFinite(v) || v <= 0) { error = Loc.S("FemImperfectionBadLOverE0"); return null; }
            lOverE0 = v;
        }
        else if (IsE0Absolute)
        {
            if (!Pars.ParseAny(E0MmText.Trim(), out double v) || !double.IsFinite(v) || v <= 0) { error = Loc.S("FemImperfectionBadE0Mm"); return null; }
            e0Mm = v;
        }
        return new FemBowParams
        {
            MemberTags = members,
            ElementChains = chains,
            Plane = _plane,
            E0Mode = _e0Mode,
            Curve = _curve,
            Plastic = _plastic,
            LOverE0 = lOverE0,
            E0Mm = e0Mm,
            Sign = _sign,
            Activity = _activity,
        };
    }

    // ------------------------------------------------------------ теги

    static string Join(IEnumerable<string> tags) => string.Join(", ", tags);

    /// <summary>Теги через запятую, точку с запятой или пробелы, без повторов (порядок ввода).</summary>
    public static List<string> SplitTags(string text) =>
        (text ?? "").Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Диапазоны номеров КЭ «10-14» → 10, 11, 12, 13, 14 (не более 100 000 в диапазоне); прочее — как есть.</summary>
    public static List<string> ExpandTags(IEnumerable<string> tags)
    {
        var result = new List<string>();
        foreach (var t in tags)
        {
            int dash = t.IndexOf('-', 1);
            if (dash > 0 && int.TryParse(t[..dash], NumberStyles.None, CultureInfo.InvariantCulture, out int a)
                && int.TryParse(t[(dash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int b) && b >= a && b - a < 100_000)
                for (int i = a; i <= b; i++) result.Add(i.ToString(CultureInfo.InvariantCulture));
            else result.Add(t);
        }
        return result.Distinct(StringComparer.Ordinal).ToList();
    }
}

/// <summary>Строка таблицы форм диалога: номер формы и амплитуда, мм (текстом — проверка при OK).</summary>
public sealed class FemModeAmplitudeRow(string mode, string amplitude) : ViewModelBase
{
    string _mode = mode, _amplitude = amplitude;
    public string ModeText { get => _mode; set { _mode = value ?? ""; OnPropertyChanged(); } }
    public string AmplitudeText { get => _amplitude; set { _amplitude = value ?? ""; OnPropertyChanged(); } }
}
