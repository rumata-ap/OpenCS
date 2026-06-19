using CScore.Fire;
using OpenCS.Utilites;
using OpenCS.Views.Helpers;
using CSfea.Thermal;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace OpenCS.ViewModels;

/// <summary>Режим раскраски T3-сетки огневого расчёта.</summary>
public enum FireMeshPlotMode
{
    Temperature,
    Gamma,
    Stress,
    Strain
}

/// <summary>Треугольник T3 для отрисовки на канве.</summary>
public sealed record FireTriDraw(
    IReadOnlyList<Point> VerticesMm,
    IReadOnlyList<double> NodeValues,
    Point CentroidMm,
    double Value,
    string Tooltip);

/// <summary>Точка арматуры на канве.</summary>
public sealed record FirePointDraw(
    Point CenterMm,
    double RadiusMm,
    double Value,
    string Tooltip);

/// <summary>ViewModel интерактивной карты T3 (температура, γ, σ, ε).</summary>
public sealed class FireMeshPlotVM : ViewModelBase
{
    public const int NumBands = 10;

    public FireMeshPlotMode Mode { get; }
    public string ColorbarTitle { get; }
    public string TimeLabelText { get; private set; } = "";

    public IReadOnlyList<FireTriDraw> Triangles { get; private set; } = [];
    public IReadOnlyList<FirePointDraw> Points { get; private set; } = [];
    public IReadOnlyList<FireIsolineSegment> Isolines { get; private set; } = [];
    public IReadOnlyList<FireIsolineLabelBuilder.Label> IsolineLabels { get; private set; } = [];
    public IReadOnlyList<FireSectionContourDraw> SectionContours { get; private set; } = [];
    public IReadOnlyList<FireMeshEdgeDraw> MeshEdges { get; private set; } = [];

    public bool IsTemperatureMode => Mode == FireMeshPlotMode.Temperature;

    public double ValueMin { get; private set; }
    public double ValueMax { get; private set; }
    public IReadOnlyList<ColorBand> ColorBands { get; private set; } = [];

    public bool HasTimeSlider { get; }
    public double TimeMin { get; }
    public double TimeMax { get; }

