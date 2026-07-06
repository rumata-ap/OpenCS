using CScore;
using CSfea.Torsion;
using CSTriangulation;
using OpenCS.Utilites;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace OpenCS.ViewModels;

/// <summary>Треугольник сетки кручения для превью.</summary>
public sealed record TorsionPreviewTriDraw(IReadOnlyList<Point> Vertices, double MinAngleDeg);

/// <summary>Превью МКЭ-сетки кручения в диалоге задачи.</summary>
public sealed class TorsionMeshPreviewVM : ViewModelBase
{
    static readonly Brush s_fillBrush = CreateFrozenBrush(240, 240, 240, 80);
    static readonly Brush s_holeFillBrush = CreateFrozenBrush(200, 200, 200, 50);

    int _buildToken;

    public bool HasGeometry { get; private set; }
    public IReadOnlyList<Point> OuterHull { get; private set; } = [];
    public IReadOnlyList<IReadOnlyList<Point>> Holes { get; private set; } = [];
    public IReadOnlyList<TorsionPreviewTriDraw> Triangles { get; private set; } = [];
    public string MeshQualityText { get; private set; } = "";
    public int NeedRedraw { get; private set; }

    public ICommand FitAllCommand { get; }
    public event Action? FitAllRequested;

    public static Brush OuterFillBrush => s_fillBrush;
    public static Brush HoleFillBrush => s_holeFillBrush;

    public TorsionMeshPreviewVM()
    {
        FitAllCommand = new RelayCommand(_ => FitAllRequested?.Invoke());
    }

    public void Configure(CrossSection? section, double elementSizeM,
        TriangulationMethod triangulation = TriangulationMethod.AdvancingFront)
    {
        if (section == null || section.Areas.Count == 0 || elementSizeM <= 0)
        {
            ClearGeometry();
            return;
        }

        int token = ++_buildToken;
        MeshQualityText = Loc.S("FireSection_MeshComputing");
        OnPropertyChanged(nameof(MeshQualityText));

        var area = section.Areas[0];
        try
        {
            var boundary = area.FromMaterialArea();
            BuildContourMm(boundary, out var outer, out var holes);

            Task.Run(() =>
            {
                try
                {
                    var mesh = MeshBuilder.Build(boundary, elementSizeM, triangulation);
                    var tris = new List<TorsionPreviewTriDraw>(mesh.Triangles.Length);
                    double minAngleGlobal = double.MaxValue;
                    foreach (var el in mesh.Triangles)
                    {
                        if (el.Length < 3) continue;
                        var p0 = new Point(mesh.NodesX[el[0]] * 1000, mesh.NodesY[el[0]] * 1000);
                        var p1 = new Point(mesh.NodesX[el[1]] * 1000, mesh.NodesY[el[1]] * 1000);
                        var p2 = new Point(mesh.NodesX[el[2]] * 1000, mesh.NodesY[el[2]] * 1000);
                        double minAng = MinTriangleAngleDeg(p0, p1, p2);
                        if (minAng < minAngleGlobal) minAngleGlobal = minAng;
                        tris.Add(new TorsionPreviewTriDraw([p0, p1, p2], minAng));
                    }

                    string quality = string.Format(
                        CultureInfo.InvariantCulture,
                        Loc.S("FireSection_MeshQuality"),
                        tris.Count,
                        minAngleGlobal < double.MaxValue ? minAngleGlobal : 0);

                    Application.Current?.Dispatcher.Invoke(() => ApplyMeshResult(
                        token, outer, holes, tris, quality), DispatcherPriority.Background);
                }
                catch (Exception ex)
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        if (token != _buildToken) return;
                        OuterHull = [];
                        Holes = [];
                        Triangles = [];
                        HasGeometry = false;
                        MeshQualityText = string.Format(Loc.S("FireSection_MeshError"), ex.Message);
                        NotifyGeometryChanged();
                    }, DispatcherPriority.Background);
                }
            });
        }
        catch (Exception ex)
        {
            OuterHull = [];
            Holes = [];
            Triangles = [];
            HasGeometry = false;
            MeshQualityText = string.Format(Loc.S("FireSection_MeshError"), ex.Message);
            NotifyGeometryChanged();
        }
    }

    void ApplyMeshResult(
        int token,
        IReadOnlyList<Point> outer,
        IReadOnlyList<IReadOnlyList<Point>> holes,
        List<TorsionPreviewTriDraw> tris,
        string quality)
    {
        if (token != _buildToken) return;
        OuterHull = outer;
        Holes = holes;
        Triangles = tris;
        HasGeometry = outer.Count >= 3;
        MeshQualityText = quality;
        NotifyGeometryChanged();
    }

    void ClearGeometry()
    {
        OuterHull = [];
        Holes = [];
        Triangles = [];
        HasGeometry = false;
        MeshQualityText = "";
        NotifyGeometryChanged();
    }

    void NotifyGeometryChanged()
    {
        OnPropertyChanged(nameof(OuterHull));
        OnPropertyChanged(nameof(Holes));
        OnPropertyChanged(nameof(Triangles));
        OnPropertyChanged(nameof(HasGeometry));
        OnPropertyChanged(nameof(MeshQualityText));
        BumpRedraw();
    }

    void BumpRedraw()
    {
        NeedRedraw++;
        OnPropertyChanged(nameof(NeedRedraw));
    }

    static void BuildContourMm(TorsionBoundary b, out List<Point> outer, out List<IReadOnlyList<Point>> holes)
    {
        outer = new List<Point>(b.OuterX.Length);
        for (int i = 0; i < b.OuterX.Length; i++)
            outer.Add(new Point(b.OuterX[i] * 1000.0, b.OuterY[i] * 1000.0));

        holes = [];
        if (b.Holes == null) return;
        foreach (var (hx, hy) in b.Holes)
        {
            var pts = new List<Point>(hx.Length);
            for (int i = 0; i < hx.Length; i++)
                pts.Add(new Point(hx[i] * 1000.0, hy[i] * 1000.0));
            if (pts.Count >= 3) holes.Add(pts);
        }
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

    public static Color MeshColorForAngle(double minAngleDeg) =>
        FirePreviewVM.MeshColorForAngle(minAngleDeg);

    static Brush CreateFrozenBrush(byte r, byte g, byte b, byte alpha = 255)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, r, g, b));
        brush.Freeze();
        return brush;
    }
}
