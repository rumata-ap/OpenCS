using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using CScore;
using CScore.Fem;
using CScore.Fem.Fragment.Masks;
using CScore.Sp63Punching;
using CScore.Sp63Punching.Fem;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Постановка CSfea — источник усилий.</summary>
public sealed record PunchingFemAnalysisItem(int Id, string Label, string Kind);

/// <summary>Строка таблицы найденных узлов окна параметров: включение и переопределения узла.</summary>
public sealed class PunchingFemJointPickVM : INotifyPropertyChanged
{
    bool _include = true, _ownSw;
    double? _ax, _ay;
    string _capitalMode = "auto";
    double _capBx, _capBy, _capHc;
    double _swDiameter = 8, _swStep = 60;
    int _swCount = 2, _swRows = 5;

    public PunchingFemJointPickVM(PunchingFemJoint joint, PunchingFemJointOverride? o)
    {
        Joint = joint;
        var c = CultureInfo.CurrentCulture;
        Node = joint.NodeTag;
        Section = joint.SectionLabel;
        Slab = joint.SlabLabel.Length > 0 ? string.Format(c, "{0}; h0 = {1:0} " + Loc.S("UnitMm", "мм"), joint.SlabLabel, joint.H0 * 1000) : "";
        if (joint.Capital is { Area: PunchingLoadArea.Rectangle cr } cap)
        {
            CapitalAutoText = string.Format(c, Loc.S("PunchingFemCapitalAuto", "по схеме: {0:0}×{1:0}, hc = {2:0} мм"), cr.Bx * 1000, cr.By * 1000, cap.Hc * 1000);
            _capBx = cr.Bx * 1000; _capBy = cr.By * 1000; _capHc = cap.Hc * 1000;
        }
        else
        {
            CapitalAutoText = Loc.S("PunchingFemCapitalNoneInSchema", "в схеме не найдена");
            // Начальные размеры для «вручную»: как у одиночной задачи.
            _capBx = 1200; _capBy = 1200; _capHc = 150;
        }
        Position = (joint.Edges.Count switch
        {
            0 => Loc.S("PunchingPosition_inside", "внутри плиты"),
            1 => Loc.S("PunchingPosition_edge", "у края"),
            _ => Loc.S("PunchingPosition_corner", "в углу"),
        }) + (joint.Holes.Count > 0 ? string.Format(c, Loc.S("PunchingFemHolesSuffix", ", отверстий {0}"), joint.Holes.Count) : "");
        Checkable = joint.NoCheck == null;
        StatusText = joint.NoCheck == null
            ? (joint.Warnings.Count > 0 ? string.Join("\n", joint.Warnings.Select(w => "• " + w.Text)) : Loc.S("PunchingFemPickReady", "готов к проверке"))
            : PunchingFemStatus.Text("no_check") + " — " + joint.NoCheckText;
        StatusBrush = PunchingFemStatus.Brush(joint.NoCheck == null ? (joint.Warnings.Count > 0 ? "reinforcement" : "concrete") : "no_check", true);
        if (o != null)
        {
            _include = !o.Excluded;
            _ax = o.Ax * 1000;
            _ay = o.Ay * 1000;
            if (o.Capital is "none" or "manual") _capitalMode = o.Capital;
            if (o.CapitalManual is { } cm) { _capBx = cm.Bx * 1000; _capBy = cm.By * 1000; _capHc = cm.Hc * 1000; }
            if (o.Reinforcement is { } r)
            {
                _ownSw = true;
                _swDiameter = r.Diameter * 1000;
                _swCount = r.Count;
                _swStep = r.Step * 1000;
                _swRows = r.RowCount;
            }
        }
    }

    public PunchingFemJoint Joint { get; }
    public string Node { get; }
    public string Section { get; }
    public string Slab { get; }
    public string Position { get; }
    public bool Checkable { get; }
    public string StatusText { get; }
    public Brush StatusBrush { get; }
    public string AxAuto => (Joint.Ax * 1000).ToString("0", CultureInfo.CurrentCulture);
    public string AyAuto => (Joint.Ay * 1000).ToString("0", CultureInfo.CurrentCulture);

    public bool Include { get => _include; set => Set(ref _include, value); }
    /// <summary>Привязка ax узла, мм; null — из сечения плиты.</summary>
    public double? Ax { get => _ax; set => Set(ref _ax, value is > 0 ? value : null); }
    public double? Ay { get => _ay; set => Set(ref _ay, value is > 0 ? value : null); }
    public bool OwnSw { get => _ownSw; set => Set(ref _ownSw, value); }
    public double SwDiameter { get => _swDiameter; set => Set(ref _swDiameter, value); }
    public int SwCount { get => _swCount; set => Set(ref _swCount, value); }
    public double SwStep { get => _swStep; set => Set(ref _swStep, value); }
    /// <summary>Число рядов узла (при раскладке рядами).</summary>
    public int SwRows { get => _swRows; set => Set(ref _swRows, value); }

