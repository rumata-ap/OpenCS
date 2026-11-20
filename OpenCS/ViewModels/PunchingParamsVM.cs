using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CScore;
using CScore.Sp63Punching;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Пункт списка выбора: код в параметрах и подпись.</summary>
public sealed record PunchingChoice(string Id, string Label)
{
    /// <inheritdoc/>
    public override string ToString() => Label;
}

/// <summary>Строка с уведомлением об изменении для таблиц окна параметров.</summary>
public abstract class PunchingEditableRow : INotifyPropertyChanged
{
    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Устанавливает значение и уведомляет.</summary>
    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>Отверстие в окне параметров (размеры в мм).</summary>
public sealed class PunchingHoleRowVM : PunchingEditableRow
{
    string _shape = "rectangle";
    double _cx, _cy, _bx = 200, _by = 200, _d = 200;
    /// <summary>"rectangle" | "circle".</summary>
    public string Shape { get => _shape; set => Set(ref _shape, value); }
    public double Cx { get => _cx; set => Set(ref _cx, value); }
    public double Cy { get => _cy; set => Set(ref _cy, value); }
    public double Bx { get => _bx; set => Set(ref _bx, value); }
    public double By { get => _by; set => Set(ref _by, value); }
    public double D { get => _d; set => Set(ref _d, value); }
    /// <summary>Вершины отверстия-многоугольника из схемы МКЭ, м (только "polygon"; в окне не правятся).</summary>
    public List<double[]> Points { get; init; } = [];
}

/// <summary>Вершина многоугольной площади (мм).</summary>
public sealed class PunchingPointRowVM : PunchingEditableRow
{
    double _x, _y;
    public double X { get => _x; set => Set(ref _x, value); }
    public double Y { get => _y; set => Set(ref _y, value); }
}

/// <summary>
/// Окно параметров задачи «Продавливание плиты (СП 63)». Поля — в мм, кН, кН·м, МПа; каждое изменение
/// пересчитывает узел ядром (<see cref="Preview"/>). При ошибке ввода рисунок показывает последнее
/// корректное состояние, а ошибки — в <see cref="ErrorText"/>.
/// </summary>
public sealed class PunchingParamsVM : ViewModelBase
{
    readonly CalcTask _task;
    readonly IReadOnlyList<Material> _materials;
    readonly LoadItem? _forceRow;
    readonly IReadOnlyList<LoadItem>? _batchRows;
    readonly string _batchSetTag;
    readonly List<PunchingCustomContourParams> _customContours;
    readonly bool _customReplacesMain;
    bool _loading;

    /// <summary>Создаёт VM по задаче и данным проекта.</summary>
    /// <param name="task">Задача «punching» или пакет «punching_batch».</param>
    /// <param name="materials">Материалы проекта.</param>
    /// <param name="plateSections">Плитные сечения проекта.</param>
    /// <param name="forceRow">Строка набора одиночной задачи (источник усилий) или null.</param>
    /// <param name="batchRows">Строки набора пакета; живой рисунок — по определяющей строке.</param>
    /// <param name="batchSetTag">Имя набора пакета.</param>
    public PunchingParamsVM(CalcTask task, IEnumerable<Material> materials, IEnumerable<PlateSection> plateSections, LoadItem? forceRow,
        IReadOnlyList<LoadItem>? batchRows = null, string batchSetTag = "")
    {
        _task = task;
        _materials = materials.ToList();
        _forceRow = task.Kind == PunchingBatchRunner.Kind ? null : forceRow;
        _batchRows = batchRows?.OrderBy(i => i.Num).ToList();
        _batchSetTag = batchSetTag;
        Concretes = [.. _materials.Where(m => m.Type == MatType.Concrete)];
        Rebars = [.. _materials.Where(m => m.Type is MatType.ReSteelF or MatType.ReSteelU)];
        PlateSections = [.. plateSections];

        var p = PunchingTaskParams.Parse(task.ParamsJson);
        _customContours = p.CustomContours;
        _customReplacesMain = p.Options.CustomReplacesMain;
        _source = p.Source;
        bool isNew = string.IsNullOrWhiteSpace(task.ParamsJson) || task.ParamsJson.Trim() == "{}";
        if (isNew)
        {
            // Новая задача: бетон и арматура — первые в проекте, усилия — из строки набора, если выбрана.
            // Поперечная арматура новой задачи — рядами по п. 10.3.17 (проверка конструктивных требований и подбор).
            var (s1, sd, nr) = DefaultRows(p.H0);
            p = p with
            {
                Slab = p.Slab with { ConcreteMaterialId = Concretes.FirstOrDefault()?.Id ?? 0 },
                Reinforcement = p.Reinforcement with
                {
                    MaterialId = Rebars.FirstOrDefault()?.Id ?? 0, FirstRow = s1, RowStep = sd, RowCount = nr, Step = sd,
                    CrossX = p.Reinforcement.CrossX with { Step = sd }, CrossY = p.Reinforcement.CrossY with { Step = sd }
                }
            };
            if (_forceRow is not null)
                p = p with { Forces = p.Forces with { F = Math.Abs(_forceRow.N), Mx = _forceRow.Mx, My = _forceRow.My, FCapital = null } };
        }

        Holes.CollectionChanged += RowsChanged;
        Points.CollectionChanged += RowsChanged;
        Load(p);

        AddHoleCommand = new RelayCommand(_ => Holes.Add(new PunchingHoleRowVM { Cy = Math.Round(1000 * (MaxHalfSize() + 2 * H0Value()) + 100) }));
        RemoveHoleCommand = new RelayCommand(_ => { if (SelectedHole is not null) Holes.Remove(SelectedHole); }, _ => SelectedHole is not null);
        AddPointCommand = new RelayCommand(_ => Points.Add(new PunchingPointRowVM()));
        RemovePointCommand = new RelayCommand(_ => { if (SelectedPoint is not null) Points.Remove(SelectedPoint); }, _ => SelectedPoint is not null);
        TakeForcesFromRowCommand = new RelayCommand(_ => TakeForcesFromRow(), _ => _forceRow is not null);
        TakeFromPlateSectionCommand = new RelayCommand(_ => TakeFromPlateSection(), _ => SelectedPlateSection is not null);
        DesignCommand = new RelayCommand(_ => ApplyDesign());
        ResetRowsCommand = new RelayCommand(_ => ResetRows());
    }

    /// <summary>
    /// Ряды по п. 10.3.17 для h0, м: первый ряд — h0/3, округлённое вверх до 5 мм (не далее h0/2); шаг — h0/3 и не более 300 мм,
    /// округлённый вниз до 5 мм; рядов — чтобы зона была не меньше 1,5h0.
    /// </summary>
    public static (double FirstRow, double Step, int Count) DefaultRows(double h0)
    {
        if (!(h0 > 0)) return (0.06, 0.06, 5);
        double s1 = Math.Min(Math.Ceiling(h0 / 3 * 200 - 1e-9) / 200, h0 / 2);
        double sd = Math.Max(0.01, Math.Floor(Math.Min(h0 / 3, 0.3) * 200 + 1e-9) / 200);
        int n = Math.Max(2, (int)Math.Ceiling((1.5 * h0 - s1) / sd - 1e-9) + 1);
        return (s1, sd, n);
    }

    // ── Списки ────────────────────────────────────────────────────────────

    /// <summary>Бетоны проекта.</summary>
    public IReadOnlyList<Material> Concretes { get; }
    /// <summary>Арматура проекта.</summary>
    public IReadOnlyList<Material> Rebars { get; }
    /// <summary>Плитные сечения проекта (источник h, привязок и бетона).</summary>
    public IReadOnlyList<PlateSection> PlateSections { get; }

