using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OpenCS.Utilites;
using OpenCS.Views.Helpers;

namespace OpenCS.ViewModels;

/// <summary>Данные цветного граничного отрезка (МГЭ).</summary>
public sealed record TorsionSegmentDraw(Point A, Point B, double ValA, double ValB);

/// <summary>VM вкладки карты поля кручения.</summary>
public sealed class TorsionPlotVM : ViewModelBase
{
    public TorsionFieldMode BaseMode { get; }
    public bool IsTauTab => BaseMode != TorsionFieldMode.Potential;
    public bool CanShowPhysicalTau => IsTauTab && _data.HasPhysicalTau;

    public IReadOnlyList<Point> OuterHullMm { get; }
    public IReadOnlyList<IReadOnlyList<Point>> HolesMm { get; }
    public Point? ShearCenterMm { get; }
    public Point? TauMaxPointMm { get; private set; }
    public bool IsBoundaryOnly { get; }
    public string BoundaryNote { get; }
    public bool HasField => _triangles.Count > 0 || _segments.Count > 0 || _nodeFallback.Count > 0;

    public double VMin => _vmin;
    public double VMax => _vmax;
    public string ValueUnit => UnitLabel(ActiveMode);

    public const int NumBands = 8;
    public IReadOnlyList<ColorBand> ColorBands => _colorBands;

    public IReadOnlyList<SmoothFieldBitmap.TriVal> Triangles => _triangles;
    public IReadOnlyList<TorsionSegmentDraw> Segments => _segments;
    public IReadOnlyList<(Point Pt, double Val)> NodeFallback => _nodeFallback;

