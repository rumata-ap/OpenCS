using CScore;
using CScore.Fem;
using CScore.Fem.Loads;
using CScore.Planar;
using CScore.PlateRebar;
using CScore.PlateStrip;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.Views;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenCS.ViewModels;

/// <summary>Вариант источника армирования полосы в списке вкладки «Полоса».</summary>
/// <param name="Key">Ключ: пусто — раскладка региона, иначе <see cref="FemCheckRebarSource"/>.</param>
/// <param name="Display">Подпись.</param>
public sealed record StripRebarSourceOption(string Key, string Display);

/// <summary>Вариант выбора в списке (индекс → значение перечисления).</summary>
public sealed record StripChoice<T>(T Value, string Display);

/// <summary>
/// Точки на плане региона (привязка к вершинам/серединам рёбер/узлам КЭ/ортогонально/к шагу), рисование зоны
/// армирования мышью и вкладка «Полоса»: ось полосы указывается мышью на том же плане, строится эквивалентное
/// сечение полосы с выбранным источником армирования (раскладка региона либо КЭ импортированной схемы).
/// </summary>
public partial class PlanarRegionMemberVM
{
    // ------------------------------------------------------------------ привязка

    bool _snapEnabled = true;
    /// <summary>Привязка точек, указанных мышью: к вершинам контуров, зон и узлам КЭ, серединам рёбер,
    /// ортогонально к предыдущей точке.</summary>
    public bool SnapEnabled { get => _snapEnabled; set { _snapEnabled = value; OnPropertyChanged(); } }

    double _snapGridStepM;
    /// <summary>Шаг сетки привязки, м; 0 — без привязки к сетке.</summary>
    public double SnapGridStepM
    {
        get => _snapGridStepM;
        set { _snapGridStepM = Math.Max(0, value); OnPropertyChanged(); }
    }

    /// <summary>Допуск привязки к вершине, пикселей экрана.</summary>
    const double SnapPixels = 10;

    List<(double U, double V)>? _fePoints;
    List<IReadOnlyList<(double U, double V)>>? _feLoops;

