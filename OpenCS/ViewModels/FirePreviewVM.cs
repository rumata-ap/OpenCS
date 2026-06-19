using CScore;
using CScore.Fire;
using CScore.Fire.Entities;
using OpenCS.Utilites;
using OpenCS.Views;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenCS.ViewModels;

/// <summary>Ребро контура для отрисовки превью огневого сечения.</summary>
public sealed record FirePreviewEdgeDraw(
    Point A,
    Point B,
    int EdgeIndex,
    string BcType,
    string ContourType,
    int? HoleIndex);

/// <summary>Отверстие контура.</summary>
public sealed record FirePreviewHoleDraw(
    IReadOnlyList<Point> Vertices,
    IReadOnlyList<FirePreviewEdgeDraw> Edges,
    int HoleIndex);

/// <summary>Треугольник T3/T6 для оверлея сетки (контур — 3 вершины).</summary>
public sealed record FirePreviewTriDraw(
    IReadOnlyList<Point> Vertices,
    double MinAngleDeg);

/// <summary>Арматурная точка на превью.</summary>
public sealed record FirePreviewRebarDraw(Point Center, double RadiusM);

/// <summary>ViewModel интерактивного превью огневого сечения (ГУ + сетка МКЭ).</summary>
public sealed class FirePreviewVM : ViewModelBase
{
    static readonly Brush s_fireBrush = CreateFrozenBrush(224, 32, 32);
    static readonly Brush s_ambientBrush = CreateFrozenBrush(32, 80, 220);
    static readonly Brush s_adiabaticBrush = CreateFrozenBrush(128, 128, 128);
    static readonly Brush s_fillBrush = CreateFrozenBrush(240, 240, 240, alpha: 80);
    static readonly Brush s_holeFillBrush = CreateFrozenBrush(200, 200, 200, alpha: 50);
    static readonly Brush s_rebarFill = CreateFrozenBrush(204, 51, 51);

    int _meshBuildToken;

    bool _hasGeometry;
    public bool HasGeometry
    {
        get => _hasGeometry;
        private set { _hasGeometry = value; OnPropertyChanged(); }
    }

    bool _showMesh;
    public bool ShowMesh
    {
        get => _showMesh;
        set { _showMesh = value; OnPropertyChanged(); BumpRedraw(); }
    }

    bool _showEdgeLabels = true;
    public bool ShowEdgeLabels
    {
        get => _showEdgeLabels;
        set { _showEdgeLabels = value; OnPropertyChanged(); BumpRedraw(); }
    }

