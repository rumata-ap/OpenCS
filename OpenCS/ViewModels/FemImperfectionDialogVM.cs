using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CScore.En1992;
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
        int walls = items.Count(i => i.Kind == FemImperfectionKinds.WallBow);
        string text = string.Format(CultureInfo.CurrentCulture, Loc.S("FemImperfectionCaseSummary"), sways, bows);
        if (modes > 0) text += string.Format(CultureInfo.CurrentCulture, Loc.S("FemImperfectionCaseSummaryModes"), modes);
        if (walls > 0) text += string.Format(CultureInfo.CurrentCulture, Loc.S("FemImperfectionCaseSummaryWalls"), walls);
        return text;
    }

    /// <summary>Подпись способа задания e₀ (погибь и погибь стен).</summary>
    static string E0Short(string mode, double? lOverE0, double? e0Mm, double? mu, string curve)
    {
        var c = CultureInfo.CurrentCulture;
        return mode switch
        {
            FemBowParams.E0Table => string.Format(c, Loc.S("FemImperfectionE0TableShort"),
                curve == FemBowParams.CurveAuto ? Loc.S("FemImperfectionCurveAuto") : curve),
            FemBowParams.E0Absolute => string.Format(c, "e₀ = {0:0.##} {1}", e0Mm ?? 0, Loc.S("UnitMm")),
            FemBowParams.E0En1992 => string.Format(c, Loc.S("FemImperfectionE0En1992Short"), mu ?? 1),
            FemBowParams.E0En1992Simplified => string.Format(c, Loc.S("FemImperfectionE0En1992SimplifiedShort"), mu ?? 1),
            FemBowParams.E0Sp63 => Loc.S("FemImperfectionE0Sp63Short"),
            _ => string.Format(c, "L/e₀ = {0:0.#}", lOverE0 ?? 0),
        };
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
                    : p.PhiMode == FemSwayParams.PhiEn1992
                    ? string.Format(c, Loc.S("FemImperfectionThetaNormShort"), p.M)
                    : string.Format(c, "1/φ = {0:0.#}", p.PhiInv ?? 0);
                return string.Format(c, "{0}; {1}; {2:0.#}°", scope, phi, p.AngleDeg);
            }
            case FemImperfectionKinds.Bow:
            {
                var p = FemBowParams.Parse(item.ParamsJson);
                string targets = string.Format(c, Loc.S("FemImperfectionBowTargetsShort"), p.MemberTags.Count, p.ElementChains.Count);
                return $"{targets}; {p.Plane}; {E0Short(p.E0Mode, p.LOverE0, p.E0Mm, p.Mu, p.Curve)}";
            }
            case FemImperfectionKinds.WallBow:
            {
                var p = FemWallBowParams.Parse(item.ParamsJson);
                string targets = string.Format(c, Loc.S("FemImperfectionWallTargetsShort"), p.MemberTags.Count, p.ElementTags.Count);
                return $"{targets}; {E0Short(p.E0Mode, p.LOverE0, p.E0Mm, p.Mu, FemBowParams.CurveAuto)}";
            }
            case FemImperfectionKinds.Modes:
            {
                var p = FemModesParams.Parse(item.ParamsJson);
                return string.Format(c, Loc.S("FemImperfectionModesShort"), p.CaseIndex + 1,
                    string.Join(", ", p.Modes.Select(m => m.IsEn1993
                        ? string.Format(c, "{0}: {1}(5.9)", m.Mode, m.Minus ? "−" : "")
                        : string.Format(c, "{0}: {1:0.##}", m.Mode, m.AmplitudeMm))));
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
    /// <param name="wallPreview">Предпросмотр ярусов погиби стен по параметрам (строки «стена, ярус: h, e₀» или ошибки); null — нет сетки.</param>
    /// <param name="modesPreview">Предпросмотр амплитуд форм (в т. ч. по (5.9) с ходом) по параметрам; null — нет.</param>
    public FemImperfectionDialogVM(FemImperfection item, Func<FemSwayParams, double?>? spanZ = null, int? mHint = null,
        Func<(IReadOnlyCollection<string> Members, IReadOnlyCollection<string> Elements)>? selection = null,
        IReadOnlyList<FemBucklingSourceChoice>? bucklingSources = null, Func<FemWallBowParams, string>? wallPreview = null,
        Func<FemModesParams, string>? modesPreview = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        _source = item;
        _spanZ = spanZ;
        _selection = selection;
        _wallPreview = wallPreview;
        _modesPreview = modesPreview;
        MHint = mHint;
        BucklingSources = bucklingSources ?? [];
        _kind = item.Kind is FemImperfectionKinds.Bow or FemImperfectionKinds.Modes or FemImperfectionKinds.WallBow
            ? item.Kind : FemImperfectionKinds.Sway;
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
            _phiNorm = p.PhiMode is FemSwayParams.PhiEn1993 or FemSwayParams.PhiEn1992;
            _norm = p.PhiMode == FemSwayParams.PhiEn1992 ? NormEn1992 : NormEn1993;
            _hText = p.HeightM?.ToString("0.###", c) ?? "";
            _mText = p.M.ToString(c);
            _phiInvText = p.PhiInv?.ToString("0.#", c) ?? "200";
        }
        else if (_kind == FemImperfectionKinds.WallBow)
        {
            var p = FemWallBowParams.Parse(item.ParamsJson);
            _membersText = Join(p.MemberTags);
            _elementsText = Join(p.ElementTags);
            _norm = p.E0Mode == FemBowParams.E0Sp63 ? NormSp63 : NormEn1992;
            _e0Mode = p.E0Mode;
            _muText = p.Mu?.ToString("0.###", c) ?? "1";
            _lOverE0Text = p.LOverE0?.ToString("0.#", c) ?? "";
            _e0MmText = p.E0Mm?.ToString("0.##", c) ?? "";
            _sizeMmText = p.ThicknessMm?.ToString("0.#", c) ?? "";
            _sign = p.Sign is FemBowParams.SignPlus or FemBowParams.SignMinus ? p.Sign : FemBowParams.SignAuto;
        }
        else if (_kind == FemImperfectionKinds.Modes)
        {
            var p = FemModesParams.Parse(item.ParamsJson);
            _bucklingSource = BucklingSources.FirstOrDefault(s => s.AnalysisId == p.AnalysisId) ?? BucklingSources.FirstOrDefault();
            _bucklingCase = _bucklingSource?.Cases.FirstOrDefault(k => k.Index == p.CaseIndex) ?? _bucklingSource?.Cases.FirstOrDefault();
            foreach (var m in p.Modes)
                ModeRows.Add(new FemModeAmplitudeRow(m.Mode.ToString(c), m.IsEn1993 ? "" : m.AmplitudeMm.ToString("0.##", c))
                {
                    ByFormula = m.IsEn1993, Minus = m.Minus,
                });
            _gammaM1Text = (p.GammaM1 ?? En1993Imperfections.GammaM1Recommended).ToString("0.###", c);
            _modeCurve = p.Curve != null && Curves.Any(k => k.Key == p.Curve) ? p.Curve : FemBowParams.CurveAuto;
            _criticalText = p.CriticalElement ?? "";
        }
        else
        {
            var p = FemBowParams.Parse(item.ParamsJson);
            _membersText = Join(p.MemberTags);
            _chainsText = string.Join("; ", p.ElementChains.Select(Join));
            _plane = p.Plane == FemBowParams.PlaneZ ? FemBowParams.PlaneZ : FemBowParams.PlaneY;
            _norm = p.E0Mode == FemBowParams.E0Sp63 ? NormSp63
                : p.E0Mode is FemBowParams.E0En1992 or FemBowParams.E0En1992Simplified ? NormEn1992 : NormEn1993;
            _e0Mode = p.E0Mode;
            _muText = p.Mu?.ToString("0.###", c) ?? "1";
            _sizeMmText = p.SectionHeightMm?.ToString("0.#", c) ?? "";
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
        PreviewTiersCommand = new RelayCommand(_ => PreviewTiers(), _ => _wallPreview != null);
        PreviewModesCommand = new RelayCommand(_ => PreviewModes(), _ => _modesPreview != null);
        if (_kind == FemImperfectionKinds.Modes && ModeRows.Count == 0) AddMode();
        // Крен «φ по (5.5)»: недоступное поле 1/φ — вычисленное значение (ход — по кнопке «по (5.5)»), а не ручное по умолчанию.
        if (_kind == FemImperfectionKinds.Sway && _phiNorm && TryPhi(out double phiOpen, out _))
            _phiInvText = (1 / phiOpen).ToString("0.#", c);
        if (_kind == FemImperfectionKinds.WallBow && _norm == NormEn1993) _norm = NormEn1992;
        if (!E0ModeChoices.Any(k => k.Key == _e0Mode)) _e0Mode = E0ModeChoices[0].Key;
    }

    // ------------------------------------------------------------ норма

    public const string NormEn1993 = "en1993";
    public const string NormEn1992 = "en1992";
    public const string NormSp63 = "sp63";

    /// <summary>Нормы: сталь EN 1993-1-1, ЖБ EN 1992-1-1 и СП 63 (у погиби стен — только ЖБ).</summary>
    public IReadOnlyList<FemLoadsChoice> Norms => IsWallBow
        ? [new(NormEn1992, Loc.S("FemImperfectionNormEn1992")), new(NormSp63, Loc.S("FemImperfectionNormSp63"))]
        : [new(NormEn1993, Loc.S("FemImperfectionNormEn1993")), new(NormEn1992, Loc.S("FemImperfectionNormEn1992")),
           new(NormSp63, Loc.S("FemImperfectionNormSp63"))];

    string _norm = NormEn1993;
    /// <summary>Норма элемента — выводится из режимов φ/e₀ и задаёт их список.</summary>
    public string Norm
    {
        get => _norm;
        set
        {
            if (value is not (NormEn1993 or NormEn1992 or NormSp63) || value == _norm) return;
            if (IsWallBow && value == NormEn1993) return;
            _norm = value;
            OnPropertyChanged();
            OnNormChanged();
        }
    }

    void OnNormChanged()
    {
        if (_norm == NormSp63 && _phiNorm) PhiNorm = false;
        OnPropertyChanged(nameof(NormText));
        OnPropertyChanged(nameof(CanPhiNorm));
        OnPropertyChanged(nameof(PhiNormLabel));
        OnPropertyChanged(nameof(IsConcreteNorm));
        OnPropertyChanged(nameof(ShowActivity));
        OnPropertyChanged(nameof(E0ModeChoices));
        if (!E0ModeChoices.Any(k => k.Key == _e0Mode)) E0Mode = E0ModeChoices[0].Key;
        PhiDescription = "";
    }

    /// <summary>Полное обозначение нормы.</summary>
    public string NormText => _norm switch
    {
        NormEn1992 => "EN 1992-1-1:2004+AC:2019",
        NormSp63 => "СП 63.13330.2018",
        _ => "EN 1993-1-1:2005+AC:2009",
    };

    /// <summary>ЖБ-норма: активность погиби — всегда (EN 1992-1-1 п. 5.2(1)P).</summary>
    public bool IsConcreteNorm => _norm != NormEn1993;

    /// <summary>Крен по норме доступен (в СП 63 крена нет — только 1/φ вручную).</summary>
    public bool CanPhiNorm => _norm != NormSp63;

    public string PhiNormLabel => Loc.S(_norm == NormEn1992 ? "FemImperfectionPhiNormEn1992" : "FemImperfectionPhiNorm");

    // ------------------------------------------------------------ общее

    public bool IsNew => _source.Id == 0;

    string _kind;
    /// <summary><see cref="FemImperfectionKinds.Sway"/> | <see cref="FemImperfectionKinds.Bow"/>.</summary>
    public string Kind
    {
        get => _kind;
        set
        {
            if (value is not (FemImperfectionKinds.Sway or FemImperfectionKinds.Bow or FemImperfectionKinds.Modes
                or FemImperfectionKinds.WallBow) || value == _kind) return;
            _kind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSway));
            OnPropertyChanged(nameof(IsBow));
            OnPropertyChanged(nameof(IsModes));
            OnPropertyChanged(nameof(IsWallBow));
            OnPropertyChanged(nameof(IsBowLike));
            OnPropertyChanged(nameof(HasNorm));
            OnPropertyChanged(nameof(HasTargets));
            OnPropertyChanged(nameof(IsListScope));
            OnPropertyChanged(nameof(HasElementsList));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Norms));
            OnPropertyChanged(nameof(ShowActivity));
            OnPropertyChanged(nameof(SizeMmLabel));
            OnPropertyChanged(nameof(RelativeLabel));
            if (IsModes)
            {
                if (_bucklingSource == null) BucklingSource = BucklingSources.FirstOrDefault();
                if (ModeRows.Count == 0) AddMode();
            }
            if (IsWallBow && _norm == NormEn1993)
            {
                _norm = NormEn1992;
                OnPropertyChanged(nameof(Norm));
            }
            OnNormChanged();
        }
    }

    public bool IsSway { get => _kind == FemImperfectionKinds.Sway; set { if (value) Kind = FemImperfectionKinds.Sway; } }
    public bool IsBow { get => _kind == FemImperfectionKinds.Bow; set { if (value) Kind = FemImperfectionKinds.Bow; } }
    public bool IsModes { get => _kind == FemImperfectionKinds.Modes; set { if (value) Kind = FemImperfectionKinds.Modes; } }
    public bool IsWallBow { get => _kind == FemImperfectionKinds.WallBow; set { if (value) Kind = FemImperfectionKinds.WallBow; } }

    /// <summary>Блок e₀ виден: погибь стержня или погибь стен.</summary>
    public bool IsBowLike => IsBow || IsWallBow;

    /// <summary>Выбор нормы виден — не у форм (у форм амплитуда задаётся вручную).</summary>
    public bool HasNorm => !IsModes;

    /// <summary>Блок «Относительно» (охват крена, элементы погиби) виден — не у форм.</summary>
    public bool HasTargets => !IsModes;

    public string Title => Loc.S(IsSway ? "FemImperfectionDialogTitleSway" : IsBow ? "FemImperfectionDialogTitleBow"
        : IsWallBow ? "FemImperfectionDialogTitleWallBow" : "FemImperfectionDialogTitleModes");

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
    public bool IsListScope => IsBow || IsWallBow || (IsSway && !_scopeAll);

    /// <summary>Поле «КЭ сетки» видно: крен и погибь стен (у погиби стержня — цепочки).</summary>
    public bool HasElementsList => IsSway || IsWallBow;

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
        if (IsWallBow) ElementsText = Join(elements);
        else if (IsSway)
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
        set
        {
            if (value && !CanPhiNorm) value = false;
            if (_phiNorm == value) return;
            _phiNorm = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PhiManual));
        }
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

    /// <summary>
    /// Считает φ по (5.5) по полям h и m и подставляет 1/φ в поле (при «φ по (5.5)» оно недоступно и показывает
    /// вычисленное значение, а не прежнее ручное). false — данных не хватает (текст — почему).
    /// </summary>
    public bool ComputePhi()
    {
        bool ok = TryPhi(out double value, out string description);
        PhiDescription = description;
        if (ok) PhiInvText = (1 / value).ToString("0.#", CultureInfo.CurrentCulture);
        return ok;
    }

    /// <summary>φ (θᵢ) по полям h и m и ход вычисления; false — данных не хватает (<paramref name="description"/> — почему).</summary>
    bool TryPhi(out double value, out string description)
    {
        var c = CultureInfo.CurrentCulture;
        value = 0;
        if (!int.TryParse(MText.Trim(), NumberStyles.Integer, c, out int m) || m < 1)
        {
            description = Loc.S("FemImperfectionBadM");
            return false;
        }
        double h;
        string source;
        if (HText.Trim().Length > 0)
        {
            if (!Pars.ParseAny(HText.Trim(), out h) || !double.IsFinite(h) || h <= 0)
            {
                description = Loc.S("FemImperfectionBadH");
                return false;
            }
            source = Loc.S("FemImperfectionHManual");
        }
        else
        {
            var p = BuildSway(out _);
            if (p == null || _spanZ?.Invoke(p) is not { } span || !(span > 1e-9))
            {
                description = Loc.S("FemImperfectionHUnknown");
                return false;
            }
            h = span;
            source = string.Format(c, Loc.S("FemImperfectionHSpan"), span);
        }
        if (_norm == NormEn1992)
        {
            var theta = En1992Imperfections.Theta(h, m);
            description = $"{source}: EN 1992-1-1 (5.1): {theta.Describe()}";
            value = theta.Theta;
        }
        else
        {
            var phi = En1993Imperfections.Phi(h, m);
            description = $"{source}: {phi.Describe()}";
            value = phi.Phi;
        }
        return true;
    }

    // ------------------------------------------------------------ погибь

    public static IReadOnlyList<FemLoadsChoice> Planes { get; } =
    [
        new(FemBowParams.PlaneY, Loc.S("FemImperfectionPlaneY")),
        new(FemBowParams.PlaneZ, Loc.S("FemImperfectionPlaneZ")),
    ];

    /// <summary>Способы задания e₀ по норме и виду (погибь стен — h_яр вместо L).</summary>
    public IReadOnlyList<FemLoadsChoice> E0ModeChoices
    {
        get
        {
            var relative = new FemLoadsChoice(FemBowParams.E0Relative, Loc.S(IsWallBow ? "FemImperfectionE0RelativeWall" : "FemImperfectionE0Relative"));
            var absolute = new FemLoadsChoice(FemBowParams.E0Absolute, Loc.S("FemImperfectionE0Absolute"));
            return _norm switch
            {
                NormEn1992 =>
                [
                    new(FemBowParams.E0En1992Simplified, Loc.S("FemImperfectionE0En1992Simplified")),
                    new(FemBowParams.E0En1992, Loc.S("FemImperfectionE0En1992")),
                    relative, absolute,
                ],
                NormSp63 => [new(FemBowParams.E0Sp63, Loc.S(IsWallBow ? "FemImperfectionE0Sp63Wall" : "FemImperfectionE0Sp63")), absolute],
                _ => [new(FemBowParams.E0Table, Loc.S("FemImperfectionE0Table")), relative, absolute],
            };
        }
    }

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
            OnPropertyChanged(nameof(IsE0Mu));
            OnPropertyChanged(nameof(IsE0Sp63));
            OnPropertyChanged(nameof(TableValueText));
        }
    }

    public bool IsE0Table => _e0Mode == FemBowParams.E0Table;
    public bool IsE0Relative => _e0Mode == FemBowParams.E0Relative;
    public bool IsE0Absolute => _e0Mode == FemBowParams.E0Absolute;
    /// <summary>Поле μ (l₀ = μ·L) видно — режимы EN 1992.</summary>
    public bool IsE0Mu => _e0Mode is FemBowParams.E0En1992 or FemBowParams.E0En1992Simplified;
    /// <summary>Поле высоты сечения / толщины стены видно — п. 8.1.7 СП 63.</summary>
    public bool IsE0Sp63 => _e0Mode == FemBowParams.E0Sp63;

    /// <summary>Критерий активности п. 5.3.2(6) — только у погиби стержня по EN 1993 (ЖБ — всегда).</summary>
    public bool ShowActivity => IsBow && !IsConcreteNorm;

    public string RelativeLabel => IsWallBow ? "h_яр/e₀" : "L/e₀";

    public string SizeMmLabel => Loc.S(IsWallBow ? "FemImperfectionThicknessMm" : "FemImperfectionSectionHeightMm");

    string _muText = "1";
    /// <summary>μ расчётной длины l₀ = μ·L (погибь) или μ·h_яр (стены).</summary>
    public string MuText { get => _muText; set { _muText = value ?? ""; OnPropertyChanged(); } }

    string _sizeMmText = "";
    /// <summary>Высота сечения h (погибь) или толщина t (стены), мм, для п. 8.1.7; пусто — по сечению / толщине КЭ.</summary>
    public string SizeMmText { get => _sizeMmText; set { _sizeMmText = value ?? ""; OnPropertyChanged(); } }

    // ------------------------------------------------------------ ярусы стен

    readonly Func<FemWallBowParams, string>? _wallPreview;

    /// <summary>«Ярусы»: предпросмотр ярусов погиби стен по полям (без предварительного расчёта).</summary>
    public ICommand PreviewTiersCommand { get; }

    string _tiersText = "";
    public string TiersText { get => _tiersText; private set { _tiersText = value; OnPropertyChanged(); } }

    void PreviewTiers()
    {
        if (_wallPreview == null) return;
        TiersText = BuildWallBow(out string? error) is { } p ? _wallPreview(p) : error ?? "";
    }

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
            if (string.IsNullOrWhiteSpace(r.ModeText) && string.IsNullOrWhiteSpace(r.AmplitudeText) && !r.ByFormula) continue;
            if (!int.TryParse(r.ModeText.Trim(), NumberStyles.Integer, c, out int mode) || mode < 1
                || (_bucklingCase.Lambda.Count > 0 && mode > _bucklingCase.Lambda.Count))
            {
                error = string.Format(c, Loc.S("FemImperfectionModesBadMode"), r.ModeText, _bucklingCase.Lambda.Count);
                return null;
            }
            double a = 0;
            if (!r.ByFormula && (!Pars.ParseAny(r.AmplitudeText.Trim(), out a) || !double.IsFinite(a) || a == 0))
            {
                error = string.Format(c, Loc.S("FemImperfectionModesBadAmplitude"), mode);
                return null;
            }
            if (modes.Any(m => m.Mode == mode)) { error = string.Format(c, Loc.S("FemImperfectionModesDuplicate"), mode); return null; }
            modes.Add(r.ByFormula
                ? new FemModeAmplitude { Mode = mode, Amplitude = FemModeAmplitude.AmplitudeEn1993, Minus = r.Minus }
                : new FemModeAmplitude { Mode = mode, AmplitudeMm = a });
        }
        if (modes.Count == 0) { error = Loc.S("FemImperfectionModesEmpty"); return null; }
        double? gamma = null;
        string? critical = null;
        if (modes.Any(m => m.IsEn1993))
        {
            if (!Pars.ParseAny(_gammaM1Text.Trim(), out double g) || !(g > 0) || !double.IsFinite(g))
            {
                error = Loc.S("FemImperfectionModesBadGamma");
                return null;
            }
            gamma = Math.Abs(g - En1993Imperfections.GammaM1Recommended) < 1e-12 ? null : g;
            critical = string.IsNullOrWhiteSpace(_criticalText) ? null : _criticalText.Trim();
        }
        return new FemModesParams
        {
            AnalysisId = _bucklingSource.AnalysisId, CaseIndex = _bucklingCase.Index, Modes = modes, GammaM1 = gamma,
            Curve = modes.Any(m => m.IsEn1993) && _modeCurve != FemBowParams.CurveAuto ? _modeCurve : null,
            CriticalElement = critical,
        };
    }

    // ------------------------------------------------------------ амплитуда по (5.9)

    string _gammaM1Text = En1993Imperfections.GammaM1Recommended.ToString("0.###", CultureInfo.CurrentCulture);
    /// <summary>γ_M1 для (5.10), текстом (по умолчанию 1 — рекомендуемое).</summary>
    public string GammaM1Text { get => _gammaM1Text; set { _gammaM1Text = value ?? ""; OnPropertyChanged(); } }

    string _modeCurve = FemBowParams.CurveAuto;
    /// <summary>Кривая для (5.10): «auto» — по табл. 6.2 по сечению критического КЭ, иначе a0…d.</summary>
    public string ModeCurve { get => _modeCurve; set { if (value != null) { _modeCurve = value; OnPropertyChanged(); } } }

    string _criticalText = "";
    /// <summary>Критический КЭ (номер); пусто — по наименьшему α_ult,k.</summary>
    public string CriticalText { get => _criticalText; set { _criticalText = value ?? ""; OnPropertyChanged(); } }

    readonly Func<FemModesParams, string>? _modesPreview;

    /// <summary>«Амплитуды»: предпросмотр амплитуд форм резолвером (с ходом (5.9)).</summary>
    public ICommand PreviewModesCommand { get; }

    string _modesPreviewText = "";
    public string ModesPreviewText { get => _modesPreviewText; private set { _modesPreviewText = value; OnPropertyChanged(); } }

    void PreviewModes()
    {
        if (_modesPreview == null) return;
        ModesPreviewText = BuildModes(out string? error) is { } p ? _modesPreview(p) : error ?? "";
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
        else if (IsWallBow)
        {
            if (BuildWallBow(out error) is not { } p) return null;
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
            PhiMode = !_phiNorm ? FemSwayParams.PhiManual : _norm == NormEn1992 ? FemSwayParams.PhiEn1992 : FemSwayParams.PhiEn1993,
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
        if (!ParseE0(out double? lOverE0, out double? e0Mm, out double? mu, out double? size, out error)) return null;
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
            Mu = mu,
            SectionHeightMm = size,
            Sign = _sign,
            // ЖБ (EN 1992-1-1 п. 5.2(1)P) — всегда; критерий п. 5.3.2(6) — только сталь.
            Activity = IsConcreteNorm ? FemBowParams.ActivityAlways : _activity,
        };
    }

    FemWallBowParams? BuildWallBow(out string? error)
    {
        var members = SplitTags(MembersText);
        var elements = ExpandTags(SplitTags(ElementsText));
        if (members.Count + elements.Count == 0) { error = Loc.S("FemImperfectionWallEmpty"); return null; }
        if (!ParseE0(out double? lOverE0, out double? e0Mm, out double? mu, out double? size, out error)) return null;
        return new FemWallBowParams
        {
            MemberTags = members, ElementTags = elements, E0Mode = _e0Mode, Mu = mu, LOverE0 = lOverE0, E0Mm = e0Mm,
            ThicknessMm = size, Sign = _sign,
        };
    }

    /// <summary>Поля e₀ по режиму: L/e₀ (h_яр/e₀), мм, μ (режимы EN 1992), h или t (п. 8.1.7, пусто — по КЭ).</summary>
    bool ParseE0(out double? lOverE0, out double? e0Mm, out double? mu, out double? size, out string? error)
    {
        lOverE0 = e0Mm = mu = size = null;
        error = null;
        if (IsE0Relative)
        {
            if (!Pars.ParseAny(LOverE0Text.Trim(), out double v) || !double.IsFinite(v) || v <= 0) { error = Loc.S("FemImperfectionBadLOverE0"); return false; }
            lOverE0 = v;
        }
        else if (IsE0Absolute)
        {
            if (!Pars.ParseAny(E0MmText.Trim(), out double v) || !double.IsFinite(v) || v <= 0) { error = Loc.S("FemImperfectionBadE0Mm"); return false; }
            e0Mm = v;
        }
        else if (IsE0Mu)
        {
            if (!Pars.ParseAny(MuText.Trim(), out double v) || !double.IsFinite(v) || v <= 0) { error = Loc.S("FemImperfectionBadMu"); return false; }
            mu = v;
        }
        else if (IsE0Sp63 && SizeMmText.Trim().Length > 0)
        {
            if (!Pars.ParseAny(SizeMmText.Trim(), out double v) || !double.IsFinite(v) || v <= 0) { error = Loc.S("FemImperfectionBadSizeMm"); return false; }
            size = v;
        }
        return true;
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

/// <summary>
/// Строка таблицы форм диалога: номер формы и амплитуда, мм (текстом — проверка при OK), или амплитуда по EN 1993-1-1
/// (5.9) (<see cref="ByFormula"/>, знак — <see cref="Minus"/>).
/// </summary>
public sealed class FemModeAmplitudeRow(string mode, string amplitude) : ViewModelBase
{
    string _mode = mode, _amplitude = amplitude;
    bool _byFormula, _minus;
    public string ModeText { get => _mode; set { _mode = value ?? ""; OnPropertyChanged(); } }
    public string AmplitudeText { get => _amplitude; set { _amplitude = value ?? ""; OnPropertyChanged(); } }

    /// <summary>Амплитуда по (5.9)–(5.11) (поле «мм» не используется).</summary>
    public bool ByFormula
    {
        get => _byFormula;
        set { if (_byFormula == value) return; _byFormula = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsManual)); }
    }

    public bool IsManual => !_byFormula;

    /// <summary>Знак «−» амплитуды по (5.9).</summary>
    public bool Minus { get => _minus; set { _minus = value; OnPropertyChanged(); } }
}