    bool _showPhysicalTau;
    public bool ShowPhysicalTau
    {
        get => _showPhysicalTau;
        set
        {
            if (!CanShowPhysicalTau || _showPhysicalTau == value) return;
            _showPhysicalTau = value;
            RebuildField();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowPhysicalTau));
            OnPropertyChanged(nameof(ActiveMode));
            OnPropertyChanged(nameof(ValueUnit));
            OnPropertyChanged(nameof(VMin));
            OnPropertyChanged(nameof(VMax));
            OnPropertyChanged(nameof(ColorBands));
            NeedRedraw++;
            OnPropertyChanged(nameof(NeedRedraw));
        }
    }

    public TorsionFieldMode ActiveMode =>
        BaseMode == TorsionFieldMode.Potential
            ? TorsionFieldMode.Potential
            : (_showPhysicalTau ? TorsionFieldMode.TauMpa : TorsionFieldMode.TauUnit);

    public int NeedRedraw { get; private set; }
    public ICommand FitAllCommand { get; }
    public event Action? FitAllRequested;

    readonly TorsionResultData _data;
    List<SmoothFieldBitmap.TriVal> _triangles = [];
    List<TorsionSegmentDraw> _segments = [];
    List<(Point Pt, double Val)> _nodeFallback = [];
    List<ColorBand> _colorBands = [];
    double _vmin, _vmax;

    public TorsionPlotVM(TorsionResultData data, TorsionFieldMode baseMode)
    {
        _data = data;
        BaseMode = baseMode;
        OuterHullMm = data.OuterHullMm;
        HolesMm = data.HolesMm;
        IsBoundaryOnly = data.IsBem && !data.HasFieldMesh;

        if (data.HasShearCenter)
            ShearCenterMm = new Point(data.ShearCenterXmm, data.ShearCenterYmm);

        BoundaryNote = data.IsBem ? Loc.S("TorsionPlotBoundaryNote") : "";

        RebuildField();
        FitAllCommand = new RelayCommand(_ => FitAllRequested?.Invoke());
    }

    public Color GetColor(double val) =>
        ColormapHelper.GetThermalColor(val, _vmin, _vmax);

    public string FormatValue(double v)
    {
        if (!double.IsFinite(v)) return "—";
        double av = Math.Abs(v);
        string fmt = av >= 1e4 || (av > 0 && av < 1e-3) ? "G4" : "F3";
        return v.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
    }

    void RebuildField()
    {
        var mode = ActiveMode;
        (_vmin, _vmax) = _data.FieldRange(mode);
        _triangles = BuildTriangles(_data, mode);
        _segments = BuildSegments(_data, mode);
        _nodeFallback = BuildNodeFallback(_data, mode, _triangles.Count, _segments.Count);
        TauMaxPointMm = FindTauMaxPoint(_data, mode);
        _colorBands = BuildBands(_vmin, _vmax);
        OnPropertyChanged(nameof(TauMaxPointMm));
        OnPropertyChanged(nameof(Triangles));
        OnPropertyChanged(nameof(Segments));
        OnPropertyChanged(nameof(NodeFallback));
    }

    static string UnitLabel(TorsionFieldMode mode) => mode switch
    {
        TorsionFieldMode.Potential => Loc.S("TorsionUnitPotential"),
        TorsionFieldMode.TauMpa => "МПа",
        _ => "мм²"
    };

    static List<ColorBand> BuildBands(double vmin, double vmax)
    {
        var bands = new List<ColorBand>(NumBands);
        double step = (vmax - vmin) / NumBands;
        for (int b = 0; b < NumBands; b++)
        {
            double center = vmin + (b + 0.5) * step;
            var color = ColormapHelper.GetThermalDiscreteColor(center, vmin, vmax, NumBands);
            string label = center.ToString("G3", System.Globalization.CultureInfo.InvariantCulture);
            bands.Add(new ColorBand(new SolidColorBrush(color), label));
        }
        return bands;
    }

    static List<SmoothFieldBitmap.TriVal> BuildTriangles(TorsionResultData data, TorsionFieldMode mode)
    {
        var list = new List<SmoothFieldBitmap.TriVal>();
        if (data.Triangles == null || data.NodeXM == null || data.NodeYM == null) return list;

        int n = data.NodeXM.Length;
        foreach (var tri in data.Triangles)
        {
            if (tri.Length < 3) continue;
            int i0 = tri[0], i1 = tri[1], i2 = tri[2];
            if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= n || i1 >= n || i2 >= n) continue;
            list.Add(new SmoothFieldBitmap.TriVal(
                new Point(data.NodeXM[i0] * 1000, data.NodeYM[i0] * 1000),
                new Point(data.NodeXM[i1] * 1000, data.NodeYM[i1] * 1000),
                new Point(data.NodeXM[i2] * 1000, data.NodeYM[i2] * 1000),
                data.FieldValue(i0, mode),
                data.FieldValue(i1, mode),
                data.FieldValue(i2, mode)));
        }
        return list;
    }

    static List<TorsionSegmentDraw> BuildSegments(TorsionResultData data, TorsionFieldMode mode)
    {
        var list = new List<TorsionSegmentDraw>();
        if (data.BoundaryJ1 == null || data.BoundaryXM == null || data.BoundaryYM == null) return list;

        int n = data.BoundaryXM.Length;
        for (int i = 0; i < n; i++)
        {
            int j = data.BoundaryJ1[i];
            if (j < 0 || j >= n) continue;
            var a = new Point(data.BoundaryXM[i] * 1000, data.BoundaryYM[i] * 1000);
            var b = new Point(data.BoundaryXM[j] * 1000, data.BoundaryYM[j] * 1000);
            double va, vb;
            if (mode == TorsionFieldMode.Potential)
            {
                va = data.Potential?[i] ?? double.NaN;
                vb = data.Potential?[j] ?? double.NaN;
            }
            else
            {
                double val = data.FieldValue(i, mode);
                va = val;
                vb = val;
            }
            list.Add(new TorsionSegmentDraw(a, b, va, vb));
        }
        return list;
    }

    static List<(Point Pt, double Val)> BuildNodeFallback(
        TorsionResultData data, TorsionFieldMode mode, int nTri, int nSeg)
    {
        var list = new List<(Point, double)>();
        if (nTri > 0 || nSeg > 0) return list;
        if (data.NodeXM == null || data.NodeYM == null) return list;
        int n = data.NodeXM.Length;
        for (int i = 0; i < n; i++)
        {
            double v = mode == TorsionFieldMode.Potential
                ? (data.Potential?[i] ?? double.NaN)
                : data.FieldValue(i, mode);
            if (!double.IsFinite(v)) continue;
            list.Add((new Point(data.NodeXM[i] * 1000, data.NodeYM[i] * 1000), v));
        }
        return list;
    }

    static Point? FindTauMaxPoint(TorsionResultData data, TorsionFieldMode mode)
    {
        if (mode == TorsionFieldMode.Potential) return null;
        if (data.TauUnit == null || data.NodeXM == null || data.NodeYM == null) return null;

        int best = -1;
        double bestVal = -1;
        for (int i = 0; i < data.TauUnit.Length; i++)
        {
            double v = Math.Abs(data.TauUnit[i]);
            if (v > bestVal) { bestVal = v; best = i; }
        }
        if (best < 0) return null;
        return new Point(data.NodeXM[best] * 1000, data.NodeYM[best] * 1000);
    }
}