    string _bcPreset = "manual";
    public string BcPreset
    {
        get => _bcPreset;
        set
        {
            _bcPreset = value ?? "manual";
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsManualBc));
            BumpRedraw();
        }
    }

    public bool IsManualBc => string.Equals(_bcPreset.Trim(), "manual", StringComparison.OrdinalIgnoreCase);

    string _meshQualityText = "";
    public string MeshQualityText
    {
        get => _meshQualityText;
        private set { _meshQualityText = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<Point> OuterHull { get; private set; } = [];
    public IReadOnlyList<FirePreviewHoleDraw> Holes { get; private set; } = [];
    public IReadOnlyList<FirePreviewEdgeDraw> OuterEdges { get; private set; } = [];
    public IReadOnlyList<FirePreviewRebarDraw> Rebars { get; private set; } = [];
    public IReadOnlyList<FirePreviewTriDraw> MeshTriangles { get; private set; } = [];
    /// <summary>Узлы в серединах рёбер (T6); пусто для T3.</summary>
    public IReadOnlyList<Point> MeshMidsideNodes { get; private set; } = [];

    public int NeedRedraw { get; private set; }
    public ICommand FitAllCommand { get; }
    public event Action? FitAllRequested;
    public event Action? EdgeBcChanged;

    FireSectionDef? _def;
    CrossSection? _section;
    MaterialArea? _area;
    double _meshStepM = 0.01;
    string _algorithm = "ruppert";
    int _smoothIterTri = 5;
    bool _useQuadratic;

    public FirePreviewVM()
    {
        FitAllCommand = new RelayCommand(_ => FitAllRequested?.Invoke());
    }

    public void Configure(
        FireSectionDef def,
        CrossSection? section,
        string bcPreset,
        double meshStepM,
        string algorithm,
        int smoothIterTri,
        string meshElementType)
    {
        _def = def;
        _section = section;
        _area = FirePreviewBuilder.GetPrimaryConcreteArea(section);
        BcPreset = bcPreset;
        _meshStepM = meshStepM;
        _algorithm = algorithm;
        _smoothIterTri = smoothIterTri;
        _useQuadratic = string.Equals(meshElementType?.Trim(), "quadratic", StringComparison.OrdinalIgnoreCase);
        RebuildGeometry();
        if (ShowMesh)
            RebuildMeshAsync();
    }

    public void OnMeshStepChanged(double meshStepM)
    {
        _meshStepM = meshStepM;
        if (ShowMesh)
            RebuildMeshAsync();
    }

    public void OnShowMeshChanged(bool show)
    {
        ShowMesh = show;
        if (show)
            RebuildMeshAsync();
        else
        {
            MeshTriangles = [];
            MeshMidsideNodes = [];
            MeshQualityText = "";
            OnPropertyChanged(nameof(MeshTriangles));
            OnPropertyChanged(nameof(MeshMidsideNodes));
            BumpRedraw();
        }
    }

    public void RebuildGeometry()
    {
        if (_def == null || _section == null || _area?.Hull == null || _area.Hull.X.Count < 3)
        {
            HasGeometry = false;
            OuterHull = [];
            Holes = [];
            OuterEdges = [];
            Rebars = [];
            OnPropertyChanged(nameof(OuterHull));
            OnPropertyChanged(nameof(Holes));
            OnPropertyChanged(nameof(OuterEdges));
            OnPropertyChanged(nameof(Rebars));
            BumpRedraw();
            return;
        }

        HasGeometry = true;
        var hull = _area.Hull;
        int n = hull.X.Count;
        bool closed = n >= 2 && NearlyEqual(hull.X[0], hull.X[n - 1]) && NearlyEqual(hull.Y[0], hull.Y[n - 1]);
        if (closed) n--;

        var outerPts = new List<Point>(n);
        var xs = new double[n];
        var ys = new double[n];
        for (int i = 0; i < n; i++)
        {
            xs[i] = hull.X[i];
            ys[i] = hull.Y[i];
            outerPts.Add(new Point(xs[i], ys[i]));
        }

        var bcTypes = FirePreviewBuilder.ResolveEdgeBcTypes(_def, xs, ys, n, BcPreset);
        var outerEdges = new List<FirePreviewEdgeDraw>(n);
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            outerEdges.Add(new FirePreviewEdgeDraw(
                new Point(xs[i], ys[i]),
                new Point(xs[j], ys[j]),
                i,
                bcTypes[i],
                "outer",
                null));
        }

        var holes = new List<FirePreviewHoleDraw>();
        for (int hi = 0; hi < _area.Holes.Count; hi++)
        {
            var hole = _area.Holes[hi];
            int hn = hole.X.Count;
            if (hn < 3) continue;
            var hPts = new List<Point>(hn);
            var hx = new double[hn];
            var hy = new double[hn];
            for (int i = 0; i < hn; i++)
            {
                hx[i] = hole.X[i];
                hy[i] = hole.Y[i];
                hPts.Add(new Point(hx[i], hy[i]));
            }

            var holeBc = FirePreviewBuilder.ResolveHoleEdgeBcTypes(_def, hx, hy, hn, hi);
            var hEdges = new List<FirePreviewEdgeDraw>(hn);
            for (int i = 0; i < hn; i++)
            {
                int j = (i + 1) % hn;
                hEdges.Add(new FirePreviewEdgeDraw(
                    new Point(hx[i], hy[i]),
                    new Point(hx[j], hy[j]),
                    i,
                    holeBc[i],
                    "hole",
                    hi));
            }
            holes.Add(new FirePreviewHoleDraw(hPts, hEdges, hi));
        }

        var rebars = new List<FirePreviewRebarDraw>();
        foreach (var a in _section.Areas)
        {
            foreach (var f in a.Fibers)
            {
                if (f.TypeFiber != FiberType.point) continue;
                double r = f.Diameter > 0 ? f.Diameter * 0.5 : 0.005;
                rebars.Add(new FirePreviewRebarDraw(new Point(f.X, f.Y), r));
            }
        }

        OuterHull = outerPts;
        Holes = holes;
        OuterEdges = outerEdges;
        Rebars = rebars;
        OnPropertyChanged(nameof(OuterHull));
        OnPropertyChanged(nameof(Holes));
        OnPropertyChanged(nameof(OuterEdges));
        OnPropertyChanged(nameof(Rebars));
        BumpRedraw();
    }

    public void CycleEdgeBc(FirePreviewEdgeDraw edge)
    {
        if (_def == null || !IsManualBc) return;

        string next = edge.BcType switch
        {
            "fire" => "ambient",
            "ambient" => "adiabatic",
            _ => "fire"
        };

        var existing = _def.Edges.FirstOrDefault(e =>
            string.Equals(e.ContourType, edge.ContourType, StringComparison.OrdinalIgnoreCase) &&
            e.HoleIndex == edge.HoleIndex &&
            e.EdgeIndex == edge.EdgeIndex);

        if (existing != null)
        {
            existing.BcType = next;
            ApplyBcDefaults(existing);
        }
        else
        {
            var def = new FireBoundaryEdgeDef
            {
                EdgeIndex = edge.EdgeIndex,
                ContourType = edge.ContourType,
                HoleIndex = edge.HoleIndex,
                BcType = next
            };
            ApplyBcDefaults(def);
            _def.Edges.Add(def);
        }

        RebuildGeometry();
        EdgeBcChanged?.Invoke();
    }

    static void ApplyBcDefaults(FireBoundaryEdgeDef e)
    {
        switch (e.BcType)
        {
            case "fire":
                e.AlphaConv = e.AlphaConv > 0 ? e.AlphaConv : 25.0;
                e.Emissivity = e.Emissivity > 0 ? e.Emissivity : 0.7;
                break;
            case "ambient":
                e.AlphaConv = e.AlphaConv > 0 ? e.AlphaConv : 9.0;
                e.Emissivity = e.Emissivity > 0 ? e.Emissivity : 0.8;
                break;
            default:
                e.BcType = "adiabatic";
                e.AlphaConv = 0;
                e.Emissivity = 0;
                break;
        }
        if (e.TAmbientCelsius == 0) e.TAmbientCelsius = 20.0;
    }

    void RebuildMeshAsync()
    {
        if (_section == null || !HasGeometry)
        {
            MeshTriangles = [];
            MeshMidsideNodes = [];
            MeshQualityText = "";
            OnPropertyChanged(nameof(MeshTriangles));
            OnPropertyChanged(nameof(MeshMidsideNodes));
            BumpRedraw();
            return;
        }

        int token = ++_meshBuildToken;
        MeshQualityText = Loc.S("FireSection_MeshComputing");
        OnPropertyChanged(nameof(MeshQualityText));

        var section = _section;
        double meshStep = _meshStepM;
        string algo = _algorithm;
        int smooth = _smoothIterTri;
        bool useQuadratic = _useQuadratic;

        Task.Run(() =>
        {
            try
            {
                var result = FireMeshBuilder.Build(section, meshStep, algo, smooth, useQuadratic);
                var mesh = result.Mesh;
                var tris = new List<FirePreviewTriDraw>(mesh.NElements);
                var midsideNodes = new List<Point>();
                double minAngleGlobal = double.MaxValue;
                int nTri = mesh.NElements;

                if (useQuadratic && result.LinearMesh != null)
                {
                    int nLinear = result.LinearMesh.NNodes;
                    for (int k = nLinear; k < mesh.NNodes; k++)
                        midsideNodes.Add(new Point(mesh.X[k], mesh.Y[k]));
                }

                foreach (var el in mesh.Elements)
                {
                    if (el.Length >= 6)
                    {
                        var p0 = new Point(mesh.X[el[0]], mesh.Y[el[0]]);
                        var p1 = new Point(mesh.X[el[1]], mesh.Y[el[1]]);
                        var p2 = new Point(mesh.X[el[2]], mesh.Y[el[2]]);
                        AddPreviewElement(tris, ref minAngleGlobal, [p0, p1, p2], p0, p1, p2);
                    }
                    else if (el.Length == 3)
                    {
                        var p0 = new Point(mesh.X[el[0]], mesh.Y[el[0]]);
                        var p1 = new Point(mesh.X[el[1]], mesh.Y[el[1]]);
                        var p2 = new Point(mesh.X[el[2]], mesh.Y[el[2]]);
                        AddPreviewElement(tris, ref minAngleGlobal, [p0, p1, p2], p0, p1, p2);
                    }
                }

                string quality = string.Format(
                    CultureInfo.InvariantCulture,
                    Loc.S(useQuadratic ? "FireSection_MeshQualityQuadratic" : "FireSection_MeshQuality"),
                    nTri,
                    minAngleGlobal < double.MaxValue ? minAngleGlobal : 0);

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (token != _meshBuildToken) return;
                    MeshTriangles = tris;
                    MeshMidsideNodes = midsideNodes;
                    MeshQualityText = quality;
                    OnPropertyChanged(nameof(MeshTriangles));
                    OnPropertyChanged(nameof(MeshMidsideNodes));
                    OnPropertyChanged(nameof(MeshQualityText));
                    BumpRedraw();
                });
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (token != _meshBuildToken) return;
                    MeshTriangles = [];
                    MeshMidsideNodes = [];
                    MeshQualityText = string.Format(Loc.S("FireSection_MeshError"), ex.Message);
                    OnPropertyChanged(nameof(MeshTriangles));
                    OnPropertyChanged(nameof(MeshMidsideNodes));
                    OnPropertyChanged(nameof(MeshQualityText));
                    BumpRedraw();
                });
            }
        });
    }

    static void AddPreviewElement(
        List<FirePreviewTriDraw> tris,
        ref double minAngleGlobal,
        Point[] outline,
        Point p0, Point p1, Point p2)
    {
        double minAng = MinTriangleAngleDeg(p0, p1, p2);
        if (minAng < minAngleGlobal) minAngleGlobal = minAng;
        tris.Add(new FirePreviewTriDraw(outline, minAng));
    }

    static double MinTriangleAngleDeg(Point a, Point b, Point c)
    {
        static double Angle(Point p, Point q, Point r)
        {
            double bax = p.X - q.X, bay = p.Y - q.Y;
            double bcx = r.X - q.X, bcy = r.Y - q.Y;
            double dot = bax * bcx + bay * bcy;
            double denom = Math.Sqrt(bax * bax + bay * bay) * Math.Sqrt(bcx * bcx + bcy * bcy) + 1e-30;
            return Math.Acos(Math.Clamp(dot / denom, -1.0, 1.0)) * 180.0 / Math.PI;
        }
        return Math.Min(Angle(a, b, c), Math.Min(Angle(b, c, a), Angle(c, a, b)));
    }

    void BumpRedraw()
    {
        NeedRedraw++;
        OnPropertyChanged(nameof(NeedRedraw));
    }

    public static Brush BrushForBc(string bcType) => bcType switch
    {
        "fire" => s_fireBrush,
        "ambient" => s_ambientBrush,
        _ => s_adiabaticBrush
    };

    public static Brush OuterFillBrush => s_fillBrush;
    public static Brush HoleFillBrush => s_holeFillBrush;
    public static Brush RebarFillBrush => s_rebarFill;
    public static Brush MeshMidsideNodeBrush => s_meshMidsideBrush;

    static readonly Brush s_meshMidsideBrush = CreateFrozenBrush(20, 60, 160);

    public static Color MeshColorForAngle(double minAngleDeg)
    {
        double t = Math.Clamp(minAngleDeg / 60.0, 0, 1);
        byte r = (byte)(255 * (1 - t) + 50 * t);
        byte g = (byte)(50 * (1 - t) + 200 * t);
        byte b = (byte)(50 * (1 - t) + 50 * t);
        return Color.FromArgb(180, r, g, b);
    }

    static Brush CreateFrozenBrush(byte r, byte g, byte b, byte alpha = 255)
    {
        var br = new SolidColorBrush(Color.FromArgb(alpha, r, g, b));
        br.Freeze();
        return br;
    }

    static bool NearlyEqual(double a, double b) => Math.Abs(a - b) <= 1e-12;
}