    /// <summary>Формы площади.</summary>
    public IReadOnlyList<PunchingChoice> AreaShapes { get; } =
    [
        new("rectangle", Loc.S("PunchingShape_rectangle", "прямоугольник")), new("circle", Loc.S("PunchingShape_circle", "круг")),
        new("ring", Loc.S("PunchingShape_ring", "кольцо")), new("polygon", Loc.S("PunchingShape_polygon", "многоугольник"))
    ];
    /// <summary>Вид узла: колонна или пилон, торец стены, угол стен (СП 430 п. 6.3.8).</summary>
    public IReadOnlyList<PunchingChoice> NodeKinds { get; } =
    [
        new("none", Loc.S("PunchingNodeKind_none", "колонна / пилон")),
        new("end", Loc.S("PunchingNodeKind_end", "торец стены (СП 430, рис. 6.1)")),
        new("corner", Loc.S("PunchingNodeKind_corner", "угол стен 90° (СП 430, рис. 6.2)")),
        new("hanging", Loc.S("PunchingNodeKind_hanging", "плита подвешена к торцу стены (СП 430, рис. 6.3)"))
    ];
    /// <summary>Длина ветвей П-контура подвешенной плиты.</summary>
    public IReadOnlyList<PunchingChoice> HangerLegsChoices { get; } =
    [
        new("a", Loc.S("PunchingHangerLegs_a", "a1 — по рис. 6.3")),
        new("a_h0", Loc.S("PunchingHangerLegs_a_h0", "a1 + h0 — как у торца (рис. 6.1)"))
    ];
    /// <summary>Редакция рис. 6.2 СП 430 для угла стен.</summary>
    public IReadOnlyList<PunchingChoice> WallFigures { get; } =
    [
        new("amd1", Loc.S("PunchingWallFigure_amd1", "СП 430 с Изм. № 1 (действующая)")),
        new("amd2", Loc.S("PunchingWallFigure_amd2", "проект Изм. № 2 (справочно)"))
    ];
    /// <summary>Формы отверстий.</summary>
    public IReadOnlyList<PunchingChoice> HoleShapes { get; } =
        [new("rectangle", Loc.S("PunchingShape_rectangle", "прямоугольник")), new("circle", Loc.S("PunchingShape_circle", "круг")),
         new("polygon", Loc.S("PunchingShape_hole_polygon", "контур из схемы"))];
    /// <summary>Положение узла.</summary>
    public IReadOnlyList<PunchingChoice> Positions { get; } =
    [
        new("inside", Loc.S("PunchingPosition_inside", "внутри плиты")), new("edge", Loc.S("PunchingPosition_edge", "у края")),
        new("corner", Loc.S("PunchingPosition_corner", "в углу"))
    ];
    /// <summary>Все стороны площади.</summary>
    static readonly PunchingChoice[] AllSides = [new("+x", "+X"), new("-x", "−X"), new("+y", "+Y"), new("-y", "−Y")];
    /// <summary>Стороны, у которых можно задать край: у узла стены — только допустимые для торца или угла.</summary>
    public IReadOnlyList<PunchingChoice> Sides => [.. AllSides.Where(s => PunchingWallParams.EdgeSides(NodeKind).Contains(s.Id))];
    /// <summary>Сторона приложения нагрузки.</summary>
    public IReadOnlyList<PunchingChoice> LoadSides { get; } =
    [
        new("from_below", Loc.S("PunchingLoadSide_from_below", "колонна под плитой (растянута верхняя грань)")),
        new("from_above", Loc.S("PunchingLoadSide_from_above", "нагрузка сверху (растянута нижняя грань)"))
    ];
    /// <summary>Учёт эксцентриситета.</summary>
    public IReadOnlyList<PunchingChoice> EccentricityModes { get; } =
    [
        new("loading", Loc.S("PunchingEcc_loading", "догружающий")), new("unloading", Loc.S("PunchingEcc_unloading", "разгружающий")),
        new("auto", Loc.S("PunchingEcc_auto", "по знаку")), new("ignore", Loc.S("PunchingEcc_ignore", "не учитывать"))
    ];
    /// <summary>Момент сопротивления контура.</summary>
    public IReadOnlyList<PunchingChoice> WbModes { get; } =
        [new("minimum", Loc.S("PunchingWb_minimum", "наименьший")), new("stress_side", Loc.S("PunchingWb_stress_side", "по стороне напряжений"))];
    /// <summary>Режим пилона.</summary>
    public IReadOnlyList<PunchingChoice> PylonModes { get; } =
        [new("auto", Loc.S("PunchingPylon_auto", "автоматически (Lmax > 2Lmin + h0)")), new("off", Loc.S("PunchingPylon_off", "не рассматривать"))];

    // ── Поля формы ─────────────────────────────────────────────────────────

    string _areaShape = "rectangle";
    double _bx, _by, _d, _t, _h, _ax, _ay, _rbtManualValue, _diameter, _step, _zone, _rswManualValue, _f, _mx, _my, _q, _holeInfluence, _muPercent;
    int _count;
    bool _rbtManual, _swEnabled, _zoneEnabled, _rswManual, _mloc, _pylonReduced, _muEnabled;
    string _position = "inside", _edge1Side = "+y", _edge2Side = "+x", _loadSide = "from_below";
    double _edge1C, _edge1Angle, _edge2C, _edge2Angle;
    string _eccX = "loading", _eccY = "loading", _wb = "minimum", _pylon = "auto", _wallFigure = "amd1";
    Material? _concrete, _rebar;
    PlateSection? _plateSection;
    PunchingHoleRowVM? _selectedHole;
    PunchingPointRowVM? _selectedPoint;

    string _nodeKind = "none", _hangerLegs = "a";
    double _wallA1, _wallA2, _wallMPlus, _wallMMinus, _hangerWidth, _hangerOffset;