    double _selectedTimeMin;
    public double SelectedTimeMin
    {
        get => _selectedTimeMin;
        set
        {
            double v = Math.Clamp(value, TimeMin, TimeMax);
            if (Math.Abs(v - _selectedTimeMin) < 1e-9) return;
            _selectedTimeMin = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TimeLabelText));
            RebuildGeometry();
        }
    }

    bool _showValues;
    public bool ShowValues
    {
        get => _showValues;
        set { _showValues = value; OnPropertyChanged(); BumpRedraw(); }
    }

    bool _smoothColormap;
    public bool SmoothColormap
    {
        get => _smoothColormap;
        set { _smoothColormap = value; OnPropertyChanged(); BumpRedraw(); }
    }

    bool _showIsolines = true;
    public bool ShowIsolines
    {
        get => _showIsolines;
        set { _showIsolines = value; OnPropertyChanged(); RebuildIsolines(); }
    }

    bool _showIsolineLabels = true;
    public bool ShowIsolineLabels
    {
        get => _showIsolineLabels;
        set
        {
            if (_showIsolineLabels == value) return;
            _showIsolineLabels = value;
            OnPropertyChanged();
            if (_showIsolines && Isolines.Count > 0)
            {
                IsolineLabels = value
                    ? FireIsolineLabelBuilder.Build(Isolines)
                    : [];
                OnPropertyChanged(nameof(IsolineLabels));
            }
        }
    }

    double _isolineStepCelsius = 100.0;
    public double IsolineStepCelsius
    {
        get => _isolineStepCelsius;
        private set
        {
            double v = value > 1e-6 ? value : 100.0;
            if (Math.Abs(v - _isolineStepCelsius) < 1e-9) return;
            _isolineStepCelsius = v;
            OnPropertyChanged();
            RebuildIsolines();
        }
    }

    string _isolineStepText = "100";
    public string IsolineStepText
    {
        get => _isolineStepText;
        set { _isolineStepText = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>Применить шаг изолиний из текстового поля (по LostFocus / Enter).</summary>
    public void CommitIsolineStepText()
    {
        if (double.TryParse(_isolineStepText.Replace(',', '.'),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var step) && step > 0)
        {
            IsolineStepCelsius = step;
            return;
        }

        _isolineStepText = _isolineStepCelsius.ToString("G", CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(IsolineStepText));
    }

    public int NeedRedraw { get; private set; }
    public ICommand FitAllCommand { get; }
    public event Action? FitAllRequested;

    /// <summary>Данные для растеризации плавной карты (как tricontourf в GreenSectionPy).</summary>
    public bool TryGetThermalRasterData(out HeatMesh mesh, out double[] nodalT, out double vmin, out double vmax)
    {
        mesh = null!;
        nodalT = null!;
        vmin = vmax = 0;
        if (!IsTemperatureMode || _thermalMesh == null || _nodalTemperature == null)
            return false;

        int snapIdx = ResolveSnapshotIndex();
        mesh = _thermalMesh;
        nodalT = _nodalTemperature(snapIdx);
        vmin = ValueMin;
        vmax = ValueMax;
        return nodalT.Length == mesh.NNodes;
    }

    readonly Func<int, (IReadOnlyList<FireTriDraw> tris, IReadOnlyList<FirePointDraw> pts)> _buildSnapshot;
    readonly double[]? _timesMin;
    readonly CSfea.Thermal.HeatMesh? _thermalMesh;
    readonly Func<int, double[]>? _nodalTemperature;

    CancellationTokenSource? _isolineCts;
    int _isolineGeneration;
    int _geometryGeneration;
    readonly FireMeshBuildResult? _meshBuildInfo;
    bool _overlayBuildStarted;

    public FireMeshPlotVM(
        FireMeshPlotMode mode,
        string colorbarTitle,
        double[]? timesMin,
        Func<int, (IReadOnlyList<FireTriDraw> tris, IReadOnlyList<FirePointDraw> pts)> buildSnapshot,
        int initialSnapshotIndex = -1,
        CSfea.Thermal.HeatMesh? thermalMesh = null,
        Func<int, double[]>? nodalTemperature = null,
        FireMeshBuildResult? meshBuildInfo = null)
    {
        Mode = mode;
        ColorbarTitle = colorbarTitle;
        _buildSnapshot = buildSnapshot;
        _timesMin = timesMin;
        _thermalMesh = thermalMesh;
        _nodalTemperature = nodalTemperature;
        _meshBuildInfo = meshBuildInfo;

        if (timesMin is { Length: > 0 })
        {
            HasTimeSlider = true;
            TimeMin = timesMin[0];
            TimeMax = timesMin[^1];
            int idx = initialSnapshotIndex < 0 ? timesMin.Length - 1 : Math.Clamp(initialSnapshotIndex, 0, timesMin.Length - 1);
            _selectedTimeMin = timesMin[idx];
        }
        else
        {
            HasTimeSlider = false;
            TimeMin = 0;
            TimeMax = 1;
            _selectedTimeMin = 0;
        }

        FitAllCommand = new RelayCommand(_ => FitAllRequested?.Invoke());
        ScheduleInitialBuild();
    }

    void ScheduleInitialBuild()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            ScheduleOverlayBuild();
            RebuildGeometry();
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            ScheduleOverlayBuild();
            RebuildGeometry();
        });
    }

    void ScheduleOverlayBuild()
    {
        if (_meshBuildInfo == null || _overlayBuildStarted)
            return;

        _overlayBuildStarted = true;
        var info = _meshBuildInfo;
        Task.Run(() =>
        {
            var contours = FirePlotGeometryBuilder.BuildSectionContours(info);
            var edges = FirePlotGeometryBuilder.BuildMeshEdges(info.Mesh);
            return (contours, edges);
        }).ContinueWith(t =>
        {
            if (t.IsFaulted || t.Result == default)
                return;

            Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                SectionContours = t.Result.contours;
                MeshEdges = t.Result.edges;
                OnPropertyChanged(nameof(SectionContours));
                OnPropertyChanged(nameof(MeshEdges));
            });
        });
    }

    void RebuildGeometry()
    {
        int snapIdx = ResolveSnapshotIndex();
        int generation = ++_geometryGeneration;
        var build = _buildSnapshot;

        Task.Run(() => build(snapIdx)).ContinueWith(t =>
        {
            if (t.IsFaulted || t.Result == default)
                return;

            Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (generation != _geometryGeneration)
                    return;

                var (tris, pts) = t.Result;
                ApplyGeometry(tris, pts);
            });
        });
    }

    void ApplyGeometry(IReadOnlyList<FireTriDraw> tris, IReadOnlyList<FirePointDraw> pts)
    {
        Triangles = tris;
        Points = pts;

        var vals = tris.Select(t => t.Value).Concat(pts.Select(p => p.Value)).ToList();
        if (vals.Count == 0)
        {
            ValueMin = 0;
            ValueMax = 1;
        }
        else
        {
            ValueMin = vals.Min();
            ValueMax = vals.Max();
            if (Math.Abs(ValueMax - ValueMin) < 1e-9)
            {
                ValueMin -= 0.5;
                ValueMax += 0.5;
            }
        }

        ColorBands = BuildBands(ValueMin, ValueMax);
        RebuildIsolines();
        TimeLabelText = HasTimeSlider && _timesMin != null
            ? string.Format(CultureInfo.InvariantCulture, "{0:F1} min", _selectedTimeMin)
            : "";
        OnPropertyChanged(nameof(Triangles));
        OnPropertyChanged(nameof(Points));
        OnPropertyChanged(nameof(ValueMin));
        OnPropertyChanged(nameof(ValueMax));
        OnPropertyChanged(nameof(ColorBands));
        OnPropertyChanged(nameof(TimeLabelText));
        BumpRedraw();
    }

    void RebuildIsolines()
    {
        _isolineCts?.Cancel();
        if (!IsTemperatureMode || !_showIsolines || _thermalMesh == null || _nodalTemperature == null)
        {
            Isolines = [];
            IsolineLabels = [];
            OnPropertyChanged(nameof(Isolines));
            OnPropertyChanged(nameof(IsolineLabels));
            return;
        }

        int snapIdx = ResolveSnapshotIndex();
        double[] field = _nodalTemperature(snapIdx);
        var mesh = _thermalMesh;
        double step = _isolineStepCelsius;
        int generation = ++_isolineGeneration;

        var cts = new CancellationTokenSource();
        _isolineCts = cts;
        var token = cts.Token;

        Task.Run(() =>
        {
            var raw = FireIsolineBuilder.Build(mesh, field, step);
            if (token.IsCancellationRequested)
                return;

            var list = new FireIsolineSegment[raw.Count];
            for (int i = 0; i < raw.Count; i++)
            {
                var s = raw[i];
                list[i] = new FireIsolineSegment(s.A, s.B, s.LevelCelsius);
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
                return;

            dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (token.IsCancellationRequested || generation != _isolineGeneration)
                    return;
                Isolines = list;
                IsolineLabels = _showIsolineLabels
                    ? FireIsolineLabelBuilder.Build(list)
                    : [];
                OnPropertyChanged(nameof(Isolines));
                OnPropertyChanged(nameof(IsolineLabels));
            });
        }, token);
    }

    int ResolveSnapshotIndex()
    {
        if (_timesMin is null || _timesMin.Length == 0) return 0;
        int best = 0;
        double bestD = double.MaxValue;
        for (int i = 0; i < _timesMin.Length; i++)
        {
            double d = Math.Abs(_timesMin[i] - _selectedTimeMin);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    void BumpRedraw()
    {
        NeedRedraw++;
        OnPropertyChanged(nameof(NeedRedraw));
    }

    static IReadOnlyList<ColorBand> BuildBands(double vmin, double vmax)
    {
        var bands = new List<ColorBand>(NumBands);
        for (int i = 0; i < NumBands; i++)
        {
            // сверху — max T (тёмный), снизу — min T (светлый), как colorbar matplotlib
            double t0 = (NumBands - 1 - i) / (double)NumBands;
            double t1 = (NumBands - i) / (double)NumBands;
            double v0 = vmin + (vmax - vmin) * t0;
            double v1 = vmin + (vmax - vmin) * t1;
            double vm = (v0 + v1) * 0.5;
            var brush = new System.Windows.Media.SolidColorBrush(
                ColormapHelper.GetThermalColor(vm, vmin, vmax));
            string label = Math.Abs(vmax - vmin) > 100
                ? $"{vm:F0}"
                : $"{vm:G4}";
            bands.Add(new ColorBand(brush, label));
        }
        return bands;
    }
}

/// <summary>Отрезок изолинии температуры на канве (мм).</summary>
public sealed record FireIsolineSegment(Point A, Point B, double LevelCelsius);
