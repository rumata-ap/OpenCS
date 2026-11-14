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
    readonly List<PunchingCustomContourParams> _customContours;
    bool _loading;

    /// <summary>Создаёт VM по задаче и данным проекта.</summary>
    public PunchingParamsVM(CalcTask task, IEnumerable<Material> materials, IEnumerable<PlateSection> plateSections, LoadItem? forceRow)
    {
        _task = task;
        _materials = materials.ToList();
        _forceRow = forceRow;
        Concretes = [.. _materials.Where(m => m.Type == MatType.Concrete)];
        Rebars = [.. _materials.Where(m => m.Type is MatType.ReSteelF or MatType.ReSteelU)];
        PlateSections = [.. plateSections];

        var p = PunchingTaskParams.Parse(task.ParamsJson);
        _customContours = p.CustomContours;
        bool isNew = string.IsNullOrWhiteSpace(task.ParamsJson) || task.ParamsJson.Trim() == "{}";
        if (isNew)
        {
            // Новая задача: бетон и арматура — первые в проекте, усилия — из строки набора, если выбрана.
            p = p with
            {
                Slab = p.Slab with { ConcreteMaterialId = Concretes.FirstOrDefault()?.Id ?? 0 },
                Reinforcement = p.Reinforcement with { MaterialId = Rebars.FirstOrDefault()?.Id ?? 0 }
            };
            if (forceRow is not null)
                p = p with { Forces = p.Forces with { F = Math.Abs(forceRow.N), Mx = forceRow.Mx, My = forceRow.My } };
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
    /// <summary>Формы отверстий.</summary>
    public IReadOnlyList<PunchingChoice> HoleShapes { get; } =
        [new("rectangle", Loc.S("PunchingShape_rectangle", "прямоугольник")), new("circle", Loc.S("PunchingShape_circle", "круг"))];
    /// <summary>Положение узла.</summary>
    public IReadOnlyList<PunchingChoice> Positions { get; } =
    [
        new("inside", Loc.S("PunchingPosition_inside", "внутри плиты")), new("edge", Loc.S("PunchingPosition_edge", "у края")),
        new("corner", Loc.S("PunchingPosition_corner", "в углу"))
    ];
    /// <summary>Стороны площади.</summary>
    public IReadOnlyList<PunchingChoice> Sides { get; } = [new("+x", "+X"), new("-x", "−X"), new("+y", "+Y"), new("-y", "−Y")];
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
        [new("auto", Loc.S("PunchingPylon_auto", "автоматически (b/a > 2)")), new("off", Loc.S("PunchingPylon_off", "не рассматривать"))];

    // ── Поля формы ─────────────────────────────────────────────────────────

    string _areaShape = "rectangle";
    double _bx, _by, _d, _t, _h, _ax, _ay, _rbtManualValue, _diameter, _step, _zone, _rswManualValue, _f, _mx, _my, _q, _holeInfluence, _muPercent;
    int _count;
    bool _rbtManual, _swEnabled, _zoneEnabled, _rswManual, _mloc, _pylonReduced, _muEnabled;
    string _position = "inside", _edge1Side = "+y", _edge2Side = "+x", _loadSide = "from_below";
    double _edge1C, _edge1Angle, _edge2C, _edge2Angle;
    string _eccX = "loading", _eccY = "loading", _wb = "minimum", _pylon = "auto";
    Material? _concrete, _rebar;
    PlateSection? _plateSection;
    PunchingHoleRowVM? _selectedHole;
    PunchingPointRowVM? _selectedPoint;

    /// <summary>Форма площади.</summary>
    public string AreaShape { get => _areaShape; set { if (SetField(ref _areaShape, value)) { OnPropertyChanged(nameof(IsRectangle)); OnPropertyChanged(nameof(IsRound)); OnPropertyChanged(nameof(IsRing)); OnPropertyChanged(nameof(IsPolygon)); } } }
    public bool IsRectangle => AreaShape == "rectangle";
    public bool IsRound => AreaShape is "circle" or "ring";
    public bool IsRing => AreaShape == "ring";
    public bool IsPolygon => AreaShape == "polygon";
    /// <summary>Размеры площади, мм.</summary>
    public double Bx { get => _bx; set => SetField(ref _bx, value); }
    public double By { get => _by; set => SetField(ref _by, value); }
    public double D { get => _d; set => SetField(ref _d, value); }
    public double T { get => _t; set => SetField(ref _t, value); }
    /// <summary>Вершины многоугольника, мм.</summary>
    public ObservableCollection<PunchingPointRowVM> Points { get; } = [];
    public PunchingPointRowVM? SelectedPoint { get => _selectedPoint; set { _selectedPoint = value; OnPropertyChanged(); } }

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
    public string Position { get => _position; set { if (SetField(ref _position, value)) { OnPropertyChanged(nameof(HasEdge1)); OnPropertyChanged(nameof(HasEdge2)); } } }
    public bool HasEdge1 => Position is "edge" or "corner";
    public bool HasEdge2 => Position == "corner";
    public string Edge1Side { get => _edge1Side; set => SetField(ref _edge1Side, value); }
    /// <summary>Расстояние от площади до края, мм.</summary>
    public double Edge1C { get => _edge1C; set => SetField(ref _edge1C, value); }
    public double Edge1Angle { get => _edge1Angle; set => SetField(ref _edge1Angle, value); }
    public string Edge2Side { get => _edge2Side; set => SetField(ref _edge2Side, value); }
    public double Edge2C { get => _edge2C; set => SetField(ref _edge2C, value); }
    public double Edge2Angle { get => _edge2Angle; set => SetField(ref _edge2Angle, value); }

    /// <summary>Отверстия, мм.</summary>
    public ObservableCollection<PunchingHoleRowVM> Holes { get; } = [];
    public PunchingHoleRowVM? SelectedHole { get => _selectedHole; set { _selectedHole = value; OnPropertyChanged(); } }

    /// <summary>Поперечная арматура.</summary>
    public bool SwEnabled { get => _swEnabled; set => SetField(ref _swEnabled, value); }
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
    /// <summary>Есть строка набора — источник усилий.</summary>
    public bool HasForceRow => _forceRow is not null;
    /// <summary>Подпись строки набора.</summary>
    public string ForceRowText => _forceRow is null ? "" : string.Format(CultureInfo.CurrentCulture,
        Loc.S("PunchingForceRowLine", "Строка «{0}»: N = {1:0.#} кН, Mx = {2:0.#} кН·м, My = {3:0.#} кН·м"),
        _forceRow.Label, _forceRow.N, _forceRow.Mx, _forceRow.My);

    /// <summary>Настройки.</summary>
    public string EccentricityX { get => _eccX; set => SetField(ref _eccX, value); }
    public string EccentricityY { get => _eccY; set => SetField(ref _eccY, value); }
    public string Wb { get => _wb; set => SetField(ref _wb, value); }
    public string Pylon { get => _pylon; set => SetField(ref _pylon, value); }
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
            AreaShape = p.Area.Shape;
            Bx = Mm(p.Area.Bx); By = Mm(p.Area.By); D = Mm(p.Area.D); T = Mm(p.Area.T);
            Points.Clear();
            foreach (var pt in p.Area.Points.Where(x => x.Length == 2)) Points.Add(new PunchingPointRowVM { X = Mm(pt[0]), Y = Mm(pt[1]) });
            H = Mm(p.Slab.H); Ax = Mm(p.Slab.Ax); Ay = Mm(p.Slab.Ay);
            Concrete = Concretes.FirstOrDefault(m => m.Id == p.Slab.ConcreteMaterialId);
            RbtManual = p.Slab.RbtOverride is not null;
            RbtManualValue = (p.Slab.RbtOverride ?? 1050) / 1000;

            Position = p.Edges.Count switch { 0 => "inside", 1 => "edge", _ => "corner" };
            var e1 = p.Edges.ElementAtOrDefault(0) ?? new PunchingEdgeParams { Side = "+y" };
            var e2 = p.Edges.ElementAtOrDefault(1) ?? new PunchingEdgeParams { Side = Perpendicular(e1.Side) };
            Edge1Side = e1.Side; Edge1C = Mm(e1.C); Edge1Angle = e1.AngleDeg;
            Edge2Side = e2.Side; Edge2C = Mm(e2.C); Edge2Angle = e2.AngleDeg;

            Holes.Clear();
            foreach (var h in p.Holes)
                Holes.Add(new PunchingHoleRowVM { Shape = h.Shape, Cx = Mm(h.Cx), Cy = Mm(h.Cy), Bx = Mm(h.Bx), By = Mm(h.By), D = Mm(h.D) });

            var r = p.Reinforcement;
            SwEnabled = r.Enabled;
            Rebar = Rebars.FirstOrDefault(m => m.Id == r.MaterialId);
            Diameter = Mm(r.Diameter); Count = r.Count; Step = Mm(r.Step);
            ZoneEnabled = r.ZoneExtent is not null; Zone = Mm(r.ZoneExtent ?? 0.3);
            RswManual = r.RswOverride is not null; RswManualValue = (r.RswOverride ?? 170_000) / 1000;

            F = p.Forces.F; Mx = p.Forces.Mx; My = p.Forces.My; MomentsAreMloc = p.Forces.MomentsAreMloc;
            LoadSide = p.Forces.LoadSide == "from_above" ? "from_above" : "from_below";
            CounterPressure = p.Forces.CounterPressure;

            EccentricityX = p.Options.EccentricityX; EccentricityY = p.Options.EccentricityY;
            Wb = p.Options.Wb; Pylon = p.Options.Pylon; HoleInfluence = p.Options.HoleInfluence;
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
            Holes = Holes.Select(h => new PunchingHoleParams { Shape = h.Shape, Cx = M(h.Cx), Cy = M(h.Cy), Bx = M(h.Bx), By = M(h.By), D = M(h.D) }).ToList(),
            Reinforcement = new PunchingReinforcementParams
            {
                Enabled = SwEnabled, MaterialId = Rebar?.Id ?? 0, Diameter = M(Diameter), Count = Count, Step = M(Step),
                ZoneExtent = ZoneEnabled ? M(Zone) : null, RswOverride = RswManual ? RswManualValue * 1000 : null
            },
            Forces = new PunchingForceParams
            {
                F = F, Mx = Mx, My = My, MomentsAreMloc = MomentsAreMloc, LoadSide = LoadSide, CounterPressure = CounterPressure
            },
            Options = new PunchingOptionParams
            {
                EccentricityX = EccentricityX, EccentricityY = EccentricityY, Wb = Wb, Pylon = Pylon,
                HoleInfluence = HoleInfluence, PylonReduced = PylonReduced, Mu = MuEnabled ? MuPercent / 100 : null
            },
            CustomContours = _customContours
        };
    }

    /// <summary>JSON параметров для сохранения в задачу.</summary>
    public string ToJson() => ToParams().ToJson();

    // ── Пересчёт ───────────────────────────────────────────────────────────

    /// <summary>Пересчитывает узел по форме; при ошибке ввода сохраняет прежний <see cref="Preview"/>.</summary>
    public void Recalculate()
    {
        RecalcCount++;
        var p = ToParams();
        var c = CultureInfo.CurrentCulture;
        H0Text = string.Format(c, "h0x = {0:0.#} мм, h0y = {1:0.#} мм, h0 = {2:0.#} мм", p.H0x * 1000, p.H0y * 1000, p.H0 * 1000);

        var errors = p.Validate();
        var strengths = PunchingRunner.ResolveStrengths(p, _materials, _task.CalcType, out var materialErrors);
        errors.AddRange(materialErrors);

        RbtText = strengths.Rbt > 0
            ? string.Format(c, Loc.S("PunchingRbtLine", "Rbt = {0:0.###} МПа ({1}, расчёт {2})"), strengths.Rbt / 1000,
                strengths.ConcreteTag.Length > 0 ? strengths.ConcreteTag : Loc.S("PunchingManualValue", "задано вручную"), _task.CalcType)
            : "";
        double asw = p.Reinforcement.Asw;
        SwText = p.Reinforcement.Enabled && strengths.Rsw > 0 && p.Reinforcement.Step > 0
            ? string.Format(c, Loc.S("PunchingSwLine", "Asw = {0:0.#} мм², Rsw = {1:0.#} МПа, qsw = Rsw·Asw/sw = {2:0.#} кН/м"),
                asw * 1e6, strengths.Rsw / 1000, strengths.Rsw * asw / p.Reinforcement.Step)
            : "";

        if (errors.Count > 0)
            ErrorText = string.Join("\n", errors.Distinct().Select(code => Loc.S("PunchingErr_" + code, code)));
        else
        {
            ErrorText = "";
            try
            {
                var input = p.ToInput(strengths.Rbt, strengths.Rsw);
                var result = PunchingChecker.Check(input);
                var calc = new CalcResult
                {
                    TaskId = _task.Id, TaskKind = _task.Kind, TaskTag = _task.Tag,
                    Status = result.Passed ? "ok" : "not_passed",
                    DataJson = PunchingRunner.Serialize(_task, p, strengths, input, result)
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

    // ── Команды ────────────────────────────────────────────────────────────

    /// <summary>F = |N|, Mx, My из строки набора (оси колонны совпадают с осями плиты).</summary>
    public void TakeForcesFromRow()
    {
        if (_forceRow is null) return;
        _loading = true;
        try { F = Math.Abs(_forceRow.N); Mx = _forceRow.Mx; My = _forceRow.My; }
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
            if (Concretes.FirstOrDefault(m => m.Id == s.ConcreteMaterialId) is { } concrete) { Concrete = concrete; RbtManual = false; }
        }
        finally { _loading = false; }
        Recalculate();
    }

    static string Perpendicular(string side) => side is "+y" or "-y" ? "+x" : "+y";
    double MaxHalfSize() => AreaShape switch { "circle" or "ring" => M(D) / 2, "polygon" => 0.2, _ => M(Math.Max(Bx, By)) / 2 };
    double H0Value() => M(H) - 0.5 * M(Ax + Ay);
    static double Mm(double m) => Math.Round(m * 1000, 6);
    static double M(double mm) => mm / 1000;
}
