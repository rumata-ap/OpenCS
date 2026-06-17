using OpenCS.Utilites;
using OpenCS.Views.Helpers;
using System.Globalization;
using System.Windows;
using System.Windows.Input;

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
            BumpRedraw();
        }
    }

    bool _showValues;
    public bool ShowValues
    {
        get => _showValues;
        set { _showValues = value; OnPropertyChanged(); BumpRedraw(); }
    }

    public int NeedRedraw { get; private set; }
    public ICommand FitAllCommand { get; }
    public event Action? FitAllRequested;

    readonly Func<int, (IReadOnlyList<FireTriDraw> tris, IReadOnlyList<FirePointDraw> pts)> _buildSnapshot;
    readonly double[]? _timesMin;

    public FireMeshPlotVM(
        FireMeshPlotMode mode,
        string colorbarTitle,
        double[]? timesMin,
        Func<int, (IReadOnlyList<FireTriDraw> tris, IReadOnlyList<FirePointDraw> pts)> buildSnapshot,
        int initialSnapshotIndex = -1)
    {
        Mode = mode;
        ColorbarTitle = colorbarTitle;
        _buildSnapshot = buildSnapshot;
        _timesMin = timesMin;

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
        RebuildGeometry();
    }

    void RebuildGeometry()
    {
        int snapIdx = ResolveSnapshotIndex();
        var (tris, pts) = _buildSnapshot(snapIdx);
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
        TimeLabelText = HasTimeSlider && _timesMin != null
            ? string.Format(CultureInfo.InvariantCulture, "{0:F1} min", _selectedTimeMin)
            : "";
        OnPropertyChanged(nameof(Triangles));
        OnPropertyChanged(nameof(Points));
        OnPropertyChanged(nameof(ValueMin));
        OnPropertyChanged(nameof(ValueMax));
        OnPropertyChanged(nameof(ColorBands));
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
            double t0 = i / (double)NumBands;
            double t1 = (i + 1) / (double)NumBands;
            double v0 = vmin + (vmax - vmin) * t0;
            double v1 = vmin + (vmax - vmin) * t1;
            double vm = (v0 + v1) * 0.5;
            var brush = new System.Windows.Media.SolidColorBrush(
                ColormapHelper.GetThermalDiscreteColor(vm, vmin, vmax, NumBands));
            string label = Math.Abs(vmax - vmin) > 100
                ? $"{vm:F0}"
                : $"{vm:G4}";
            bands.Add(new ColorBand(brush, label));
        }
        return bands;
    }
}