    /// <summary>Капитель узла: "auto" — по схеме, "none" — без капители, "manual" — размеры строки.</summary>
    public string CapitalMode
    {
        get => _capitalMode;
        set
        {
            string mode = value is "none" or "manual" ? value : "auto";
            if (mode == _capitalMode) return;
            // «По схеме» — размеры снова распознанные (правки «вручную» не остаются висеть в строке).
            if (mode == "auto" && Joint.Capital is { Area: PunchingLoadArea.Rectangle cr } cap)
            {
                _capBx = cr.Bx * 1000; _capBy = cr.By * 1000; _capHc = cap.Hc * 1000;
            }
            Set(ref _capitalMode, mode);
            foreach (var n in new[] { nameof(CapBx), nameof(CapBy), nameof(CapHc) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }
    }
    /// <summary>Режимы капители для списка.</summary>
    public static IReadOnlyList<PunchingChoice> CapitalModes { get; } =
    [
        new("auto", Loc.S("PunchingFemCapital_auto", "по схеме")), new("none", Loc.S("PunchingFemCapital_none", "нет")),
        new("manual", Loc.S("PunchingFemCapital_manual", "вручную")),
    ];
    /// <summary>Что распознано в схеме (подсказка).</summary>
    public string CapitalAutoText { get; } = "";
    /// <summary>Размеры капители, мм; правка переводит узел в режим «вручную».</summary>
    /// <remarks>Пусто, когда капители нет (режим «нет» или «по схеме» без капители в схеме); ввод любого числа, даже
    /// прежнего, переводит узел в «вручную».</remarks>
    public double? CapBx { get => ShowCapital ? _capBx : null; set => SetCapital(ref _capBx, value); }
    public double? CapBy { get => ShowCapital ? _capBy : null; set => SetCapital(ref _capBy, value); }
    public double? CapHc { get => ShowCapital ? _capHc : null; set => SetCapital(ref _capHc, value); }

    /// <summary>
    /// Рабочая высота у колонны для расстановки рядов поперечной арматуры, м: h0 + hc капители узла (по схеме или
    /// вручную), без капители — h0.
    /// </summary>
    public double SwH0 => Joint.H0 + _capitalMode switch
    {
        "none" => 0,
        "manual" => Math.Max(0, _capHc / 1000),
        _ => Joint.Capital?.Hc ?? 0,
    };

    /// <summary>Размеры капители видны: «вручную» или распознанная в схеме при «по схеме».</summary>
    bool ShowCapital => _capitalMode == "manual" || (_capitalMode == "auto" && Joint.Capital != null);

    void SetCapital(ref double field, double? value, [CallerMemberName] string? name = null)
    {
        if (value is not double v) return;
        field = v;
        CapitalMode = "manual";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        Changed?.Invoke();
    }

    /// <summary>Узел продавливания ЛИРЫ из приложенного *.asp; null — ЛИРА этот узел не считала или файла нет.</summary>
    public CScore.Import.LiraAspPunching? Lira { get; private set; }
    /// <summary>Результат ЛИРЫ кратко: «Asw 6,08 см²», «Ksw 2,00», «#170»; пусто — нет.</summary>
    public string LiraText { get; private set; } = "";
    /// <summary>Подсказка к результату ЛИРЫ: номер записи, U, классы, сообщение.</summary>
    public string? LiraHint { get; private set; }

    /// <summary>Задать узел ЛИРЫ (после сопоставления с *.asp).</summary>
    public void SetLira(CScore.Import.LiraAspPunching? lira)
    {
        Lira = lira;
        LiraText = PunchingFemLiraText.Short(lira);
        LiraHint = PunchingFemLiraText.Hint(lira);
        foreach (var n in new[] { nameof(Lira), nameof(LiraText), nameof(LiraHint) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>Переопределение узла; null — переопределять нечего.</summary>
    public PunchingFemJointOverride? ToOverride(PunchingReinforcementParams common) =>
        _include && _ax == null && _ay == null && !_ownSw && _capitalMode == "auto" ? null : new PunchingFemJointOverride
        {
            Node = Node, Excluded = !_include, Ax = _ax / 1000, Ay = _ay / 1000,
            Capital = _capitalMode == "auto" ? null : _capitalMode,
            CapitalManual = _capitalMode != "manual" ? null : new PunchingCapitalParams
            {
                Enabled = true, Bx = _capBx / 1000, By = _capBy / 1000, D = _capBx / 1000, Hc = _capHc / 1000,
                Dx = Joint.Capital?.Offset.X ?? 0, Dy = Joint.Capital?.Offset.Y ?? 0,
            },
            Reinforcement = _ownSw ? common with { Enabled = true, Diameter = _swDiameter / 1000, Count = _swCount, Step = _swStep / 1000, RowCount = _swRows } : null,
        };

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Changed;

    void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        Changed?.Invoke();
    }
}

/// <summary>Текст результата ЛИРЫ по узлу продавливания (таблица 47) для окон проверки и результата.</summary>
public static class PunchingFemLiraText
{
    /// <summary>Кратко: «#170», «Asw 6,08 см²; Ksw 1,60», «Ksw 2,00»; пусто — нет узла ЛИРЫ.</summary>
    public static string Short(CScore.Import.LiraAspPunching? p) => p == null ? "" : Short(p.ErrorCode, p.Asw, p.Ksw);

    public static string Short(int errorCode, double asw, double ksw)
    {
        var c = CultureInfo.CurrentCulture;
        if (errorCode != 0) return "#" + errorCode.ToString(c);
        return asw > 0
            ? string.Format(c, Loc.S("PunchingFemLiraAswKsw", "Asw {0:0.00} см²; Ksw {1:0.00}"), asw, ksw)
            : string.Format(c, Loc.S("PunchingFemLiraKsw", "Ksw {0:0.00}"), ksw);
    }

    /// <summary>Подсказка: запись, U, классы, сообщение ЛИРЫ.</summary>
    public static string? Hint(CScore.Import.LiraAspPunching? p) => p == null ? null
        : Hint(p.Index, p.Perimeter, p.ConcreteClass, p.RebarClass, p.ErrorText);

    public static string Hint(int index, double perimeter, string concrete, string rebar, string? error)
    {
        var c = CultureInfo.CurrentCulture;
        string s = string.Format(c, Loc.S("PunchingFemLiraHint", "ЛИРА, запись {0} таблицы «Арматура для контуров продавливания»: U = {1:0} мм"),
            index, perimeter * 1000);
        if (concrete.Length + rebar.Length > 0) s += $"; {concrete} / {rebar}";
        return error != null ? s + "\n" + error : s;
    }
}

/// <summary>
/// Окно параметров проверки продавливания по МКЭ (<c>punching_check</c>): схема и расчёт CSfea — источник усилий,
/// способ определения силы, бетон и привязки, поперечная арматура, режимы ядра, поиск узлов с таблицей и
/// переопределениями на узел; живой рисунок выбранного узла (контуры по геометрии из схемы).
/// </summary>
public sealed class PunchingFemCheckVM : ViewModelBase
{
    readonly DatabaseService _db;
    readonly IReadOnlyList<Material> _materials;
    readonly Dictionary<string, PunchingFemJointOverride> _overrides = new(StringComparer.Ordinal);
    FemSchema? _schema;
    string? _keepKind;
    PunchingFemAnalysisItem? _analysis;
    PunchingFemJointPickVM? _selectedJoint;
    bool _loading;

    /// <summary>Имя новой проверки: «Продавливание», если свободно, иначе «Продавливание 2», «Продавливание 3»…</summary>
    public static string UniqueTag(string baseTag, IEnumerable<string?> taken)
    {
        var used = taken.Where(t => !string.IsNullOrEmpty(t)).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (!used.Contains(baseTag)) return baseTag;
        for (int i = 2; ; i++)
            if (!used.Contains($"{baseTag} {i}")) return $"{baseTag} {i}";
    }

    public PunchingFemCheckVM(DatabaseService db, FemCheck? existing, FemSchema? schema = null)
    {
        _db = db;
        _materials = db.Materials.ToList();
        Concretes = [.. _materials.Where(m => m.Type == MatType.Concrete)];
        Rebars = [.. _materials.Where(m => m.Type is MatType.ReSteelF or MatType.ReSteelU)];
        Schemas = [.. db.FemSchemas];
        Existing = existing;
        var p = PunchingFemCheckParams.Parse(existing?.ParamsJson);
        bool isNew = existing == null;
        if (isNew)
            p = p with
            {
                ConcreteMaterialId = Concretes.FirstOrDefault()?.Id ?? 0,
                Reinforcement = p.Reinforcement with { MaterialId = Rebars.FirstOrDefault()?.Id ?? 0 },
            };
        foreach (var o in p.Overrides) _overrides[o.Node] = o;

        _loading = true;
        Tag = existing?.Tag is { Length: > 0 } t ? t : UniqueTag(Loc.S("PunchingFemDefaultTag", "Продавливание"), db.FemChecks.Select(c => c.Tag));
        _schema = existing != null ? Schemas.FirstOrDefault(s => s.Id == existing.SchemaId) : schema ?? Schemas.FirstOrDefault();
        _keepKind = existing != null ? p.SourceKind : null;
        RefreshAnalyses(p.SourceRef);
        Method = p.Method;
        MomentsAreMloc = p.MomentsAreMloc;
        ConcreteFromSchema = p.ConcreteFromSchema;
        Concrete = Concretes.FirstOrDefault(m => m.Id == p.ConcreteMaterialId) ?? Concretes.FirstOrDefault();
        RbtManual = p.RbtOverride is not null;
        RbtManualValue = (p.RbtOverride ?? 1050) / 1000;
        DefaultAx = p.DefaultAx * 1000;
        DefaultAy = p.DefaultAy * 1000;
        var r = p.Reinforcement;
        SwEnabled = r.Enabled;
        Rebar = Rebars.FirstOrDefault(m => m.Id == r.MaterialId) ?? Rebars.FirstOrDefault();
        Diameter = r.Diameter * 1000;
        Count = r.Count;
        Step = r.Step * 1000;
        ZoneEnabled = r.ZoneExtent is not null;
        Zone = (r.ZoneExtent ?? 0.3) * 1000;
        SwLayout = r.IsCross ? "cross" : "uniform";
        SwByRows = r.ByRows;
        FirstRow = (r.FirstRow ?? 0.07) * 1000; RowStep = r.RowStep * 1000; RowCount = r.RowCount;
        CrossXLines = r.CrossX.Lines; CrossXStep = r.CrossX.Step * 1000; CrossXPos = r.CrossX.Positive; CrossXNeg = r.CrossX.Negative;
        CrossYLines = r.CrossY.Lines; CrossYStep = r.CrossY.Step * 1000; CrossYPos = r.CrossY.Positive; CrossYNeg = r.CrossY.Negative;
        RswManual = r.RswOverride is not null;
        RswManualValue = (r.RswOverride ?? 170000) / 1000;
        var opt = p.Options;
        EccentricityX = opt.EccentricityX;
        EccentricityY = opt.EccentricityY;
        Wb = opt.Wb;
        Pylon = opt.Pylon;
        WallFigure = opt.WallFigure == "amd2" ? "amd2" : "amd1";
        HoleInfluence = opt.HoleInfluence;
        PylonReduced = opt.PylonReduced;
        AngleDeg = p.AngleDeg;
        EdgeInfluence = p.EdgeInfluence;
        WallRatio = p.WallRatio;
        _loading = false;

        FindJointsCommand = new RelayCommand(_ => FindJoints(), _ => _schema != null);
        IncludeAllCommand = new RelayCommand(_ => { foreach (var j in Joints) j.Include = true; });
        ExcludeAllCommand = new RelayCommand(_ => { foreach (var j in Joints) j.Include = false; });
        LiraOnlyCommand = new RelayCommand(_ => { foreach (var j in Joints) j.Include = j.Lira != null; }, _ => HasLiraJoints);
        AttachAspCommand = new RelayCommand(_ =>
        {
            if (_schema != null && AttachLiraAsp?.Invoke(_schema) == true) FindJoints();
        }, _ => _schema != null && AttachLiraAsp != null && !_isFragment);
        LiraSetsCommand = new RelayCommand(async _ =>
        {
            if (_schema == null || CreateLiraForceSets == null || _liraBusy) return;
            _liraBusy = true;
            CommandManager.InvalidateRequerySuggested();
            try { await CreateLiraForceSets(_schema, ToParams(), LiraSetJoints()); }
            finally
            {
                _liraBusy = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }, _ => _schema != null && CreateLiraForceSets != null && !_liraBusy && !_isFragment && _analysis?.Kind == FragmentMaskSet.SourceLira
               && LiraSetJoints().Count > 0);
        if (_schema != null) FindJoints();
    }

    public FemCheck? Existing { get; }
    /// <summary>Схему можно выбрать только у новой проверки.</summary>
    public bool CanChangeSchema => Existing == null;
    public IReadOnlyList<FemSchema> Schemas { get; }
    public IReadOnlyList<Material> Concretes { get; }
    public IReadOnlyList<Material> Rebars { get; }
    public ObservableCollection<PunchingFemAnalysisItem> Analyses { get; } = [];
    public ObservableCollection<PunchingFemJointPickVM> Joints { get; } = [];

    public IReadOnlyList<PunchingChoice> Methods { get; } =
    [
        new("auto", Loc.S("PunchingFemMethodAuto", "авто: CSfea — разрез, импорт — колонны")),
        new("contour", Loc.S("PunchingFemMethodContour", "по разрезу основания (силы КЭ)")),
        new("column", Loc.S("PunchingFemMethodColumn", "по колоннам минус нагрузка основания")),
    ];
    public IReadOnlyList<PunchingChoice> EccentricityModes { get; } =
    [
        new("loading", Loc.S("PunchingEcc_loading", "догружающий")), new("unloading", Loc.S("PunchingEcc_unloading", "разгружающий")),
        new("auto", Loc.S("PunchingEcc_auto", "по знаку")), new("ignore", Loc.S("PunchingEcc_ignore", "не учитывать"))
    ];
    public IReadOnlyList<PunchingChoice> WbModes { get; } =
        [new("minimum", Loc.S("PunchingWb_minimum", "наименьший")), new("stress_side", Loc.S("PunchingWb_stress_side", "по стороне напряжений"))];
    public IReadOnlyList<PunchingChoice> PylonModes { get; } =
        [new("auto", Loc.S("PunchingPylon_auto", "автоматически (Lmax > 2Lmin + h0)")), new("off", Loc.S("PunchingPylon_off", "не рассматривать"))];
    /// <summary>Редакция рис. 6.2 СП 430 для углов стен.</summary>
    public IReadOnlyList<PunchingChoice> WallFigures { get; } =
    [
        new("amd1", Loc.S("PunchingWallFigure_amd1", "СП 430 с Изм. № 1 (действующая)")),
        new("amd2", Loc.S("PunchingWallFigure_amd2", "проект Изм. № 2 (справочно)"))
    ];

    // ── Поля ───────────────────────────────────────────────────────────────

    string _tag = "", _method = "auto", _eccX = "loading", _eccY = "loading", _wb = "minimum", _pylon = "auto", _wallFigure = "amd1";
    bool _mloc = true, _concreteFromSchema = true, _rbtManual, _swEnabled, _zoneEnabled, _rswManual, _pylonReduced;
    double _rbtManualValue, _defaultAx, _defaultAy, _diameter, _step, _zone, _rswManualValue, _holeInfluence = 6, _angle = 10, _edgeInfluence = 6, _wallRatio = 4;
    int _count = 2;
    Material? _concrete, _rebar;

    public string Tag { get => _tag; set => SetField(ref _tag, value); }

    public FemSchema? Schema
    {
        get => _schema;
        set
        {
            if (!SetField(ref _schema, value)) return;
            RefreshAnalyses(null);
            Joints.Clear();
            if (value != null) FindJoints();
        }
    }

    public PunchingFemAnalysisItem? Analysis { get => _analysis; set => SetField(ref _analysis, value); }
    public bool HasNoAnalyses => Analyses.Count == 0;

    public string Method { get => _method; set => SetField(ref _method, value); }
    public bool MomentsAreMloc { get => _mloc; set => SetField(ref _mloc, value); }
    public bool ConcreteFromSchema { get => _concreteFromSchema; set { if (SetField(ref _concreteFromSchema, value)) Preview(); } }
    public Material? Concrete { get => _concrete; set { if (SetField(ref _concrete, value)) Preview(); } }
    public bool RbtManual { get => _rbtManual; set { if (SetField(ref _rbtManual, value)) Preview(); } }
    /// <summary>Rbt вручную, МПа.</summary>
    public double RbtManualValue { get => _rbtManualValue; set { if (SetField(ref _rbtManualValue, value)) Preview(); } }
    /// <summary>Привязки по умолчанию, мм.</summary>
    public double DefaultAx { get => _defaultAx; set { if (SetField(ref _defaultAx, value)) Refind(); } }
    public double DefaultAy { get => _defaultAy; set { if (SetField(ref _defaultAy, value)) Refind(); } }
    public bool SwEnabled { get => _swEnabled; set { if (SetField(ref _swEnabled, value)) Preview(); } }
    public Material? Rebar { get => _rebar; set { if (SetField(ref _rebar, value)) Preview(); } }
    public double Diameter { get => _diameter; set { if (SetField(ref _diameter, value)) Preview(); } }
    public int Count { get => _count; set { if (SetField(ref _count, value)) Preview(); } }
    public double Step { get => _step; set { if (SetField(ref _step, value)) Preview(); } }
    public bool ZoneEnabled { get => _zoneEnabled; set { if (SetField(ref _zoneEnabled, value)) Preview(); } }
    public double Zone { get => _zone; set { if (SetField(ref _zone, value)) Preview(); } }
    public bool RswManual { get => _rswManual; set { if (SetField(ref _rswManual, value)) Preview(); } }

    /// <summary>Раскладки поперечной арматуры.</summary>
    public IReadOnlyList<PunchingChoice> SwLayouts { get; } =
    [
        new("uniform", Loc.S("PunchingSwLayout_uniform", "равномерно вдоль контура")),
        new("cross", Loc.S("PunchingSwLayout_cross", "крестообразно у осей площадки (рис. 8.11г)"))
    ];
    string _swLayout = "uniform";
    bool _swByRows, _crossXPos = true, _crossXNeg = true, _crossYPos = true, _crossYNeg = true;
    double _firstRow = 70, _rowStep = 60, _crossXStep = 100, _crossYStep = 100;
    int _rowCount = 5, _crossXLines = 3, _crossYLines = 3;
    public string SwLayout { get => _swLayout; set { if (SetField(ref _swLayout, value)) { OnPropertyChanged(nameof(IsSwCross)); OnPropertyChanged(nameof(IsSwUniform)); Preview(); } } }
    public bool IsSwCross => SwLayout == "cross";
    public bool IsSwUniform => !IsSwCross;
    /// <summary>Ряды поперёк контура (проверка п. 10.3.17); при включении — по h0 выбранного узла (с капителью — h0 + hc).</summary>
    public bool SwByRows
    {
        get => _swByRows;
        set
        {
            if (value && !_swByRows && !_loading)
            {
                var (s1, sd, nr) = PunchingParamsVM.DefaultRows(_selectedJoint?.SwH0 ?? 0.2);
                _firstRow = s1 * 1000; _rowStep = sd * 1000; _rowCount = nr;
                OnPropertyChanged(nameof(FirstRow)); OnPropertyChanged(nameof(RowStep)); OnPropertyChanged(nameof(RowCount));
            }
            if (SetField(ref _swByRows, value)) { OnPropertyChanged(nameof(SwLegacy)); Preview(); }
        }
    }
    public bool SwLegacy => !SwByRows;
    /// <summary>s1, sd, мм; число рядов.</summary>
    public double FirstRow { get => _firstRow; set { if (SetField(ref _firstRow, value)) Preview(); } }
    public double RowStep { get => _rowStep; set { if (SetField(ref _rowStep, value)) Preview(); } }
    public int RowCount { get => _rowCount; set { if (SetField(ref _rowCount, value)) Preview(); } }
    /// <summary>Лучи креста вдоль осей узла (местные оси сечения колонны): линии, шаг (мм), стороны.</summary>
    public int CrossXLines { get => _crossXLines; set { if (SetField(ref _crossXLines, value)) { OnPropertyChanged(nameof(CrossXWidthText)); Preview(); } } }
    public double CrossXStep { get => _crossXStep; set { if (SetField(ref _crossXStep, value)) { OnPropertyChanged(nameof(CrossXWidthText)); Preview(); } } }
    public bool CrossXPos { get => _crossXPos; set { if (SetField(ref _crossXPos, value)) Preview(); } }
    public bool CrossXNeg { get => _crossXNeg; set { if (SetField(ref _crossXNeg, value)) Preview(); } }
    public int CrossYLines { get => _crossYLines; set { if (SetField(ref _crossYLines, value)) { OnPropertyChanged(nameof(CrossYWidthText)); Preview(); } } }
    public double CrossYStep { get => _crossYStep; set { if (SetField(ref _crossYStep, value)) { OnPropertyChanged(nameof(CrossYWidthText)); Preview(); } } }
    public bool CrossYPos { get => _crossYPos; set { if (SetField(ref _crossYPos, value)) Preview(); } }
    public bool CrossYNeg { get => _crossYNeg; set { if (SetField(ref _crossYNeg, value)) Preview(); } }
    public string CrossXWidthText => string.Format(CultureInfo.CurrentCulture, Loc.S("PunchingCrossWidth", "Lsw = {0:0} мм"), Math.Max(0, CrossXLines - 1) * CrossXStep);
    public string CrossYWidthText => string.Format(CultureInfo.CurrentCulture, Loc.S("PunchingCrossWidth", "Lsw = {0:0} мм"), Math.Max(0, CrossYLines - 1) * CrossYStep);
    public double RswManualValue { get => _rswManualValue; set { if (SetField(ref _rswManualValue, value)) Preview(); } }
    public string EccentricityX { get => _eccX; set => SetField(ref _eccX, value); }
    public string EccentricityY { get => _eccY; set => SetField(ref _eccY, value); }
    public string Wb { get => _wb; set => SetField(ref _wb, value); }
    public string Pylon { get => _pylon; set { if (SetField(ref _pylon, value)) Preview(); } }
    /// <summary>Редакция рис. 6.2 у углов стен: "amd1" | "amd2"; меняет основание пирамиды узла — поиск заново.</summary>
    public string WallFigure { get => _wallFigure; set { if (SetField(ref _wallFigure, value)) Refind(); } }
    public double HoleInfluence { get => _holeInfluence; set { if (SetField(ref _holeInfluence, value)) Refind(); } }
    public bool PylonReduced { get => _pylonReduced; set => SetField(ref _pylonReduced, value); }
    public double AngleDeg { get => _angle; set { if (SetField(ref _angle, value)) Refind(); } }
    public double EdgeInfluence { get => _edgeInfluence; set { if (SetField(ref _edgeInfluence, value)) Refind(); } }
    public double WallRatio { get => _wallRatio; set { if (SetField(ref _wallRatio, value)) Refind(); } }

    public PunchingFemJointPickVM? SelectedJoint { get => _selectedJoint; set { if (SetField(ref _selectedJoint, value)) Preview(); } }

    /// <summary>Сводка поиска узлов.</summary>
    public string JointsSummary { get; private set; } = "";
    /// <summary>Сцена живого рисунка выбранного узла (без усилий — только контуры).</summary>
    public PunchingScene? Scene { get; private set; }
    /// <summary>Пояснение к рисунку или ошибка исходных данных узла.</summary>
    public string PreviewText { get; private set; } = "";
    public string ErrorText { get; private set; } = "";
    public bool HasErrors => ErrorText.Length > 0;

    public ICommand FindJointsCommand { get; }
    public ICommand IncludeAllCommand { get; }
    public ICommand ExcludeAllCommand { get; }
    /// <summary>Включить в проверку только узлы, которые считала ЛИРА (по приложенному *.asp).</summary>
    public ICommand LiraOnlyCommand { get; }
    /// <summary>Приложить к схеме *.asp ЛИРЫ (та же загрузка, что в меню схемы).</summary>
    public ICommand AttachAspCommand { get; }

    /// <summary>Загрузка *.asp к схеме (окно выбора файла и сверка, как в меню схемы); true — файл приложен.</summary>
    public Func<FemSchema, bool>? AttachLiraAsp { get; set; }

    /// <summary>
    /// Наборы усилий продавливания из открытой ЛИРЫ (РСУ, РСН, загружения) и пакетные задачи для включённых узлов с
    /// колоннами; источник проверки — снимок результатов ЛИРЫ.
    /// </summary>
    public ICommand LiraSetsCommand { get; }

    /// <summary>Создание наборов из ЛИРЫ: схема, параметры формы, узлы.</summary>
    public Func<FemSchema, PunchingFemCheckParams, IReadOnlyList<PunchingFemJoint>, Task>? CreateLiraForceSets { get; set; }

    /// <summary>Идёт чтение ЛИРЫ и создание наборов.</summary>
    bool _liraBusy;

    /// <summary>Включённые проверяемые узлы со стержнями колонн.</summary>
    List<PunchingFemJoint> LiraSetJoints() => Joints.Where(j => j.Include && j.Checkable && j.Joint.Bars.Count > 0).Select(j => j.Joint).ToList();

    /// <summary>Строка о *.asp ЛИРЫ: файл и сколько узлов сопоставлено, или подсказка приложить.</summary>
    public string LiraAspText { get; private set; } = "";
    /// <summary>В найденных узлах есть узлы ЛИРЫ.</summary>
    public bool HasLiraJoints => Joints.Any(j => j.Lira != null);

    // ── Логика ─────────────────────────────────────────────────────────────

    void RefreshAnalyses(string? keepRef)
    {
        Analyses.Clear();
        if (_schema != null)
            foreach (var a in _db.GetFemAnalyses(_schema.Id).Where(a => a.Kind is FemCsfeaRunner.LinearAnalysisKind or FemCsfeaRunner.AnalysisKind))
                Analyses.Add(new PunchingFemAnalysisItem(a.Id,
                    a.Tag + (a.ResultId == null ? " " + Loc.S("PunchingFemAnalysisNoResult", "(не рассчитана)") : "")
                         + (a.Kind == FemCsfeaRunner.LinearAnalysisKind ? " — " + Loc.S("PunchingFemLinear", "линейный") : " — " + Loc.S("PunchingFemSecant", "секущий")),
                    a.Kind));
        // Снимки результатов программ (вложения схемы) — источник для импортированных схем.
        if (_schema != null)
            foreach (var (kind, label) in new[] { (FragmentMaskSet.SourceLira, "ЛИРА-САПР"), (FragmentMaskSet.SourceScad, "SCAD") })
                if (_db.GetFemSchemaSourceFile(_schema.Id, StoreyFragmentMaskService.SnapshotFileKind(kind)) is { } file)
                    Analyses.Add(new PunchingFemAnalysisItem(kind == FragmentMaskSet.SourceLira ? -1 : -2,
                        string.Format(CultureInfo.CurrentCulture, Loc.S("PunchingFemSnapshotSource", "Снимок результатов {0} ({1})"), label,
                            file.ImportedAt.Replace('T', ' ')), kind));
        Analysis = Analyses.FirstOrDefault(a => a.Kind == _keepKind && (a.Id < 0 || a.Id.ToString(CultureInfo.InvariantCulture) == keepRef))
                   ?? Analyses.FirstOrDefault(a => a.Id.ToString(CultureInfo.InvariantCulture) == keepRef)
                   ?? Analyses.FirstOrDefault(a => !a.Label.Contains('(') || a.Id < 0) ?? Analyses.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoAnalyses));
    }

    void Refind()
    {
        if (!_loading && _schema != null) FindJoints();
    }

    /// <summary>Поиск узлов схемы с текущими настройками; включение и переопределения узлов сохраняются.</summary>
    public void FindJoints()
    {
        if (_schema == null) return;
        foreach (var j in Joints) if (j.ToOverride(Common()) is { } o) _overrides[j.Node] = o; else _overrides.Remove(j.Node);
        string? keep = _selectedJoint?.Node;
        Joints.Clear();
        var data = PunchingFemService.Load(_db, _schema, ToParams().FinderOptions);
        foreach (var joint in data.Joints)
        {
            var row = new PunchingFemJointPickVM(joint, _overrides.GetValueOrDefault(joint.NodeTag));
            row.Changed += () => { if (row == _selectedJoint) Preview(); };
            Joints.Add(row);
        }
        int noCheck = data.Joints.Count(j => j.NoCheck != null);
        JointsSummary = data.Joints.Count == 0
            ? Loc.S("PunchingFemNoJoints", "Узлов «колонна — плита» не найдено: нужен стержень под углом к горизонтальной плите с концом в её узле или в теле АЖТ.")
            : string.Format(CultureInfo.CurrentCulture, Loc.S("PunchingFemJointsFound", "Найдено узлов: {0}, из них не проверяются по геометрии: {1}, плит: {2}. При расчёте без проверки останутся и узлы без усилий в результате."),
                data.Joints.Count, noCheck, data.Slabs.Count(s => data.Joints.Any(j => j.SlabIndex == s.Index)));
        OnPropertyChanged(nameof(JointsSummary));
        MatchLira(data.Joints);
        SelectedJoint =Joints.FirstOrDefault(j => j.Node == keep) ?? Joints.FirstOrDefault(j => j.Checkable) ?? Joints.FirstOrDefault();
    }

    bool _isFragment;

    /// <summary>Сопоставить найденные узлы с узлами продавливания ЛИРЫ из *.asp схемы.</summary>
    void MatchLira(IReadOnlyList<PunchingFemJoint> joints)
    {
        var c = CultureInfo.CurrentCulture;
        _isFragment = _schema != null && _db.GetFemSchemaFragmentSource(_schema.Id).IsFragment;
        var asp = _schema == null ? null : PunchingFemService.LoadLiraAsp(_db, _schema.Id);
        var match = PunchingFemService.MatchLira(asp?.File, joints);
        foreach (var j in Joints) j.SetLira(match.ByNode.GetValueOrDefault(j.Node));
        LiraAspText = asp switch
        {
            null => _isFragment
                ? Loc.S("PunchingFemLiraNoAspFragment", "Подбор ЛИРЫ (*.asp) не приложен к исходной схеме фрагмента — сверки с ЛИРОЙ нет.")
                : Loc.S("PunchingFemLiraNoAsp", "Подбор ЛИРЫ (*.asp) к схеме не приложен — приложите его, чтобы сверить узлы с ЛИРОЙ."),
            { Error: { } e } => string.Format(c, Loc.S("PunchingFemLiraAspError", "Подбор ЛИРЫ {0} не прочитан: {1}"), asp.Value.FileName, e),
            { File.Punching.Count: 0 } => string.Format(c, Loc.S("PunchingFemLiraAspNoPunching", "В подборе ЛИРЫ {0} нет узлов продавливания."), asp.Value.FileName),
            _ => string.Format(c, Loc.S("PunchingFemLiraAspMatched", "ЛИРА ({0}): узлов продавливания {1}, сопоставлено {2}{3}."),
                asp.Value.FileName, asp.Value.File!.Punching.Count, match.ByNode.Count,
                match.Unmatched.Count > 0 ? string.Format(c, Loc.S("PunchingFemLiraAspUnmatched", ", без пары {0}"), match.Unmatched.Count) : ""),
        };
        OnPropertyChanged(nameof(LiraAspText));
        OnPropertyChanged(nameof(HasLiraJoints));
        CommandManager.InvalidateRequerySuggested();
    }

    PunchingReinforcementParams Common() => new()
    {
        Enabled = SwEnabled, MaterialId = Rebar?.Id ?? 0, Diameter = Diameter / 1000, Count = Count, Step = Step / 1000,
        ZoneExtent = ZoneEnabled ? Zone / 1000 : null, RswOverride = RswManual ? RswManualValue * 1000 : null,
        Layout = SwLayout, FirstRow = SwByRows ? FirstRow / 1000 : null, RowStep = RowStep / 1000, RowCount = RowCount,
        CrossX = new PunchingCrossArmParams { Lines = CrossXLines, Step = CrossXStep / 1000, Positive = CrossXPos, Negative = CrossXNeg },
        CrossY = new PunchingCrossArmParams { Lines = CrossYLines, Step = CrossYStep / 1000, Positive = CrossYPos, Negative = CrossYNeg },
    };

    /// <summary>Параметры проверки по форме.</summary>
    public PunchingFemCheckParams ToParams()
    {
        var common = Common();
        var overrides = new Dictionary<string, PunchingFemJointOverride>(_overrides, StringComparer.Ordinal);
        foreach (var j in Joints)
            if (j.ToOverride(common) is { } o) overrides[j.Node] = o;
            else overrides.Remove(j.Node);
        return new PunchingFemCheckParams
        {
            SourceKind = _analysis?.Kind switch
            {
                FemCsfeaRunner.AnalysisKind => FragmentMaskSet.SourceCsfeaSecant,
                FragmentMaskSet.SourceLira => FragmentMaskSet.SourceLira,
                FragmentMaskSet.SourceScad => FragmentMaskSet.SourceScad,
                _ => FragmentMaskSet.SourceCsfeaLinear,
            },
            SourceRef = _analysis is { Id: > 0 } a ? a.Id.ToString(CultureInfo.InvariantCulture) : "",
            Method = Method, MomentsAreMloc = MomentsAreMloc, ConcreteFromSchema = ConcreteFromSchema,
            ConcreteMaterialId = Concrete?.Id ?? 0, RbtOverride = RbtManual ? RbtManualValue * 1000 : null,
            DefaultAx = DefaultAx / 1000, DefaultAy = DefaultAy / 1000, Reinforcement = common,
            Options = new PunchingOptionParams
            {
                EccentricityX = EccentricityX, EccentricityY = EccentricityY, Wb = Wb, Pylon = Pylon, HoleInfluence = HoleInfluence,
                PylonReduced = PylonReduced, WallFigure = WallFigure,
            },
            AngleDeg = AngleDeg, EdgeInfluence = EdgeInfluence, WallRatio = WallRatio,
            Overrides = overrides.Values.OrderBy(o => o.Node, FemNodeRigidBodies.TagComparer).ToList(),
        };
    }

    /// <summary>Проверка формы: коды ошибок для сохранения; пусто — можно сохранять.</summary>
    public List<string> Validate()
    {
        var e = new List<string>();
        if (_schema == null) e.Add(Loc.S("PunchingFemErrNoSchema", "Не выбрана схема."));
        if (_analysis == null) e.Add(Loc.S("PunchingFemErrNoAnalysis", "Не выбран источник усилий: расчёт CSfea схемы или снимок результатов ЛИРА-САПР/SCAD."));
        if (!RbtManual && Concrete == null && !ConcreteFromSchema) e.Add(Loc.S("PunchingErr_concrete_missing", "Не выбран бетон."));
        if (SwEnabled)
            foreach (var code in new PunchingTaskParams { Reinforcement = Common() }.Validate().Where(c => c.StartsWith("sw_", StringComparison.Ordinal)).Distinct())
                e.Add(Loc.S("PunchingErr_" + code, "Неверные параметры поперечной арматуры."));
        if (!(DefaultAx >= 0 && DefaultAy >= 0)) e.Add(Loc.S("PunchingErr_h0_nonpositive", "Неверные привязки."));
        return e;
    }

    /// <summary>Записывает форму в проверку.</summary>
    public void ApplyTo(FemCheck check)
    {
        check.SchemaId = _schema?.Id ?? check.SchemaId;
        check.MemberId = 0;
        check.ElementId = null;
        check.NormCode = PunchingFemCheckParams.NormCode;
        check.Tag = Tag;
        check.ParamsJson = ToParams().ToJson();
        check.ForceSetIdsJson = "[]";
    }

    /// <summary>Живой рисунок выбранного узла: контуры по геометрии узла с параметрами формы, без усилий.</summary>
    void Preview()
    {
        if (_loading) return;
        Scene = null;
        ErrorText = "";
        PreviewText = "";
        if (_selectedJoint is not { } pick)
        {
            Raise();
            return;
        }
        var joint = pick.Joint;
        if (joint.NoCheck != null)
        {
            ErrorText = pick.StatusText;
            Raise();
            return;
        }
        var p = ToParams();
        var zero = new PunchingFemForces(0, PunchingLoadSide.FromBelow, 0, 0, PunchingFemForceMethod.Column, null, null, null);
        var tp = PunchingFemService.TaskParams(joint, zero, p, _schema?.Tag ?? "", "");
        var errors = tp.Validate();
        var strengths = PunchingRunner.ResolveStrengths(tp, _materials, CalcType.C, out var materialErrors);
        errors.AddRange(materialErrors);
        if (errors.Count > 0)
            ErrorText = string.Join("\n", errors.Distinct().Select(code => Loc.S("PunchingErr_" + code, code)));
        try
        {
            var input = tp.ToInput(Math.Max(strengths.Rbt, 1), strengths.Rsw);
            // Контуры ядра по геометрии узла без усилий: Кисп неизвестен до проверки — NaN (нейтральный цвет, без Кисп в подсказке).
            var geometryOnly = PunchingScene.From(input, PunchingChecker.Check(input, includeTrace: false));
            Scene = geometryOnly with { Variants = geometryOnly.Variants.Select(v => v with { Utilization = double.NaN, Governs = false }).ToList() };
            var c = CultureInfo.CurrentCulture;
            PreviewText = string.Format(c, Loc.S("PunchingFemPreview",
                    "Узел {0}: {1}, h = {2:0} мм, h0 = {3:0.#} мм, ax = {4:0}, ay = {5:0} мм{6}; бетон {7}. Усилия — из расчёта, Кисп — в результате проверки."),
                joint.NodeTag, joint.SectionLabel, tp.Slab.H * 1000, tp.H0 * 1000, tp.Slab.Ax * 1000, tp.Slab.Ay * 1000,
                joint.CoverDefault && pick.Ax == null ? " " + Loc.S("PunchingFemCoverDefault", "(по умолчанию)") : "",
                strengths.ConcreteTag.Length > 0 ? strengths.ConcreteTag : RbtManual ? Loc.S("PunchingManualValue", "задано вручную") : "—");
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        Raise();
    }

    bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    void Raise()
    {
        OnPropertyChanged(nameof(Scene));
        OnPropertyChanged(nameof(PreviewText));
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(HasErrors));
    }
}