    /// <summary>КЭ-пластины элемента из сетки схемы (узлы и контуры КЭ) в плоскости региона.</summary>
    void EnsureFeGeometry()
    {
        if (_fePoints != null) return;
        _fePoints = [];
        _feLoops = [];
        if (_existingMember == null) return;

        var nodes = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        foreach (var n in _app.db.GetFemMeshNodes(_schema.Id)) nodes.TryAdd(n.NodeTag, n);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in _app.db.GetFemMeshElements(_schema.Id))
        {
            if (e.ElemType != "shell" || !string.Equals(e.SourceMemberTag, _existingMember.ElemTag, StringComparison.Ordinal)) continue;
            var geometry = FemElementGeometry.Of(e, nodes);
            if (geometry is not { IsShell: true }) continue;
            var loop = geometry.Contour
                .Select(i => PlanarBoundaryFrameConverter.ToLocalPoint(_frame, geometry.Points[i]))
                .Select(p => (U: p.X, V: p.Y))
                .ToList();
            _feLoops.Add(loop);
            for (int k = 0; k < geometry.Points.Count; k++)
                if (seen.Add(geometry.NodeTags[k]))
                {
                    var p = PlanarBoundaryFrameConverter.ToLocalPoint(_frame, geometry.Points[k]);
                    _fePoints.Add((p.X, p.Y));
                }
        }
    }

    /// <summary>Привязать точку, указанную мышью. <paramref name="pixelSize"/> — размер пикселя в единицах модели.</summary>
    (double U, double V) SnapPoint(double u, double v, double pixelSize, (double U, double V)? anchor, bool includeZones)
    {
        if (!SnapEnabled && SnapGridStepM <= 0) return (u, v);
        EnsureFeGeometry();

        var vertices = new List<(double U, double V)>(_fePoints!);
        var loops = new List<IReadOnlyList<(double U, double V)>>();
        void AddContour(IList<double> xs, IList<double> ys)
        {
            var loop = xs.Zip(ys, (x, y) => (U: x, V: y)).ToList();
            vertices.AddRange(loop);
            loops.Add(loop);
        }
        if (Hull != null) AddContour(Hull.X, Hull.Y);
        foreach (var hole in Holes) AddContour(hole.X, hole.Y);
        if (includeZones)
            foreach (var z in RebarZones)
                if (z.Model.Polygon.Count >= 3)
                {
                    var loop = z.Model.Polygon.Select(p => (U: p.U, V: p.V)).ToList();
                    vertices.AddRange(loop);
                    loops.Add(loop);
                }

        double tolerance = SnapEnabled ? SnapPixels * pixelSize : 0;
        var snapped = PlanarPointSnap.Snap(u, v, SnapEnabled ? vertices : [], SnapEnabled ? loops : [], tolerance,
            SnapEnabled ? anchor : null, SnapGridStepM);
        return (snapped.U, snapped.V);
    }

    // ------------------------------------------------------------------ рисование зоны мышью

    readonly List<(double U, double V)> _drawPoints = [];
    (double U, double V)? _drawHover;

    bool _isDrawingZone;
    /// <summary>Идёт рисование контура зоны армирования мышью на плане.</summary>
    public bool IsDrawingZone { get => _isDrawingZone; private set { _isDrawingZone = value; OnPropertyChanged(); } }

    /// <summary>Число поставленных вершин рисуемой зоны.</summary>
    public int DrawingPointCount => _drawPoints.Count;

    /// <summary>Начать рисование зоны: щелчки левой кнопкой ставят вершины, двойной щелчок или Enter завершает.</summary>
    public void BeginZoneDrawing()
    {
        if (IsDrawingZone) return;
        _drawPoints.Clear();
        _drawHover = null;
        IsDrawingZone = true;
        OnPropertyChanged(nameof(DrawingPointCount));
        RefreshRebarPlotElements();
    }

    /// <summary>Поставить вершину в указанной точке (с привязкой).</summary>
    public void AddDrawingPoint(double u, double v, double pixelSize)
    {
        if (!IsDrawingZone) return;
        var p = SnapPoint(u, v, pixelSize, _drawPoints.Count > 0 ? _drawPoints[^1] : null, includeZones: true);
        if (_drawPoints.Count > 0 && Math.Abs(_drawPoints[^1].U - p.U) < 1e-9 && Math.Abs(_drawPoints[^1].V - p.V) < 1e-9)
            return;
        _drawPoints.Add(p);
        OnPropertyChanged(nameof(DrawingPointCount));
        RefreshRebarPlotElements();
    }

    /// <summary>Резиновая линия до курсора.</summary>
    public void HoverDrawing(double u, double v, double pixelSize)
    {
        if (!IsDrawingZone || _drawPoints.Count == 0) return;
        _drawHover = SnapPoint(u, v, pixelSize, _drawPoints[^1], includeZones: true);
        RefreshRebarPlotElements();
    }

    /// <summary>Убрать последнюю вершину.</summary>
    public void UndoDrawingPoint()
    {
        if (!IsDrawingZone || _drawPoints.Count == 0) return;
        _drawPoints.RemoveAt(_drawPoints.Count - 1);
        OnPropertyChanged(nameof(DrawingPointCount));
        RefreshRebarPlotElements();
    }

    /// <summary>Завершить рисование: из вершин получается новая зона активной грани. Меньше трёх вершин — зона не создаётся.</summary>
    public bool FinishZoneDrawing()
    {
        if (!IsDrawingZone) return false;
        if (_drawPoints.Count < 3) { CancelZoneDrawing(); return false; }

        var zone = new RebarZone
        {
            Name = $"Зона {RebarZones.Count + 1}",
            Face = ActiveRebarFace,
            Polygon = [.. _drawPoints.Select(p => new RebarZonePoint { U = p.U, V = p.V })],
        };
        _drawPoints.Clear();
        _drawHover = null;
        IsDrawingZone = false;
        OnPropertyChanged(nameof(DrawingPointCount));
        AddRebarZone(zone);
        return true;
    }

    /// <summary>Отменить рисование без создания зоны.</summary>
    public void CancelZoneDrawing()
    {
        if (!IsDrawingZone) return;
        _drawPoints.Clear();
        _drawHover = null;
        IsDrawingZone = false;
        OnPropertyChanged(nameof(DrawingPointCount));
        RefreshRebarPlotElements();
    }

    /// <summary>Рисуемый контур зоны поверх плана армирования.</summary>
    void AddDrawingElements(List<PlotElement> elements)
    {
        if (!IsDrawingZone || _drawPoints.Count == 0) return;
        var xs = _drawPoints.Select(p => p.U).ToList();
        var ys = _drawPoints.Select(p => p.V).ToList();
        if (_drawHover is { } h) { xs.Add(h.U); ys.Add(h.V); }
        if (xs.Count >= 2)
            elements.Add(new ScatterElement { Xs = [.. xs], Ys = [.. ys], Stroke = Brushes.Crimson, StrokeThickness = 2 });
        if (_drawPoints.Count >= 2)
            elements.Add(new ScatterElement
            {
                Xs = [xs[^1], _drawPoints[0].U], Ys = [ys[^1], _drawPoints[0].V],
                Stroke = Brushes.LightCoral, StrokeThickness = 1
            });
        elements.Add(new MarkerElement { Xs = [.. _drawPoints.Select(p => p.U)], Ys = [.. _drawPoints.Select(p => p.V)], Fill = Brushes.Crimson, MarkerSize = 7 });
    }

    /// <summary>Щелчок по плану армирования: в режиме рисования — вершина, иначе — выбор зоны.</summary>
    public void OnRebarCanvasClick(double u, double v, double pixelSize)
    {
        if (IsDrawingZone) AddDrawingPoint(u, v, pixelSize);
        else SelectZoneAtPoint(u, v);
    }

    // ------------------------------------------------------------------ вкладка «Полоса»

    /// <summary>Вкладка доступна у сохранённого элемента с регионом.</summary>
    public bool CanBuildStrip => _existingRegion != null && _existingMember != null;

    double? _stripStartU, _stripStartV, _stripEndU, _stripEndV;
    public double? StripStartU { get => _stripStartU; set { _stripStartU = value; OnPropertyChanged(); RefreshStripPlot(); } }
    public double? StripStartV { get => _stripStartV; set { _stripStartV = value; OnPropertyChanged(); RefreshStripPlot(); } }
    public double? StripEndU { get => _stripEndU; set { _stripEndU = value; OnPropertyChanged(); RefreshStripPlot(); } }
    public double? StripEndV { get => _stripEndV; set { _stripEndV = value; OnPropertyChanged(); RefreshStripPlot(); } }

    double _stripWidthM = 1.0;
    /// <summary>Ширина полосы, м.</summary>
    public double StripWidthM { get => _stripWidthM; set { _stripWidthM = value; OnPropertyChanged(); RefreshStripPlot(); } }

    int _stripWidthPoints = 2;
    /// <summary>Число точек интегрирования по ширине полосы.</summary>
    public int StripWidthPoints { get => _stripWidthPoints; set { _stripWidthPoints = Math.Max(1, value); OnPropertyChanged(); } }

    double _stripStationFraction = 0.5;
    /// <summary>Доля длины полосы (0..1), на которой берётся раскладка армирования по ширине.</summary>
    public double StripStationFraction { get => _stripStationFraction; set { _stripStationFraction = value; OnPropertyChanged(); } }

    string _stripName = "";
    /// <summary>Имя эквивалентного сечения.</summary>
    public string StripName { get => _stripName; set { _stripName = value; OnPropertyChanged(); } }

    /// <summary>Политики редукции.</summary>
    public IReadOnlyList<StripChoice<ReductionPolicy>> StripPolicyOptions { get; } =
    [
        new(ReductionPolicy.ConstitutiveIntegration, Loc.S("EquivalentSectionPolicyConstitutiveIntegration")),
        new(ReductionPolicy.EnergyEquivalent, Loc.S("EquivalentSectionPolicyEnergyEquivalent")),
        new(ReductionPolicy.DirectUniaxial, Loc.S("EquivalentSectionPolicyDirectUniaxial")),
    ];

    /// <summary>Формулировки балки.</summary>
    public IReadOnlyList<StripChoice<StripBeamFormulation>> StripFormulationOptions { get; } =
    [
        new(StripBeamFormulation.EulerBernoulli, Loc.S("StripFormulationEuler")),
        new(StripBeamFormulation.Timoshenko, Loc.S("StripFormulationTimoshenko")),
    ];

    ReductionPolicy _stripPolicy = ReductionPolicy.ConstitutiveIntegration;
    public ReductionPolicy StripPolicy { get => _stripPolicy; set { _stripPolicy = value; OnPropertyChanged(); } }

    StripBeamFormulation _stripFormulation = StripBeamFormulation.EulerBernoulli;
    public StripBeamFormulation StripFormulation { get => _stripFormulation; set { _stripFormulation = value; OnPropertyChanged(); } }

    /// <summary>Источники армирования полосы, доступные для схемы.</summary>
    public ObservableCollection<StripRebarSourceOption> StripRebarSources { get; } = [];

    string _stripRebarSourceKey = "";
    public string StripRebarSourceKey { get => _stripRebarSourceKey; set { _stripRebarSourceKey = value ?? ""; OnPropertyChanged(); } }

    /// <summary>Эквивалентные сечения этого региона.</summary>
    public ObservableCollection<EquivalentSection> RegionEquivalentSections { get; } = [];

    EquivalentSection? _selectedEquivalent;
    /// <summary>Выбранное эквивалентное сечение региона; null — новое. Выбор загружает его параметры.</summary>
    public EquivalentSection? SelectedEquivalent
    {
        get => _selectedEquivalent;
        set { _selectedEquivalent = value; OnPropertyChanged(); LoadFromEquivalent(value); }
    }

    bool _isPickingStripAxis;
    /// <summary>Идёт указание оси полосы: первый щелчок — начало, второй — конец.</summary>
    public bool IsPickingStripAxis { get => _isPickingStripAxis; private set { _isPickingStripAxis = value; OnPropertyChanged(); } }

    IReadOnlyList<PlotElement> _stripPlotElements = [];
    public IReadOnlyList<PlotElement> StripPlotElements
    {
        get => _stripPlotElements;
        private set { _stripPlotElements = value; OnPropertyChanged(); }
    }

    IReadOnlyList<FemValidationDiagnostic> _stripDiagnostics = [];
    public IReadOnlyList<FemValidationDiagnostic> StripDiagnostics
    {
        get => _stripDiagnostics;
        private set { _stripDiagnostics = value; OnPropertyChanged(); }
    }

    string _stripSummary = "";
    /// <summary>Результат построения: длина, жёсткости, состояние.</summary>
    public string StripSummary { get => _stripSummary; private set { _stripSummary = value; OnPropertyChanged(); } }

    /// <summary>Длина оси полосы, м (по указанным точкам); 0 — ось не задана.</summary>
    public double StripLengthM => StripAxisSet
        ? Math.Sqrt(Math.Pow(_stripEndU!.Value - _stripStartU!.Value, 2) + Math.Pow(_stripEndV!.Value - _stripStartV!.Value, 2))
        : 0.0;

    bool StripAxisSet => _stripStartU.HasValue && _stripStartV.HasValue && _stripEndU.HasValue && _stripEndV.HasValue;

    public ICommand PickStripAxisCommand { get; private set; } = null!;
    public ICommand ClearStripAxisCommand { get; private set; } = null!;
    public ICommand BuildStripCommand { get; private set; } = null!;
    public ICommand NewStripCommand { get; private set; } = null!;

    bool _stripInitialized;

    /// <summary>Подготовить вкладку при первом открытии: команды, источники армирования, сечения региона.</summary>
    public void EnsureStripInitialized()
    {
        if (_stripInitialized) return;
        _stripInitialized = true;

        PickStripAxisCommand = new RelayCommand(_ => BeginStripAxisPicking(), _ => CanBuildStrip);
        ClearStripAxisCommand = new RelayCommand(_ => ClearStripAxis());
        BuildStripCommand = new RelayCommand(_ => BuildStrip(), _ => CanBuildStrip);
        NewStripCommand = new RelayCommand(_ => SelectedEquivalent = null);
        OnPropertyChanged(nameof(PickStripAxisCommand));
        OnPropertyChanged(nameof(ClearStripAxisCommand));
        OnPropertyChanged(nameof(BuildStripCommand));
        OnPropertyChanged(nameof(NewStripCommand));

        if (!CanBuildStrip) { RefreshStripPlot(); return; }

        LoadStripSources();
        ReloadRegionEquivalentSections();
        StripName = NextStripName();
        RefreshStripPlot();
    }

    string NextStripName()
    {
        var taken = _app.db.EquivalentSections.Select(e => e.Tag).ToHashSet(StringComparer.Ordinal);
        int n = RegionEquivalentSections.Count + 1;
        string name;
        do name = $"{Tag} · полоса {n++}"; while (taken.Contains(name));
        return name;
    }

    void ReloadRegionEquivalentSections()
    {
        RegionEquivalentSections.Clear();
        if (_existingRegion == null) return;
        foreach (var e in _app.db.EquivalentSections
                     .Where(e => e.SourceSchemaId == _schema.Id && e.SourceRegionId == _existingRegion.Id)
                     .OrderBy(e => e.Num).ThenBy(e => e.Id))
            RegionEquivalentSections.Add(e);
    }

    /// <summary>
    /// Источники армирования: раскладка региона всегда; у импортированной схемы — заданное и подобранное,
    /// если их данные есть. Умолчание: подобранное → заданное → раскладка (при наличии зон — раскладка).
    /// </summary>
    void LoadStripSources()
    {
        StripRebarSources.Clear();
        StripRebarSources.Add(new("", Loc.S("StripRebarSourceLayout")));

        var data = FemCheckSchemaData.Load(_app.db, _schema.Id);
        bool selected = data.IsScad ? data.ScadSelected != null : data.Asp != null;
        bool assigned = data.IsScad ? data.ScadAssigned is { Plates.Count: > 0 } : data.Rbt != null;
        if (selected) StripRebarSources.Add(new(FemCheckRebarSource.Selected, FemCheckContext.SourceName(FemCheckRebarSource.Selected)));
        if (assigned) StripRebarSources.Add(new(FemCheckRebarSource.Assigned, FemCheckContext.SourceName(FemCheckRebarSource.Assigned)));
        StripRebarSources.Add(new(FemCheckRebarSource.Section, FemCheckContext.SourceName(FemCheckRebarSource.Section)));

        bool imported = _app.db.HasImportedMesh(_schema.Id);
        StripRebarSourceKey = imported && RebarZones.Count == 0
            ? (selected ? FemCheckRebarSource.Selected : assigned ? FemCheckRebarSource.Assigned : "")
            : "";
    }

    void LoadFromEquivalent(EquivalentSection? e)
    {
        StripDiagnostics = [];
        StripSummary = "";
        if (e == null)
        {
            StripName = NextStripName();
            RefreshStripPlot();
            return;
        }
        var line = e.Strip.Geometry.CenterLine;
        if (line.Count >= 2)
        {
            _stripStartU = line[0].U; _stripStartV = line[0].V;
            _stripEndU = line[^1].U; _stripEndV = line[^1].V;
        }
        _stripWidthM = e.Strip.ExplicitWidthM;
        _stripWidthPoints = e.WidthIntegrationPoints;
        _stripStationFraction = e.SpanStationFraction;
        _stripPolicy = e.ReductionPolicy;
        _stripFormulation = e.BeamFormulation;
        _stripRebarSourceKey = e.RebarSourceKey ?? "";
        _stripName = e.Tag;
        foreach (string p in new[]
                 {
                     nameof(StripStartU), nameof(StripStartV), nameof(StripEndU), nameof(StripEndV), nameof(StripWidthM),
                     nameof(StripWidthPoints), nameof(StripStationFraction), nameof(StripPolicy), nameof(StripFormulation),
                     nameof(StripRebarSourceKey), nameof(StripName), nameof(StripLengthM)
                 })
            OnPropertyChanged(p);
        StripSummary = DescribeEquivalent(e);
        StripDiagnostics = e.Diagnostics;
        RefreshStripPlot();
    }

    /// <summary>Начать указание оси: первый щелчок — начало, второй — конец.</summary>
    public void BeginStripAxisPicking()
    {
        _stripHover = null;
        _stripStartU = _stripStartV = _stripEndU = _stripEndV = null;
        _stripPickedFirst = false;
        IsPickingStripAxis = true;
        foreach (string p in new[] { nameof(StripStartU), nameof(StripStartV), nameof(StripEndU), nameof(StripEndV), nameof(StripLengthM) })
            OnPropertyChanged(p);
        RefreshStripPlot();
    }

    void ClearStripAxis()
    {
        IsPickingStripAxis = false;
        _stripPickedFirst = false;
        _stripHover = null;
        _stripStartU = _stripStartV = _stripEndU = _stripEndV = null;
        foreach (string p in new[] { nameof(StripStartU), nameof(StripStartV), nameof(StripEndU), nameof(StripEndV), nameof(StripLengthM) })
            OnPropertyChanged(p);
        RefreshStripPlot();
    }

    /// <summary>Отменить указание оси (Esc), оставив ранее заданные точки, если они есть.</summary>
    public void CancelStripAxisPicking()
    {
        if (!IsPickingStripAxis) return;
        IsPickingStripAxis = false;
        _stripPickedFirst = false;
        _stripHover = null;
        RefreshStripPlot();
    }

    bool _stripPickedFirst;
    (double U, double V)? _stripHover;

    /// <summary>Щелчок по плану полосы: в режиме указания оси — точка оси (с привязкой).</summary>
    public void OnStripCanvasClick(double u, double v, double pixelSize)
    {
        if (!IsPickingStripAxis) return;
        (double U, double V)? anchor = _stripPickedFirst ? (_stripStartU!.Value, _stripStartV!.Value) : null;
        var p = SnapPoint(u, v, pixelSize, anchor, includeZones: false);
        if (!_stripPickedFirst)
        {
            _stripStartU = p.U; _stripStartV = p.V;
            _stripPickedFirst = true;
        }
        else
        {
            _stripEndU = p.U; _stripEndV = p.V;
            _stripPickedFirst = false;
            IsPickingStripAxis = false;
            _stripHover = null;
        }
        foreach (string n in new[] { nameof(StripStartU), nameof(StripStartV), nameof(StripEndU), nameof(StripEndV), nameof(StripLengthM) })
            OnPropertyChanged(n);
        RefreshStripPlot();
    }

    /// <summary>Курсор над планом полосы: резиновая ось до курсора.</summary>
    public void OnStripCanvasHover(double u, double v, double pixelSize)
    {
        if (!IsPickingStripAxis || !_stripPickedFirst) return;
        _stripHover = SnapPoint(u, v, pixelSize, (_stripStartU!.Value, _stripStartV!.Value), includeZones: false);
        RefreshStripPlot();
    }

    void RefreshStripPlot()
    {
        var elements = new List<PlotElement>();
        EnsureFeGeometry();
        if (_feLoops is { Count: > 0 })
        {
            var xs = new List<double>();
            var ys = new List<double>();
            foreach (var loop in _feLoops)
                for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
                {
                    xs.Add(loop[j].U); ys.Add(loop[j].V);
                    xs.Add(loop[i].U); ys.Add(loop[i].V);
                }
            elements.Add(new LineSegmentsElement { Xs = [.. xs], Ys = [.. ys], Stroke = Brushes.LightGray, StrokeThickness = 0.5 });
        }
        if (Hull != null && Hull.X.Count >= 3)
            elements.Add(new PolygonElement { Xs = [.. Hull.X], Ys = [.. Hull.Y], Fill = null, Stroke = Brushes.DimGray, StrokeThickness = 1.5 });
        foreach (var hole in Holes)
            if (hole.X.Count >= 3)
                elements.Add(new PolygonElement { Xs = [.. hole.X], Ys = [.. hole.Y], Fill = null, Stroke = Brushes.DimGray, StrokeDashArray = [4, 2] });
        foreach (var zone in RebarZones)
            if (zone.Model.Polygon.Count >= 3)
                elements.Add(new PolygonElement
                {
                    Xs = [.. zone.Model.Polygon.Select(p => p.U)], Ys = [.. zone.Model.Polygon.Select(p => p.V)],
                    Fill = null, Stroke = Brushes.SandyBrown, StrokeThickness = 1, StrokeDashArray = [3, 2]
                });

        // Ось и полоса: по двум точкам либо до курсора, пока вторая не указана.
        (double U, double V)? a = _stripStartU.HasValue && _stripStartV.HasValue ? (_stripStartU.Value, _stripStartV.Value) : null;
        (double U, double V)? b = _stripEndU.HasValue && _stripEndV.HasValue ? (_stripEndU.Value, _stripEndV.Value) : _stripHover;
        if (a is { } p0 && b is { } p1)
        {
            double du = p1.U - p0.U, dv = p1.V - p0.V;
            double len = Math.Sqrt(du * du + dv * dv);
            if (len > 1e-9 && StripWidthM > 0)
            {
                double nu = -dv / len * StripWidthM / 2, nv = du / len * StripWidthM / 2;
                elements.Add(new PolygonElement
                {
                    Xs = [p0.U + nu, p1.U + nu, p1.U - nu, p0.U - nu],
                    Ys = [p0.V + nv, p1.V + nv, p1.V - nv, p0.V - nv],
                    Fill = new SolidColorBrush(Color.FromArgb(60, 0, 150, 90)),
                    Stroke = Brushes.SeaGreen, StrokeThickness = 1
                });
            }
            elements.Add(new ScatterElement { Xs = [p0.U, p1.U], Ys = [p0.V, p1.V], Stroke = Brushes.DarkGreen, StrokeThickness = 2 });
            elements.Add(new TextElement
            {
                X = 0.5 * (p0.U + p1.U), Y = 0.5 * (p0.V + p1.V),
                Text = $"{len:0.###} м", Foreground = Brushes.DarkGreen
            });
        }
        if (a is { } s)
        {
            elements.Add(new MarkerElement { Xs = [s.U], Ys = [s.V], Fill = Brushes.ForestGreen, MarkerSize = 9 });
            elements.Add(new TextElement { X = s.U, Y = s.V, Text = "A", Foreground = Brushes.DarkGreen, Background = null, FontSize = 12 });
        }
        if (_stripEndU.HasValue && _stripEndV.HasValue)
        {
            elements.Add(new MarkerElement { Xs = [_stripEndU.Value], Ys = [_stripEndV.Value], Fill = Brushes.Firebrick, MarkerSize = 9 });
            elements.Add(new TextElement { X = _stripEndU.Value, Y = _stripEndV.Value, Text = "B", Foreground = Brushes.Firebrick, Background = null, FontSize = 12 });
        }
        StripPlotElements = elements;
    }

    string DescribeEquivalent(EquivalentSection e) => string.Format(
        System.Globalization.CultureInfo.CurrentCulture,
        Loc.S("StripResultSummary"), e.Tag, e.Strip.Geometry.LengthM, e.Strip.ExplicitWidthM, e.EA, e.EIy, e.EIz,
        e.IsCalculable ? Loc.S("EquivalentSectionStatusValid") : Loc.S("EquivalentSectionStatusError"));

    /// <summary>Построить эквивалентное сечение полосы по указанной оси и сохранить его в проекте.</summary>
    public void BuildStrip()
    {
        if (!CanBuildStrip) return;
        var diagnostics = new List<FemValidationDiagnostic>();
        void Done()
        {
            StripDiagnostics = diagnostics;
        }

        if (!StripAxisSet)
        {
            diagnostics.Add(new("strip_axis_not_set", Loc.S("StripAxisNotSet")));
            Done();
            return;
        }
        if (!(StripWidthM > 0))
        {
            diagnostics.Add(new("strip_width_invalid", Loc.S("StripWidthInvalid")));
            Done();
            return;
        }
        if (PlateSection == null)
        {
            diagnostics.Add(new("strip_plate_section_missing", Loc.S("StripNoPlateSection")));
            Done();
            return;
        }

        // Сечение, источник армирования и контур берутся из сохранённого состояния: перед построением — запись.
        bool needSave = _existingMember!.PlateSectionId != PlateSection.Id || RebarLayoutDirty;
        if (needSave && !TrySave(out _))
        {
            diagnostics.AddRange(Diagnostics);
            Done();
            return;
        }

        var region = _app.db.GetPlanarRegions(_schema.Id).FirstOrDefault(r => r.Id == _existingRegion!.Id);
        if (region == null)
        {
            diagnostics.Add(new("equivalent_section_region_not_found", Loc.S("StripRegionNotFound")));
            Done();
            return;
        }

        SupportLocus Locus(double u, double v) => new()
        {
            Frame = new Frame3D(
                PlanarBoundaryFrameConverter.ToGlobalPoint(region.Frame, new PlanarVector3(u, v, 0)),
                region.Frame.LocalX, region.Frame.LocalY, region.Frame.LocalZ),
            StructuralMode = BeamJunctionMode.Support,
            Kind = StripSupportKind.Manual,
        };

        string name = string.IsNullOrWhiteSpace(StripName) ? NextStripName() : StripName.Trim();
        var built = PlateStripGeometryBuilder.Build(
            name, region,
            Locus(_stripStartU!.Value, _stripStartV!.Value), Locus(_stripEndU!.Value, _stripEndV!.Value),
            StripWidthM);
        diagnostics.AddRange(built.Diagnostics);
        if (!built.IsCalculable || built.Analogy == null)
        {
            Done();
            return;
        }

        var materials = _app.Materials.Where(m => m.Id != 0).ToDictionary(m => m.Id);
        var service = new EquivalentSectionProjectService(_app.db, materials);
        var existing = SelectedEquivalent;
        var result = service.BuildAndSave(
            built.Analogy, _schema.Id, region.Id, CalcType.C, StripPolicy, StripWidthPoints,
            Math.Clamp(StripStationFraction, 0.0, 1.0), existing, StripFormulation, null, StripRebarSourceKey);
        diagnostics.AddRange(result.Diagnostics);

        if (result.Section is { } section)
        {
            if (existing == null)
            {
                // Новое сечение: номер в конце списка (имя уже в Tag из идентификатора полосы).
                section.Num = _app.db.EquivalentSections.Where(e => !ReferenceEquals(e, section))
                    .Select(e => e.Num).DefaultIfEmpty(0).Max() + 1;
                _app.db.SaveEquivalentSection(section);
            }
            ReloadRegionEquivalentSections();
            _selectedEquivalent = RegionEquivalentSections.FirstOrDefault(e => e.Id == section.Id) ?? section;
            OnPropertyChanged(nameof(SelectedEquivalent));
            StripSummary = DescribeEquivalent(section);
            _app.LogService.Info(string.Format(Loc.S("StripBuiltLog"), section.Tag, section.Strip.Geometry.LengthM));
        }
        else
        {
            StripSummary = Loc.S("StripBuildFailed");
        }
        Done();
    }
}