    /// <summary>Вид узла: "none" | "end" | "corner" | "hanging".</summary>
    public string NodeKind
    {
        get => _nodeKind;
        set
        {
            if (_nodeKind == value) return;
            bool wasLoading = _loading;
            _loading = true;
            try
            {
                SetField(ref _nodeKind, value);
                // Края ещё не заданы (узел внутри плиты) — сторона по умолчанию для нового вида узла: у торца и
                // подвешенной плиты — за торцом (+X, контур 1′), у угла — снаружи (−X). Заданный край не трогаем.
                if (!wasLoading && Position == "inside")
                    SetField(ref _edge1Side, DefaultEdge1Side(value), nameof(Edge1Side));
                NormalizeEdgeSides();
            }
            finally { _loading = wasLoading; }
            foreach (var n in new[] { nameof(IsWallNode), nameof(IsWallEnd), nameof(IsWallCorner), nameof(IsHanging), nameof(IsColumnNode), nameof(Sides),
                         nameof(ShowMy), nameof(ShowMlocOption), nameof(MxLabel), nameof(MxHint), nameof(BatchForcesText), nameof(ShowWallHint),
                         nameof(ShowMx), nameof(Edge1Side), nameof(Edge2Side), nameof(ShowFCapital) })
                OnPropertyChanged(n);
            if (!_loading) Recalculate();
        }
    }
    /// <summary>Узел стены (торец, угол или подвешенная плита).</summary>
    public bool IsWallNode => NodeKind is "end" or "corner" or "hanging";
    /// <summary>Торец стены, в том числе с подвешенной плитой: П-контур, ΔM, контур 1′.</summary>
    public bool IsWallEnd => NodeKind is "end" or "hanging";
    /// <summary>Плита подвешена к торцу стены (СП 430, рис. 6.3).</summary>
    public bool IsHanging => NodeKind == "hanging";
    /// <summary>Расстояние между осями крайних рядов подвешивающих стержней поперёк стены, мм.</summary>
    public double HangerWidth { get => _hangerWidth; set => SetField(ref _hangerWidth, value); }
    /// <summary>Расстояние от торца стены до оси крайнего подвешивающего стержня, мм.</summary>
    public double HangerOffset { get => _hangerOffset; set => SetField(ref _hangerOffset, value); }
    /// <summary>Длина ветвей П-контура подвешенной плиты: "a" | "a_h0".</summary>
    public string HangerLegs { get => _hangerLegs; set => SetField(ref _hangerLegs, value); }
    public bool IsWallCorner => NodeKind == "corner";
    /// <summary>Колонна или пилон — форма площади и настройки пилона.</summary>
    public bool IsColumnNode => !IsWallNode;
    /// <summary>Толщина стены a1 (у угла — стены вдоль X), мм.</summary>
    public double WallA1 { get => _wallA1; set => SetField(ref _wallA1, value); }
    /// <summary>Толщина стены a2 вдоль Y (угол), мм.</summary>
    public double WallA2 { get => _wallA2; set => SetField(ref _wallA2, value); }
    /// <summary>Помощник ΔM: изгибающий момент плиты со стороны +Y от стены, кН·м (не сохраняется).</summary>
    public double WallMPlus { get => _wallMPlus; set { if (SetMoment(ref _wallMPlus, value)) UpdateDeltaM(); } }
    /// <summary>Помощник ΔM: момент со стороны −Y, кН·м (не сохраняется).</summary>
    public double WallMMinus { get => _wallMMinus; set { if (SetMoment(ref _wallMMinus, value)) UpdateDeltaM(); } }
    bool SetMoment(ref double field, double value, [CallerMemberName] string? name = null)
    {
        if (field == value) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
    /// <summary>ΔM = M(+Y) − M(−Y) из помощника.</summary>
    void UpdateDeltaM() { if (!_loading) Mx = _wallMPlus - _wallMMinus; }

    /// <summary>Подпись поля Mx: у торца стены — ΔM.</summary>
    public string MxLabel => IsWallEnd ? "ΔM" : "Mx";
    /// <summary>Подсказка к полю Mx.</summary>
    public string MxHint => IsWallEnd
        ? Loc.S("PunchingDeltaMHint", "Разность изгибающих моментов плиты по обе стороны стены (момент из плоскости стены); в расчёт идёт ½ΔM (СП 430, п. 6.3.8).")
        : "";
    /// <summary>Поле Mx видно (у угла стен моменты не учитываются).</summary>
    public bool ShowMx => !IsWallCorner;
    /// <summary>Поле My видно только у колонны.</summary>
    public bool ShowMy => IsColumnNode;
    /// <summary>Флажок «полные Mloc» — только у колонны.</summary>
    public bool ShowMlocOption => IsColumnNode;
    /// <summary>Подсказка «это стена» у прямоугольной колонны с b/a &gt; 4 (п. 5.1.8 СП 430).</summary>
    public bool ShowWallHint => IsColumnNode && IsRectangle && Math.Min(Bx, By) > 0 && Math.Max(Bx, By) / Math.Min(Bx, By) > 4 + 1e-9;

    /// <summary>Форма площади.</summary>
    public string AreaShape { get => _areaShape; set { if (SetField(ref _areaShape, value)) { OnPropertyChanged(nameof(ShowWallHint)); OnPropertyChanged(nameof(IsRectangle)); OnPropertyChanged(nameof(IsRound)); OnPropertyChanged(nameof(IsRing)); OnPropertyChanged(nameof(IsPolygon)); OnPropertyChanged(nameof(IsNotRound)); } } }
    /// <summary>Площадь не круглая — капитель задаётся прямоугольником со смещением.</summary>
    public bool IsNotRound => !IsRound;

    bool _capitalEnabled;
    string _fCapital = "";
    double _capitalBx, _capitalBy, _capitalD, _capitalHc, _capitalDx, _capitalDy;
    /// <summary>Капитель задана (только у колонны).</summary>
    public bool CapitalEnabled
    {
        get => _capitalEnabled;
        set { if (SetField(ref _capitalEnabled, value)) OnPropertyChanged(nameof(ShowFCapital)); }
    }
    /// <summary>Размеры капители, мм.</summary>
    public double CapitalBx { get => _capitalBx; set => SetField(ref _capitalBx, value); }
    public double CapitalBy { get => _capitalBy; set => SetField(ref _capitalBy, value); }
    public double CapitalD { get => _capitalD; set => SetField(ref _capitalD, value); }
    /// <summary>Высота утолщения под плитой, мм.</summary>
    public double CapitalHc { get => _capitalHc; set => SetField(ref _capitalHc, value); }
    /// <summary>Смещение центра капители от центра колонны, мм.</summary>
    public double CapitalDx { get => _capitalDx; set => SetField(ref _capitalDx, value); }
    public double CapitalDy { get => _capitalDy; set => SetField(ref _capitalDy, value); }
    public bool IsRectangle => AreaShape == "rectangle";
    public bool IsRound => AreaShape is "circle" or "ring";
    public bool IsRing => AreaShape == "ring";
    public bool IsPolygon => AreaShape == "polygon";
    /// <summary>Размеры площади, мм.</summary>
    public double Bx { get => _bx; set { if (SetField(ref _bx, value)) OnPropertyChanged(nameof(ShowWallHint)); } }
    public double By { get => _by; set { if (SetField(ref _by, value)) OnPropertyChanged(nameof(ShowWallHint)); } }
    public double D { get => _d; set => SetField(ref _d, value); }
    public double T { get => _t; set => SetField(ref _t, value); }
    /// <summary>Вершины многоугольника, мм.</summary>
    public ObservableCollection<PunchingPointRowVM> Points { get; } = [];
    public PunchingPointRowVM? SelectedPoint
    {
        get => _selectedPoint;
        set { _selectedPoint = value; OnPropertyChanged(); System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    /// <summary>Толщина плиты и привязки, мм.</summary>
    public double H { get => _h; set => SetField(ref _h, value); }
    public double Ax { get => _ax; set => SetField(ref _ax, value); }
    public double Ay { get => _ay; set => SetField(ref _ay, value); }
    /// <summary>Бетон плиты.</summary>
    public Material? Concrete { get => _concrete; set => SetField(ref _concrete, value); }
    public bool RbtManual { get => _rbtManual; set => SetField(ref _rbtManual, value); }
    /// <summary>Ручное Rbt, МПа.</summary>
    public double RbtManualValue { get => _rbtManualValue; set => SetField(ref _rbtManualValue, value); }
    /// <summary>Плитное сечение — источник для «Взять из сечения».</summary>
    public PlateSection? SelectedPlateSection { get => _plateSection; set { _plateSection = value; OnPropertyChanged(); } }

    /// <summary>Положение: "inside" | "edge" | "corner".</summary>
    public string Position
    {
        get => _position;
        set
        {
            if (_position == value) return;
            bool wasLoading = _loading;
            _loading = true;
            try { SetField(ref _position, value); NormalizeEdgeSides(); }
            finally { _loading = wasLoading; }
            OnPropertyChanged(nameof(HasEdge1)); OnPropertyChanged(nameof(HasEdge2));
            if (!_loading) Recalculate();
        }
    }
    public bool HasEdge1 => Position is "edge" or "corner";
    public bool HasEdge2 => Position == "corner";
    public string Edge1Side
    {
        get => _edge1Side;
        set
        {
            if (_edge1Side == value) return;
            bool wasLoading = _loading;
            _loading = true;
            try { SetField(ref _edge1Side, value); NormalizeEdgeSides(); }
            finally { _loading = wasLoading; }
            if (!_loading) Recalculate();
        }
    }
    /// <summary>Расстояние от площади до края, мм.</summary>
    public double Edge1C { get => _edge1C; set => SetField(ref _edge1C, value); }
    public double Edge1Angle { get => _edge1Angle; set => SetField(ref _edge1Angle, value); }
    public string Edge2Side { get => _edge2Side; set => SetField(ref _edge2Side, value); }
    public double Edge2C { get => _edge2C; set => SetField(ref _edge2C, value); }
    public double Edge2Angle { get => _edge2Angle; set => SetField(ref _edge2Angle, value); }

    /// <summary>Отверстия, мм.</summary>
    public ObservableCollection<PunchingHoleRowVM> Holes { get; } = [];
    public PunchingHoleRowVM? SelectedHole
    {
        get => _selectedHole;
        set { _selectedHole = value; OnPropertyChanged(); System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    /// <summary>Поперечная арматура.</summary>
    public bool SwEnabled { get => _swEnabled; set => SetField(ref _swEnabled, value); }

    /// <summary>Раскладки поперечной арматуры.</summary>
    public IReadOnlyList<PunchingChoice> SwLayouts { get; } =
    [
        new("uniform", Loc.S("PunchingSwLayout_uniform", "равномерно вдоль контура")),
        new("cross", Loc.S("PunchingSwLayout_cross", "крестообразно у осей площадки (рис. 8.11г)"))
    ];
    string _swLayout = "uniform";
    bool _swByRows, _crossXPos = true, _crossXNeg = true, _crossYPos = true, _crossYNeg = true;
    double _firstRow, _rowStep, _crossXStep, _crossYStep;
    int _rowCount, _crossXLines, _crossYLines;
    /// <summary>"uniform" | "cross".</summary>
    public string SwLayout { get => _swLayout; set { if (SetField(ref _swLayout, value)) { OnPropertyChanged(nameof(IsSwCross)); OnPropertyChanged(nameof(IsSwUniform)); OnPropertyChanged(nameof(ShowLegacyCount)); } } }
    public bool IsSwCross => SwLayout == "cross";
    public bool IsSwUniform => !IsSwCross;
    /// <summary>Арматура рядами (s1, sd, nr) — проверка п. 10.3.17 и подбор числа рядов.</summary>
    public bool SwByRows
    {
        get => _swByRows;
        set
        {
            if (value && !_swByRows && !_loading)
            {
                // Включение рядов: s1, sd, nr — по п. 10.3.17 для текущей h0.
                var (s1, sd, nr) = DefaultRows(SwH0Value());
                _loading = true;
                try { FirstRow = Mm(s1); RowStep = Mm(sd); RowCount = nr; }
                finally { _loading = false; }
            }
            if (SetField(ref _swByRows, value)) { OnPropertyChanged(nameof(SwLegacy)); OnPropertyChanged(nameof(ShowLegacyCount)); }
        }
    }
    /// <summary>Старый ввод: n стержней на шаге и граница зоны вручную.</summary>
    public bool SwLegacy => !SwByRows;
    /// <summary>Показывать n стержней на шаге (без рядов).</summary>
    public bool ShowLegacyCount => SwLegacy;
    /// <summary>Расстояние от грани площади до первого ряда s1, мм.</summary>
    public double FirstRow { get => _firstRow; set => SetField(ref _firstRow, value); }
    /// <summary>Шаг рядов sd, мм.</summary>
    public double RowStep { get => _rowStep; set => SetField(ref _rowStep, value); }
    /// <summary>Число рядов.</summary>
    public int RowCount { get => _rowCount; set => SetField(ref _rowCount, value); }

    /// <summary>
    /// Рабочая высота, по которой расставлены ряды при последнем пересчёте, м: ряды «по умолчанию» для неё (s1 и sd
    /// по <see cref="DefaultRows"/>) следуют за сменой h0 + hc; заданные вручную не меняются. 0 — ещё не было пересчёта.
    /// </summary>
    double _rowsH0;

    /// <summary>
    /// Ряды по умолчанию — за рабочей высотой: если s1 и sd совпадают с <see cref="DefaultRows"/> прежней h0 (+ hc), а
    /// она изменилась (включили капитель, сменили hc, h или привязки), они пересчитываются для новой. Число рядов:
    /// прежнее по умолчанию — новое по умолчанию; подобранное или заданное — не меньше прежнего.
    /// </summary>
    void FollowRows()
    {
        double h0 = SwH0Value();
        if (!(h0 > 0)) return;   // промежуточный ввод (пустое h) — базу не сдвигаем
        if (_rowsH0 > 0 && Math.Abs(h0 - _rowsH0) > 1e-9 && SwByRows)
        {
            var (s1o, sdo, nro) = DefaultRows(_rowsH0);
            if (Math.Abs(FirstRow - Mm(s1o)) < 1e-6 && Math.Abs(RowStep - Mm(sdo)) < 1e-6)
            {
                var (s1, sd, nr) = DefaultRows(h0);
                SetRows(s1, sd, RowCount == nro ? nr : Math.Max(RowCount, nr));
            }
        }
        _rowsH0 = h0;
    }

    /// <summary>Записывает ряды без пересчёта (вызывающий пересчитывает сам).</summary>
    void SetRows(double s1, double sd, int count)
    {
        bool was = _loading;
        _loading = true;
        try { FirstRow = Mm(s1); RowStep = Mm(sd); RowCount = count; }
        finally { _loading = was; }
    }

    /// <summary>«По п. 10.3.17»: s1, sd и число рядов — по умолчанию для текущей h0 (с капителью — h0 + hc).</summary>
    public void ResetRows()
    {
        var (s1, sd, nr) = DefaultRows(SwH0Value());
        SetRows(s1, sd, nr);
        _rowsH0 = SwH0Value();
        Recalculate();
    }

    /// <summary>
    /// Подсказка у рядов: заданные ряды не отвечают п. 10.3.17 для текущей h0 (+ hc) — s1 вне [h0/3; h0/2],
    /// sd &gt; min(h0/3; 300 мм) или зона короче 1,5h0. Пусто — отвечают или ряды не заданы.
    /// </summary>
    public string RowsHint { get; private set; } = "";

    /// <summary>Основной контур на h0/2 — заданный (контур ЛИРЫ из проверки по МКЭ).</summary>
    public bool HasCustomMain => _customReplacesMain && _customContours.Count > 0;
    /// <summary>Пояснение к заданному основному контуру: откуда он и что контуры по СП 63 у площади не строятся.</summary>
    public string CustomMainText => !HasCustomMain ? ""
        : _customContours.Any(c => c.Name == PunchingFemCheckRunner.LiraContourName)
            ? Loc.S("PunchingCustomMainLira", "Основной контур на h0/2 — контур ЛИРЫ из *.asp (задан проверкой по МКЭ): замкнутый и незамкнутые контуры по СП 63 у грузовой площади не строятся. В этом окне контур не правится; контуры за арматурой и за капителью — по СП 63.")
            : Loc.S("PunchingCustomMainUser", "Основной контур на h0/2 — заданный контур пользователя: замкнутый и незамкнутые контуры по СП 63 у грузовой площади не строятся. Контуры за арматурой и за капителью — по СП 63.");
    /// <summary>Показывать подсказку и кнопку «По п. 10.3.17».</summary>
    public bool ShowRowsHint => RowsHint.Length > 0;

    void UpdateRowsHint()
    {
        string hint = "";
        double h0 = SwH0Value();
        if (SwEnabled && SwByRows && h0 > 0)
        {
            double s1 = M(FirstRow), sd = M(RowStep), ext = s1 + Math.Max(0, RowCount - 1) * sd;
            const double eps = 1e-6;
            bool bad = s1 < h0 / 3 - eps || s1 > h0 / 2 + eps || (RowCount > 1 && sd > Math.Min(h0 / 3, 0.3) + eps) || ext < 1.5 * h0 - eps;
            if (bad)
            {
                var (ds1, dsd, dnr) = DefaultRows(h0);
                hint = string.Format(CultureInfo.CurrentCulture,
                    Loc.S("PunchingRowsHint", "Ряды не отвечают п. 10.3.17 для h0 = {0:0} мм (s1 от h0/3 до h0/2, sd ≤ h0/3 и 300 мм, зона ≥ 1,5h0). По норме: s1 = {1:0}, sd = {2:0} мм, рядов {3}."),
                    h0 * 1000, ds1 * 1000, dsd * 1000, dnr);
            }
        }
        if (hint == RowsHint) return;
        RowsHint = hint;
        OnPropertyChanged(nameof(RowsHint));
        OnPropertyChanged(nameof(ShowRowsHint));
    }
    /// <summary>Лучи вдоль X: число линий, шаг линий (мм), стороны.</summary>
    public int CrossXLines { get => _crossXLines; set { if (SetField(ref _crossXLines, value)) OnPropertyChanged(nameof(CrossXWidthText)); } }
    public double CrossXStep { get => _crossXStep; set { if (SetField(ref _crossXStep, value)) OnPropertyChanged(nameof(CrossXWidthText)); } }
    public bool CrossXPos { get => _crossXPos; set => SetField(ref _crossXPos, value); }
    public bool CrossXNeg { get => _crossXNeg; set => SetField(ref _crossXNeg, value); }
    /// <summary>Лучи вдоль Y.</summary>
    public int CrossYLines { get => _crossYLines; set { if (SetField(ref _crossYLines, value)) OnPropertyChanged(nameof(CrossYWidthText)); } }
    public double CrossYStep { get => _crossYStep; set { if (SetField(ref _crossYStep, value)) OnPropertyChanged(nameof(CrossYWidthText)); } }
    public bool CrossYPos { get => _crossYPos; set => SetField(ref _crossYPos, value); }
    public bool CrossYNeg { get => _crossYNeg; set => SetField(ref _crossYNeg, value); }
    /// <summary>Ширина лучей Lsw = (n − 1)·sw, текстом.</summary>
    public string CrossXWidthText => string.Format(CultureInfo.CurrentCulture, Loc.S("PunchingCrossWidth", "Lsw = {0:0} мм"), Math.Max(0, CrossXLines - 1) * CrossXStep);
    public string CrossYWidthText => string.Format(CultureInfo.CurrentCulture, Loc.S("PunchingCrossWidth", "Lsw = {0:0} мм"), Math.Max(0, CrossYLines - 1) * CrossYStep);
    /// <summary>Итог последнего подбора («Подобрать»).</summary>
    public string DesignApplyText { get; private set; } = "";

    /// <summary>Следующий <see cref="Recalculate"/> — сразу после «Подобрать»: итог подбора не сбрасывать.</summary>
    bool _keepDesignText;
    public Material? Rebar { get => _rebar; set => SetField(ref _rebar, value); }
    /// <summary>Диаметр, мм.</summary>
    public double Diameter { get => _diameter; set => SetField(ref _diameter, value); }
    public int Count { get => _count; set => SetField(ref _count, value); }
    /// <summary>Шаг sw, мм.</summary>
    public double Step { get => _step; set => SetField(ref _step, value); }
    public bool ZoneEnabled { get => _zoneEnabled; set => SetField(ref _zoneEnabled, value); }
    /// <summary>Расстояние от грани площади до внешнего ряда, мм.</summary>
    public double Zone { get => _zone; set => SetField(ref _zone, value); }
    public bool RswManual { get => _rswManual; set => SetField(ref _rswManual, value); }
    /// <summary>Ручное Rsw, МПа.</summary>
    public double RswManualValue { get => _rswManualValue; set => SetField(ref _rswManualValue, value); }

    /// <summary>Усилия: F, кН; Mx, My, кН·м; отпор q, кПа.</summary>
    public double F { get => _f; set => SetField(ref _f, value); }
    public double Mx { get => _mx; set => SetField(ref _mx, value); }
    public double My { get => _my; set => SetField(ref _my, value); }
    public bool MomentsAreMloc { get => _mloc; set => SetField(ref _mloc, value); }
    public string LoadSide { get => _loadSide; set => SetField(ref _loadSide, value); }
    public double CounterPressure { get => _q; set => SetField(ref _q, value); }
    /// <summary>
    /// Сила проверки за капителью, кН, текстом: пусто — та же F. Задача по строке проверки по МКЭ приходит с силой
    /// своего основания пирамиды у грани капители.
    /// </summary>
    public string FCapitalText { get => _fCapital; set => SetField(ref _fCapital, value ?? ""); }
    /// <summary>Поле «F за капителью»: одиночная задача у колонны с капителью.</summary>
    public bool ShowFCapital => CapitalEnabled && IsColumnNode && IsSingle;
    /// <summary>Сила за капителью из текста; пусто или не число — null (та же F).</summary>
    double? FCapitalValue() => Pars.ParseAny(_fCapital.Trim(), out double v) && double.IsFinite(v) ? v : null;
    /// <summary>Есть строка набора — источник усилий.</summary>
    public bool HasForceRow => _forceRow is not null;
    /// <summary>Показывать строку набора и кнопку «Взять из строки» (только одиночная задача).</summary>
    public bool ShowForceRow => HasForceRow && !IsBatch;
    /// <summary>Пакет по набору усилий: F, Mx, My — из строк набора.</summary>
    public bool IsBatch => _task.Kind == PunchingBatchRunner.Kind;
    /// <summary>Одиночная задача: усилия вводятся в окне.</summary>
    public bool IsSingle => !IsBatch;
    /// <summary>Подпись источника усилий пакета.</summary>
    public string BatchForcesText => !IsBatch ? ""
        : _batchRows is { Count: > 0 } rows
            ? string.Format(CultureInfo.CurrentCulture, IsWallEnd
                    ? Loc.S("PunchingBatchForcesLineWallEnd", "Усилия — все {0} строк набора «{1}»: F = |N|, ΔM = Mx строки (в расчёт — ½ΔM), My не учитывается.")
                    : IsWallCorner
                        ? Loc.S("PunchingBatchForcesLineWallCorner", "Усилия — все {0} строк набора «{1}»: F = |N|, моменты не учитываются.")
                        : Loc.S("PunchingBatchForcesLine", "Усилия — все {0} строк набора «{1}»: F = |N|, Mx и My строки (оси колонны совпадают с осями плиты)."),
                rows.Count, _batchSetTag)
            : Loc.S("PunchingBatchNoForceSet", "Набор усилий не выбран или пуст — выберите его в общем диалоге задачи.");
    /// <summary>Определяющая строка пакета — по ней рисунок и таблица.</summary>
    public string PreviewNote { get; private set; } = "";
    /// <summary>Подпись строки набора.</summary>
    public string ForceRowText => _forceRow is null ? "" : string.Format(CultureInfo.CurrentCulture,
        Loc.S("PunchingForceRowLine", "Строка «{0}»: N = {1:0.#} кН, Mx = {2:0.#} кН·м, My = {3:0.#} кН·м"),
        _forceRow.Label, _forceRow.N, _forceRow.Mx, _forceRow.My);

    /// <summary>Настройки.</summary>
    public string EccentricityX { get => _eccX; set => SetField(ref _eccX, value); }
    public string EccentricityY { get => _eccY; set => SetField(ref _eccY, value); }
    public string Wb { get => _wb; set => SetField(ref _wb, value); }
    public string Pylon { get => _pylon; set => SetField(ref _pylon, value); }
    /// <summary>Редакция рис. 6.2 у угла стен: "amd1" | "amd2".</summary>
    public string WallFigure { get => _wallFigure; set => SetField(ref _wallFigure, value); }
    public double HoleInfluence { get => _holeInfluence; set => SetField(ref _holeInfluence, value); }
    public bool PylonReduced { get => _pylonReduced; set => SetField(ref _pylonReduced, value); }
    public bool MuEnabled { get => _muEnabled; set => SetField(ref _muEnabled, value); }
    /// <summary>μ, %.</summary>
    public double MuPercent { get => _muPercent; set => SetField(ref _muPercent, value); }

    // ── Производные подписи и результат ────────────────────────────────────

    /// <summary>h0x, h0y, h0.</summary>
    public string H0Text { get; private set; } = "";
    /// <summary>Rbt и его источник.</summary>
    public string RbtText { get; private set; } = "";
    /// <summary>Asw, Rsw, qsw.</summary>
    public string SwText { get; private set; } = "";
    /// <summary>Ошибки ввода; пусто — нет.</summary>
    public string ErrorText { get; private set; } = "";
    /// <summary>Есть ошибки ввода.</summary>
    public bool HasErrors => ErrorText.Length > 0;
    /// <summary>Последний корректный результат (рисунок, вердикт, таблица).</summary>
    public PunchingResultVM? Preview { get; private set; }
    /// <summary>Число пересчётов (для тестов и отладки).</summary>
    public int RecalcCount { get; private set; }

    public ICommand AddHoleCommand { get; }
    public ICommand RemoveHoleCommand { get; }
    public ICommand AddPointCommand { get; }
    public ICommand RemovePointCommand { get; }
    public ICommand TakeForcesFromRowCommand { get; }
    public ICommand TakeFromPlateSectionCommand { get; }
    /// <summary>Подобрать поперечную арматуру по ядру.</summary>
    public ICommand DesignCommand { get; }
    /// <summary>Ряды — по п. 10.3.17 для текущей h0 (+ hc).</summary>
    public ICommand ResetRowsCommand { get; }

    bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        if (!_loading) Recalculate();
        return true;
    }

    void RowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (INotifyPropertyChanged r in e.NewItems ?? Array.Empty<object>()) r.PropertyChanged += RowChanged;
        foreach (INotifyPropertyChanged r in e.OldItems ?? Array.Empty<object>()) r.PropertyChanged -= RowChanged;
        if (!_loading) Recalculate();
    }

    void RowChanged(object? sender, PropertyChangedEventArgs e) { if (!_loading) Recalculate(); }

    // ── Перевод параметры ↔ форма ───────────────────────────────────────────

    void Load(PunchingTaskParams p)
    {
        _loading = true;
        try
        {
            NodeKind = p.Wall.Kind is "end" or "corner" or "hanging" ? p.Wall.Kind : "none";
            WallA1 = Mm(p.Wall.A1); WallA2 = Mm(p.Wall.A2);
            HangerWidth = Mm(p.Wall.HangerWidth); HangerOffset = Mm(p.Wall.HangerOffset);
            HangerLegs = p.Wall.HangerLegs == "a_h0" ? "a_h0" : "a";
            WallMPlus = 0; WallMMinus = 0;
            AreaShape = p.Area.Shape;
            Bx = Mm(p.Area.Bx); By = Mm(p.Area.By); D = Mm(p.Area.D); T = Mm(p.Area.T);
            Points.Clear();
            foreach (var pt in p.Area.Points.Where(x => x.Length == 2)) Points.Add(new PunchingPointRowVM { X = Mm(pt[0]), Y = Mm(pt[1]) });
            H = Mm(p.Slab.H); Ax = Mm(p.Slab.Ax); Ay = Mm(p.Slab.Ay);
            Concrete = Concretes.FirstOrDefault(m => m.Id == p.Slab.ConcreteMaterialId);
            RbtManual = p.Slab.RbtOverride is not null;
            RbtManualValue = (p.Slab.RbtOverride ?? 1050) / 1000;

            Position = p.Edges.Count switch { 0 => "inside", 1 => "edge", _ => "corner" };
            var e1 = p.Edges.ElementAtOrDefault(0) ?? new PunchingEdgeParams { Side = DefaultEdge1Side(NodeKind) };
            var e2 = p.Edges.ElementAtOrDefault(1) ?? new PunchingEdgeParams { Side = Perpendicular(e1.Side) };
            Edge1Side = e1.Side; Edge1C = Mm(e1.C); Edge1Angle = e1.AngleDeg;
            Edge2Side = e2.Side; Edge2C = Mm(e2.C); Edge2Angle = e2.AngleDeg;
            NormalizeEdgeSides();

            Holes.Clear();
            foreach (var h in p.Holes)
                Holes.Add(h.Shape == "polygon" && h.Points is { Count: >= 3 } pts
                    ? new PunchingHoleRowVM
                    {
                        Shape = "polygon", Points = pts, Cx = Mm(pts.Average(q => q[0])), Cy = Mm(pts.Average(q => q[1])),
                        Bx = Mm(pts.Max(q => q[0]) - pts.Min(q => q[0])), By = Mm(pts.Max(q => q[1]) - pts.Min(q => q[1])), D = 0,
                    }
                    : new PunchingHoleRowVM { Shape = h.Shape, Cx = Mm(h.Cx), Cy = Mm(h.Cy), Bx = Mm(h.Bx), By = Mm(h.By), D = Mm(h.D) });

            var r = p.Reinforcement;
            SwEnabled = r.Enabled;
            Rebar = Rebars.FirstOrDefault(m => m.Id == r.MaterialId);
            Diameter = Mm(r.Diameter); Count = r.Count; Step = Mm(r.Step);
            ZoneEnabled = r.ZoneExtent is not null; Zone = Mm(r.ZoneExtent ?? 0.3);
            SwLayout = r.IsCross ? "cross" : "uniform";
            SwByRows = r.ByRows;
            FirstRow = Mm(r.FirstRow ?? DefaultRows(p.H0).FirstRow); RowStep = Mm(r.RowStep); RowCount = r.RowCount;
            CrossXLines = r.CrossX.Lines; CrossXStep = Mm(r.CrossX.Step); CrossXPos = r.CrossX.Positive; CrossXNeg = r.CrossX.Negative;
            CrossYLines = r.CrossY.Lines; CrossYStep = Mm(r.CrossY.Step); CrossYPos = r.CrossY.Positive; CrossYNeg = r.CrossY.Negative;
            RswManual = r.RswOverride is not null; RswManualValue = (r.RswOverride ?? 170_000) / 1000;

            F = p.Forces.F; Mx = p.Forces.Mx; My = p.Forces.My; MomentsAreMloc = p.Forces.MomentsAreMloc;
            LoadSide = p.Forces.LoadSide == "from_above" ? "from_above" : "from_below";
            CounterPressure = p.Forces.CounterPressure;
            FCapitalText = p.Forces.FCapital is double fc ? fc.ToString("0.###", CultureInfo.CurrentCulture) : "";

            EccentricityX = p.Options.EccentricityX; EccentricityY = p.Options.EccentricityY;
            Wb = p.Options.Wb; Pylon = p.Options.Pylon; HoleInfluence = p.Options.HoleInfluence;
            WallFigure = p.Options.WallFigure == "amd2" ? "amd2" : "amd1";
            CapitalEnabled = p.Capital.Enabled;
            CapitalBx = Mm(p.Capital.Bx); CapitalBy = Mm(p.Capital.By); CapitalD = Mm(p.Capital.D); CapitalHc = Mm(p.Capital.Hc);
            CapitalDx = Mm(p.Capital.Dx); CapitalDy = Mm(p.Capital.Dy);
            PylonReduced = p.Options.PylonReduced;
            MuEnabled = p.Options.Mu is not null; MuPercent = 100 * (p.Options.Mu ?? 0.01);
        }
        finally { _loading = false; }
        Recalculate();
    }

    /// <summary>Параметры задачи по текущей форме (в СИ-единицах CScore: м, кН, кПа).</summary>
    public PunchingTaskParams ToParams()
    {
        var edges = new List<PunchingEdgeParams>();
        if (HasEdge1) edges.Add(new PunchingEdgeParams { Side = Edge1Side, C = M(Edge1C), AngleDeg = Edge1Angle });
        if (HasEdge2) edges.Add(new PunchingEdgeParams { Side = Edge2Side, C = M(Edge2C), AngleDeg = Edge2Angle });
        return new PunchingTaskParams
        {
            Area = new PunchingAreaParams
            {
                Shape = AreaShape, Bx = M(Bx), By = M(By), D = M(D), T = M(T),
                Points = Points.Select(pt => new[] { M(pt.X), M(pt.Y) }).ToList()
            },
            Slab = new PunchingSlabParams
            {
                H = M(H), Ax = M(Ax), Ay = M(Ay), ConcreteMaterialId = Concrete?.Id ?? 0,
                RbtOverride = RbtManual ? RbtManualValue * 1000 : null
            },
            Edges = edges,
            Holes = Holes.Select(h => h.Shape == "polygon" && h.Points.Count >= 3
                ? new PunchingHoleParams { Shape = "polygon", Points = h.Points }
                : new PunchingHoleParams { Shape = h.Shape == "polygon" ? "rectangle" : h.Shape, Cx = M(h.Cx), Cy = M(h.Cy), Bx = M(h.Bx), By = M(h.By), D = M(h.D) }).ToList(),
            Reinforcement = new PunchingReinforcementParams
            {
                Enabled = SwEnabled, MaterialId = Rebar?.Id ?? 0, Diameter = M(Diameter), Count = Count, Step = M(Step),
                ZoneExtent = ZoneEnabled ? M(Zone) : null, RswOverride = RswManual ? RswManualValue * 1000 : null,
                Layout = SwLayout, FirstRow = SwByRows ? M(FirstRow) : null, RowStep = M(RowStep), RowCount = RowCount,
                CrossX = new PunchingCrossArmParams { Lines = CrossXLines, Step = M(CrossXStep), Positive = CrossXPos, Negative = CrossXNeg },
                CrossY = new PunchingCrossArmParams { Lines = CrossYLines, Step = M(CrossYStep), Positive = CrossYPos, Negative = CrossYNeg },
            },
            Forces = new PunchingForceParams
            {
                F = F, Mx = Mx, My = My, MomentsAreMloc = MomentsAreMloc, LoadSide = LoadSide, CounterPressure = CounterPressure,
                FCapital = IsSingle ? FCapitalValue() : null,
            },
            Options = new PunchingOptionParams
            {
                EccentricityX = EccentricityX, EccentricityY = EccentricityY, Wb = Wb, Pylon = Pylon, WallFigure = WallFigure,
                HoleInfluence = HoleInfluence, PylonReduced = PylonReduced, Mu = MuEnabled ? MuPercent / 100 : null,
                // Как контуры пользователя: в окне не правится, но сохраняется (контур ЛИРЫ из проверки по МКЭ).
                CustomReplacesMain = _customReplacesMain,
            },
            CustomContours = _customContours,
            Wall = new PunchingWallParams
            {
                Kind = NodeKind, A1 = M(WallA1), A2 = M(WallA2),
                HangerWidth = M(HangerWidth), HangerOffset = M(HangerOffset), HangerLegs = HangerLegs,
            },
            Capital = new PunchingCapitalParams
            {
                Enabled = CapitalEnabled && IsColumnNode, Bx = M(CapitalBx), By = M(CapitalBy), D = M(CapitalD), Hc = M(CapitalHc),
                Dx = M(CapitalDx), Dy = M(CapitalDy),
            },
            // Источник задачи по строке проверки по МКЭ (проверка, узел, ключ) в окне не правится, но хранится: по нему
            // «Создать задачу» находит прежнюю задачу строки, а отчёт показывает происхождение усилий.
            Source = _source,
        };
    }

    readonly PunchingSourceParams? _source;

    /// <summary>JSON параметров для сохранения в задачу.</summary>
    public string ToJson() => ToParams().ToJson();

    // ── Пересчёт ───────────────────────────────────────────────────────────

    /// <summary>Пересчитывает узел по форме; при ошибке ввода сохраняет прежний <see cref="Preview"/>.</summary>
    public void Recalculate()
    {
        RecalcCount++;
        // Итог «Подобрать» относится к вводу, который он сам записал: любая следующая правка его сбрасывает.
        if (_keepDesignText) _keepDesignText = false;
        else if (DesignApplyText.Length > 0) { DesignApplyText = ""; OnPropertyChanged(nameof(DesignApplyText)); }
        FollowRows();
        UpdateRowsHint();
        var p = ToParams();
        var c = CultureInfo.CurrentCulture;
        H0Text = string.Format(c, "h0x = {0:0.#} мм, h0y = {1:0.#} мм, h0 = {2:0.#} мм", p.H0x * 1000, p.H0y * 1000, p.H0 * 1000);
        if (p.Capital.Enabled)
            H0Text += string.Format(c, Loc.S("PunchingCapitalH0Line", "; у колонны с капителью h0 + hc = {0:0.#} мм"), (p.H0 + p.Capital.Hc) * 1000);

        var errors = p.Validate();
        var strengths = PunchingRunner.ResolveStrengths(p, _materials, _task.CalcType, out var materialErrors);
        errors.AddRange(materialErrors);

        RbtText = strengths.Rbt > 0
            ? string.Format(c, Loc.S("PunchingRbtLine", "Rbt = {0:0.###} МПа ({1}, расчёт {2})"), strengths.Rbt / 1000,
                strengths.ConcreteTag.Length > 0 ? strengths.ConcreteTag : Loc.S("PunchingManualValue", "задано вручную"), _task.CalcType)
            : "";
        SwText = SwLine(p, strengths.Rsw);

        if (IsBatch && _batchRows is not { Count: > 0 }) errors.Add("force_set_missing");
        if (errors.Count > 0)
            ErrorText = string.Join("\n", errors.Distinct().Select(code => Loc.S("PunchingErr_" + code, code)));
        else
        {
            ErrorText = "";
            try
            {
                var single = p;
                if (IsBatch)
                {
                    // Пакет: весь набор без трассировки, рисунок и таблица — по определяющей строке.
                    var outcome = PunchingBatchRunner.Evaluate(p, strengths, _batchRows!);
                    var gov = outcome.Governing ?? outcome.Rows[0];
                    single = PunchingBatchRunner.RowParams(p, gov.Item);
                    PreviewNote = string.Format(c, Loc.S("PunchingBatchPreviewLine",
                        "Определяющая строка № {0} «{1}» из {2}; рисунок и таблица — по этой строке."), gov.Item.Num, gov.Item.Label, _batchRows!.Count);
                    OnPropertyChanged(nameof(PreviewNote));
                }
                var input = single.ToInput(strengths.Rbt, strengths.Rsw);
                var result = PunchingChecker.Check(input);
                var calc = new CalcResult
                {
                    TaskId = _task.Id, TaskKind = _task.Kind, TaskTag = _task.Tag,
                    Status = result.Passed ? "ok" : "not_passed",
                    DataJson = PunchingRunner.Serialize(_task, single, strengths, input, result)
                };
                string? keep = Preview?.SelectedVariantId;
                var preview = new PunchingResultVM(calc, _task);
                if (keep is not null && preview.Rows.Any(r => r.Id == keep)) preview.SelectedVariantId = keep;
                Preview = preview;
                OnPropertyChanged(nameof(Preview));
            }
            catch (Exception ex)
            {
                ErrorText = ex.Message;
            }
        }
        OnPropertyChanged(nameof(H0Text));
        OnPropertyChanged(nameof(RbtText));
        OnPropertyChanged(nameof(SwText));
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(HasErrors));
    }

    /// <summary>Asw, Rsw, qsw (и ряды в пирамиде) по текущему вводу; пусто — арматуры нет или ввод неполон.</summary>
    static string SwLine(PunchingTaskParams p, double rsw)
    {
        var r = p.Reinforcement;
        if (!r.Enabled || !(rsw > 0) || p.Validate().Any(e => e.StartsWith("sw_", StringComparison.Ordinal))) return "";
        var c = CultureInfo.CurrentCulture;
        var core = r.ToCore(rsw);
        // Ряды у колонны с капителью ядро считает по h0 + hc (если контур у колонны в капители) — так и здесь.
        double h0 = p.H0;
        if (p.Capital.Enabled && !p.Validate().Any())
            try { h0 = PunchingCapitalCheck.ColumnH0(p.ToInput(1, rsw)); }
            catch (ArgumentException) { }
        double asw = PunchingSwLayout.AswPerLine(core, h0);
        string rows = core.Rows is { } rs
            ? string.Format(c, Loc.S("PunchingSwRowsLine", "рядов в пирамиде (h0/3 … h0 от грани): {0} из {1}; "), PunchingSwLayout.CountRows(rs, h0).Counted, rs.Count)
            : "";
        if (core.Cross is not null)
        {
            var (qx, qy) = PunchingSwLayout.QCross(core, h0);
            return rows + string.Format(c, Loc.S("PunchingSwCrossLine", "Asw = {0:0.#} мм², Rsw = {1:0.#} МПа; qsw лучей вдоль X = {2:0.#} кН/м, вдоль Y = {3:0.#} кН/м"),
                asw * 1e6, rsw / 1000, qx, qy);
        }
        return rows + string.Format(c, Loc.S("PunchingSwLine", "Asw = {0:0.#} мм², Rsw = {1:0.#} МПа, qsw = Rsw·Asw/sw = {2:0.#} кН/м"),
            asw * 1e6, rsw / 1000, PunchingSwLayout.QUniform(core, h0));
    }

    /// <summary>
    /// «Подобрать»: при выключенной арматуре включает её рядами по п. 10.3.17; затем по ядру подбирает диаметр из ряда
    /// (с рядами) или число стержней на шаге (без рядов) и ширину зоны для контуров за армированием.
    /// </summary>
    public void ApplyDesign()
    {
        var c = CultureInfo.CurrentCulture;
        _loading = true;
        try
        {
            if (!SwEnabled)
            {
                SwEnabled = true;
                var (s1, sd, nr) = DefaultRows(SwH0Value());
                SwByRows = true; FirstRow = Mm(s1); RowStep = Mm(sd); RowCount = nr;
                if (!(Step > 0)) Step = Mm(sd);
            }
        }
        finally { _loading = false; }

        var p = ToParams();
        var errors = p.Validate();
        var strengths = PunchingRunner.ResolveStrengths(p, _materials, _task.CalcType, out var materialErrors);
        errors.AddRange(materialErrors);

        if (errors.Count > 0 || !(strengths.Rsw > 0))
        {
            DesignApplyText = Loc.S("PunchingDesignInputError", "Подбор невозможен: исправьте исходные данные.");
            OnPropertyChanged(nameof(DesignApplyText));
            _keepDesignText = true;
            Recalculate();
            return;
        }
        var single = p;
        if (IsBatch && _batchRows is { Count: > 0 })
        {
            var outcome = PunchingBatchRunner.Evaluate(p, strengths, _batchRows);
            single = PunchingBatchRunner.RowParams(p, (outcome.Governing ?? outcome.Rows[0]).Item);
        }
        var d = PunchingDesign.Solve(single.ToInput(strengths.Rbt, strengths.Rsw));
        _loading = true;
        try
        {
            switch (d.State)
            {
                case PunchingDesignState.NotRequired:
                    DesignApplyText = Loc.S("PunchingDesignApplied_not_required", "Поперечная арматура не требуется — ввод не изменён.");
                    break;
                case PunchingDesignState.NotEnsured:
                    DesignApplyText = Loc.S("PunchingDesign_not_ensured", "Арматурой не обеспечить.");
                    break;
                case PunchingDesignState.LayoutIneffective:
                    DesignApplyText = Loc.S("PunchingDesign_layout_ineffective", "Раскладка не пересекает определяющий контур или пирамиду.");
                    break;
                default:
                    if (SwByRows)
                    {
                        if (d.BarDiameter is double dia) Diameter = Mm(dia);
                        if (d.RowsRequired is int n) RowCount = n;
                        DesignApplyText = d.BarDiameter is double dd
                            ? string.Format(c, Loc.S("PunchingDesignAppliedRows", "Подобрано: Ø{0:0}, рядов {1}."), dd * 1000, RowCount)
                            : Loc.S("PunchingDesignAppliedTooBig", "Даже Ø32 недостаточно — уменьшите шаги или добавьте линии.");
                    }
                    else
                    {
                        double bar = Math.PI * Math.Pow(M(Diameter), 2) / 4;
                        if (bar > 0 && double.IsFinite(d.AswLineReq)) Count = Math.Max(1, (int)Math.Ceiling(d.AswLineReq / bar - 1e-9));
                        if (d.ZoneExtentRequired is double z) { ZoneEnabled = true; Zone = Math.Ceiling(z * 1000 / 5) * 5; }
                        DesignApplyText = string.Format(c, Loc.S("PunchingDesignAppliedCount", "Подобрано: {0} × Ø{1:0} на шаге {2:0} мм."), Count, Diameter, Step);
                    }
                    break;
            }
        }
        finally { _loading = false; }
        OnPropertyChanged(nameof(DesignApplyText));
        _keepDesignText = true;
        Recalculate();
    }

    // ── Команды ────────────────────────────────────────────────────────────

    /// <summary>F = |N|, Mx, My из строки набора (оси колонны совпадают с осями плиты).</summary>
    public void TakeForcesFromRow()
    {
        if (_forceRow is null) return;
        _loading = true;
        try { F = Math.Abs(_forceRow.N); Mx = _forceRow.Mx; My = _forceRow.My; FCapitalText = ""; }
        finally { _loading = false; }
        Recalculate();
    }

    /// <summary>
    /// h, бетон и привязки из плитного сечения: растянутая грань — верхняя при колонне под плитой,
    /// нижняя при нагрузке сверху; привязка — до крайнего слоя у этой грани.
    /// </summary>
    public void TakeFromPlateSection()
    {
        var s = SelectedPlateSection;
        if (s is null) return;
        bool top = LoadSide != "from_above";
        double? Cover(Func<PlateRebarLayer, double> area, Func<PlateRebarLayer, double> z)
        {
            var layers = s.RebarLayers.Where(l => area(l) > 0).ToList();
            if (layers.Count == 0) return null;
            return top ? s.H / 2 - layers.Max(z) : s.H / 2 + layers.Min(z);
        }
        _loading = true;
        try
        {
            H = Mm(s.H);
            if (Cover(l => l.Asx, l => l.Zsx) is double ax && ax > 0) Ax = Mm(ax);
            if (Cover(l => l.Asy, l => l.Zsy) is double ay && ay > 0) Ay = Mm(ay);
            // Ручное Rbt — осознанный выбор пользователя: бетон берём из сечения, но флаг не сбрасываем.
            if (Concretes.FirstOrDefault(m => m.Id == s.ConcreteMaterialId) is { } concrete) Concrete = concrete;
        }
        finally { _loading = false; }
        Recalculate();
    }

    /// <summary>
    /// Приводит стороны краёв к допустимым для вида узла (<see cref="PunchingWallParams.EdgeSides"/>): иначе список сторон
    /// в окне отфильтрован, поле «Край» пустое и сохранить нельзя. Первый край по умолчанию: у торца — за торцом (+X,
    /// контур 1′), у угла — снаружи (−X), у колонны — +Y; второй — соседняя допустимая сторона (не та же и не напротив).
    /// </summary>
    void NormalizeEdgeSides()
    {
        var allowed = PunchingWallParams.EdgeSides(NodeKind);
        if (!allowed.Contains(_edge1Side))
            SetField(ref _edge1Side, DefaultEdge1Side(NodeKind), nameof(Edge1Side));
        if (!allowed.Contains(_edge2Side) || !SideOk(_edge1Side, _edge2Side))
            SetField(ref _edge2Side, allowed.FirstOrDefault(s => SideOk(_edge1Side, s)) ?? _edge2Side, nameof(Edge2Side));
    }

    static string DefaultEdge1Side(string nodeKind) => nodeKind switch { "corner" => "-x", "end" or "hanging" => "+x", _ => "+y" };

    static string Perpendicular(string side) => side is "+y" or "-y" ? "+x" : "+y";
    /// <summary>Два края с этих сторон допустимы (разные и не противоположные).</summary>
    static bool SideOk(string a, string b) => a != b && PunchingTaskParams.SideVector(a).Dot(PunchingTaskParams.SideVector(b)) > -0.5;
    double MaxHalfSize() => IsWallNode ? M(Math.Max(WallA1, WallA2)) / 2 : AreaShape switch { "circle" or "ring" => M(D) / 2, "polygon" => 0.2, _ => M(Math.Max(Bx, By)) / 2 };
    double H0Value() => M(H) - 0.5 * M(Ax + Ay);
    /// <summary>Рабочая высота, по которой расставляется поперечная арматура: у колонны с капителью — h0 + hc.</summary>
    double SwH0Value() => H0Value() + (CapitalEnabled && IsColumnNode && CapitalHc > 0 ? M(CapitalHc) : 0);
    static double Mm(double m) => Math.Round(m * 1000, 6);
    static double M(double mm) => mm / 1000;
}
