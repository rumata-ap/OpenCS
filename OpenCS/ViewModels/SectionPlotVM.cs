using CScore;
using OpenCS.Utilites;
using OpenCS.Views.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace OpenCS.ViewModels
{
    public enum SectionPlotMode { Stress, Strain }

    /// <summary>Данные для отрисовки одной полигональной фибры.</summary>
    public record FiberDrawData(
        IReadOnlyList<Point> Vertices,  // координаты в мм
        Point Centroid,
        double Value,      // σ [МПа] или ε (для колормапа)
        bool IsRebar,
        string Tooltip,
        double Sigma,      // σ [МПа], всегда
        double Eps,        // деформация, всегда
        double AreaMm2);   // площадь фибры [мм²]

    /// <summary>Данные для области без сетки — контур + значения в вершинах.</summary>
    public record NoMeshAreaDrawData(
        IReadOnlyList<Point> Hull,                        // мм, CCW
        IReadOnlyList<IReadOnlyList<Point>> Holes,        // мм, CW
        IReadOnlyList<(Point pt, double val)> HullValues, // значения в вершинах hull
        string Tooltip);

    /// <summary>Данные для отрисовки арматурного стержня.</summary>
    public record RebarDrawData(
        Point Center,
        double RadiusMm,
        double Value,
        string Tooltip,
        double Sigma,      // σ [МПа], всегда
        double Eps,        // деформация, всегда
        double AreaMm2);   // площадь стержня [мм²]

    /// <summary>Полоса дискретной цветовой шкалы для колорбара.</summary>
    public record ColorBand(System.Windows.Media.Brush Brush, string Label);

    /// <summary>ViewModel вкладок «Напряжения σ» / «Деформации ε».</summary>
    public class SectionPlotVM : ViewModelBase
    {
        public SectionPlotMode Mode { get; }

        public IReadOnlyList<FiberDrawData>       ConcreteFibers { get; }
        public IReadOnlyList<NoMeshAreaDrawData>  NoMeshAreas    { get; }
        public IReadOnlyList<RebarDrawData>       RebarFibers    { get; }

        public double ConcreteMin { get; }
        public double ConcreteMax { get; }
        public double RebarMin    { get; }
        public double RebarMax    { get; }

        public bool HasRebar => RebarFibers.Count > 0;

        /// <summary>Два конца нейтральной линии ε=0 в мм (null если нет).</summary>
        public IReadOnlyList<Point>? NeutralAxis { get; }

        public const int NumBands = 8;
        public IReadOnlyList<ColorBand> ConcreteColorBands { get; }
        public IReadOnlyList<ColorBand> RebarColorBands    { get; }

        // ── Чекбоксы ─────────────────────────────────────────────────
        bool _showConcrete = true, _showRebar = true,
             _showValues   = false, _showMaxCompr = false;

        public bool ShowConcrete
        {
            get => _showConcrete;
            set { _showConcrete = value; OnPropertyChanged(); NeedRedraw++; OnPropertyChanged(nameof(NeedRedraw)); }
        }
        public bool ShowRebar
        {
            get => _showRebar;
            set { _showRebar = value; OnPropertyChanged(); NeedRedraw++; OnPropertyChanged(nameof(NeedRedraw)); }
        }
        public bool ShowValues
        {
            get => _showValues;
            set { _showValues = value; OnPropertyChanged(); NeedRedraw++; OnPropertyChanged(nameof(NeedRedraw)); }
        }
        public bool ShowMaxCompr
        {
            get => _showMaxCompr;
            set { _showMaxCompr = value; OnPropertyChanged(); NeedRedraw++; OnPropertyChanged(nameof(NeedRedraw)); }
        }

        // Триггер перерисовки для FiberCanvas
        public int NeedRedraw { get; private set; }

        public ICommand FitAllCommand { get; }

        // Событие для FiberCanvas: сброс к FitToView
        public event Action? FitAllRequested;

        // ── Стили линий (из CalcSettings) ────────────────────────────────
        public string HullColorHex         { get; }
        public double HullThickness        { get; }
        public string HoleColorHex         { get; }
        public double HoleThickness        { get; }
        public string NeutralAxisColorHex  { get; }
        public double NeutralAxisThickness { get; }
        public string CentroidNdsColorHex  { get; }
        public double CentroidNdsSize      { get; }
        public double FiberLabelFontSize   { get; }

        /// <summary>Центр тяжести по НДС (секущий модуль) в мм; null если не вычислен.</summary>
        public Point? NdsCentroid { get; }

        public SectionPlotVM(CrossSection section, Kurvature k, CalcType calcType, SectionPlotMode mode, CalcSettings? settings = null)
        {
            Mode = mode;

            var cs = settings ?? CalcSettings.Default;
            int gridDensity       = cs.GridDensity;
            HullColorHex          = cs.HullColor;
            HullThickness         = cs.HullThickness;
            HoleColorHex          = cs.HoleColor;
            HoleThickness         = cs.HoleThickness;
            NeutralAxisColorHex   = cs.NeutralAxisColor;
            NeutralAxisThickness  = cs.NeutralAxisThickness;
            CentroidNdsColorHex   = cs.CentroidNdsColor;
            CentroidNdsSize       = cs.CentroidNdsSize;
            FiberLabelFontSize    = cs.FiberLabelFontSize;

            var concrete   = new List<FiberDrawData>();
            var noMesh     = new List<NoMeshAreaDrawData>();
            var rebar      = new List<RebarDrawData>();

            // Накопители для ц.т. по НДС
            double ea_c = 0, esy_c = 0, esz_c = 0;

            foreach (var area in section.Areas)
            {
                if (!area.Diagramms.TryGetValue(calcType, out var dgr)) continue;
                bool isRebar = area.Category != AreaCategory.Region;
                bool hasMesh = area.Fibers.Any(f => f.TypeFiber != FiberType.point);
                double E0 = Math.Abs(dgr.SigValue(1e-7)) / 1e-7 / 1000.0;

                if (hasMesh)
                {
                    foreach (var f in area.Fibers.Where(f => f.TypeFiber != FiberType.point))
                    {
                        // f.Sig в кПа → делим на 1000 для МПа
                        double val = mode == SectionPlotMode.Stress ? f.Sig / 1000.0 : f.Eps;
                        var pts = ParseWkt(f.WKT);
                        if (pts == null || pts.Count < 3) continue;
                        var centroid  = new Point(f.X * 1000, f.Y * 1000);
                        double sigMpa = f.Sig / 1000.0;
                        double aMm2   = f.Area * 1e6;
                        string tip = $"{area.Tag}\nx={f.X*1000:F1} мм  y={f.Y*1000:F1} мм\n" +
                                     $"σ = {sigMpa:+0.0;-0.0} МПа\n" +
                                     $"ε = {f.Eps:+0.00000;-0.00000}\n" +
                                     $"A = {aMm2:F0} мм²";
                        concrete.Add(new FiberDrawData(pts, centroid, val, isRebar, tip, sigMpa, f.Eps, aMm2));
                        // Центр тяжести НДС
                        double esf = Math.Abs(f.Eps) > 1e-9 ? Math.Abs(f.Sig / 1000.0 / f.Eps) : E0;
                        double amm2f = f.Area * 1e6;
                        ea_c  += esf * amm2f;
                        esy_c += esf * amm2f * f.X * 1000;
                        esz_c += esf * amm2f * f.Y * 1000;
                    }
                }
                else if (area.Hull != null && !isRebar)
                {
                    var hullPts = ToPointsMm(area.Hull.X, area.Hull.Y);
                    var holePts = area.Holes
                        .Select(h => (IReadOnlyList<Point>)ToPointsMm(h.X, h.Y))
                        .ToList();

                    // Значения в вершинах hull
                    var hullVals = new List<(Point pt, double val)>();
                    for (int i = 0; i < area.Hull.X.Count; i++)
                    {
                        double ex = area.Hull.X[i], ey = area.Hull.Y[i];
                        double eps = k.e0 + k.ky * ey + k.kz * ex;
                        double v = mode == SectionPlotMode.Stress
                            ? dgr.SigValue(eps) / 1000.0 : eps;
                        hullVals.Add((new Point(ex * 1000, ey * 1000), v));
                    }

                    // Hull и holes в мм (без дублирующей последней точки)
                    var hullMm = area.Hull.X
                        .Zip(area.Hull.Y, (x, y) => (X: x * 1000, Y: y * 1000))
                        .SkipLast(1)
                        .ToList();
                    var holesMm = area.Holes.Select(h =>
                        h.X.Zip(h.Y, (x, y) => (X: x * 1000, Y: y * 1000))
                           .SkipLast(1).ToList()).ToList();

                    // Сетка псевдофибр — клиппинг прямоугольными ячейками
                    double hmXMin = hullMm.Min(p => p.X), hmXMax = hullMm.Max(p => p.X);
                    double hmYMin = hullMm.Min(p => p.Y), hmYMax = hullMm.Max(p => p.Y);
                    double nmStep = Math.Max(hmXMax - hmXMin, hmYMax - hmYMin) / Math.Max(gridDensity, 1);
                    if (nmStep < 1.0) nmStep = 1.0;
                    var nmXs = BuildSteps(hmXMin, hmXMax, nmStep);
                    var nmYs = BuildSteps(hmYMin, hmYMax, nmStep);

                    for (int xi = 0; xi < nmXs.Count - 1; xi++)
                    for (int yi = 0; yi < nmYs.Count - 1; yi++)
                    {
                        var cell = GridSplit.ClipByRect(hullMm,
                            nmXs[xi], nmXs[xi + 1], nmYs[yi], nmYs[yi + 1]);
                        if (cell.Count < 3) continue;
                        double cx_mm = cell.Average(p => p.X);
                        double cy_mm = cell.Average(p => p.Y);
                        if (holesMm.Any(h => PointInPolyMm(cx_mm, cy_mm, h))) continue;
                        double eps_c = k.e0 + k.ky * (cy_mm / 1000) + k.kz * (cx_mm / 1000);
                        double val = mode == SectionPlotMode.Stress
                            ? dgr.SigValue(eps_c) / 1000.0 : eps_c;
                        var cellPts = (IReadOnlyList<Point>)cell
                            .Select(p => new Point(p.X, p.Y)).ToList();
                        double cellSigMpa = dgr.SigValue(eps_c) / 1000.0;
                        double cellAmm2   = PolygonAreaMm2(cell);
                        string cellTip = $"{area.Tag}\nx={cx_mm:F1} мм  y={cy_mm:F1} мм\n" +
                            $"σ = {cellSigMpa:+0.0;-0.0} МПа\n" +
                            $"ε = {eps_c:+0.00000;-0.00000}\n" +
                            $"A = {cellAmm2:F0} мм²";
                        concrete.Add(new FiberDrawData(cellPts,
                            new Point(cx_mm, cy_mm), val, false, cellTip, cellSigMpa, eps_c, cellAmm2));
                        // Центр тяжести НДС для псевдофибры
                        double esf_c = Math.Abs(eps_c) > 1e-9 ? Math.Abs(cellSigMpa / eps_c) : E0;
                        ea_c  += esf_c * cellAmm2;
                        esy_c += esf_c * cellAmm2 * cx_mm;
                        esz_c += esf_c * cellAmm2 * cy_mm;
                    }

                    string tip = $"{area.Tag} (контур)";
                    noMesh.Add(new NoMeshAreaDrawData(hullPts, holePts, hullVals, tip));
                }

                // Точечные фибры (арматура) → круги
                foreach (var f in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
                {
                    // f.Sig в кПа → делим на 1000 для МПа
                    double rSigMpa  = f.Sig / 1000.0;
                    double rAreaMm2 = f.Area * 1e6;
                    double val = mode == SectionPlotMode.Stress ? rSigMpa : f.Eps;
                    string tip = $"{area.Tag} ⌀{f.Diameter*1000:F0} мм\n" +
                                 $"x={f.X*1000:F1}  y={f.Y*1000:F1} мм\n" +
                                 $"σ = {rSigMpa:+0.0;-0.0} МПа\n" +
                                 $"ε = {f.Eps:+0.00000;-0.00000}\n" +
                                 $"A = {rAreaMm2:F0} мм²";
                    rebar.Add(new RebarDrawData(
                        new Point(f.X * 1000, f.Y * 1000),
                        f.Diameter / 2.0 * 1000,
                        val, tip, rSigMpa, f.Eps, rAreaMm2));
                    // Центр тяжести НДС
                    double esf = Math.Abs(f.Eps) > 1e-9 ? Math.Abs(f.Sig / 1000.0 / f.Eps) : E0;
                    double amm2f = f.Area * 1e6;
                    ea_c  += esf * amm2f;
                    esy_c += esf * amm2f * f.X * 1000;
                    esz_c += esf * amm2f * f.Y * 1000;
                }
            }

            ConcreteFibers = concrete;
            NoMeshAreas    = noMesh;
            RebarFibers    = rebar;

            NdsCentroid = ea_c > 1e-6 ? new Point(esy_c / ea_c, esz_c / ea_c) : (Point?)null;

            // Диапазоны нормировки
            var concreteVals = concrete.Select(f => f.Value)
                .Concat(noMesh.SelectMany(a => a.HullValues.Select(hv => hv.val)))
                .ToList();
            ConcreteMin = concreteVals.Count > 0 ? concreteVals.Min() : -1;
            ConcreteMax = concreteVals.Count > 0 ? concreteVals.Max() :  1;

            var rebarVals = rebar.Select(r => r.Value).ToList();
            RebarMin = rebarVals.Count > 0 ? rebarVals.Min() : -1;
            RebarMax = rebarVals.Count > 0 ? rebarVals.Max() :  1;

            ConcreteColorBands = BuildColorBands(ConcreteMin, ConcreteMax, false);
            RebarColorBands    = BuildColorBands(RebarMin,    RebarMax,    true);

            // Нейтральная линия деформаций: найти пересечение ε=0 с bbox сечения
            var allPts = concrete.SelectMany(f => f.Vertices)
                .Concat(noMesh.SelectMany(a => a.Hull)).ToList();
            if (allPts.Count >= 2)
            {
                double bxMin = allPts.Min(p => p.X), bxMax = allPts.Max(p => p.X);
                double byMin = allPts.Min(p => p.Y), byMax = allPts.Max(p => p.Y);
                NeutralAxis = ComputeNeutralAxisSegment(
                    k.kz, k.ky, k.e0 * 1000, bxMin, bxMax, byMin, byMax);
            }

            FitAllCommand = new RelayCommand(_ => FitAllRequested?.Invoke());
        }

        static IReadOnlyList<Point>? ParseWkt(string? wkt)
        {
            if (string.IsNullOrEmpty(wkt)) return null;
            WktHelper.ParseWKTPolygon(wkt, out var xs, out var ys, out _, out _);
            if (xs == null || xs.Count < 3) return null;
            var pts = new List<Point>(xs.Count);
            for (int i = 0; i < xs.Count; i++)
                pts.Add(new Point(xs[i] * 1000, ys[i] * 1000)); // м → мм
            return pts;
        }

        static IReadOnlyList<Point> ToPointsMm(IList<double> xs, IList<double> ys)
        {
            var pts = new List<Point>(xs.Count);
            for (int i = 0; i < xs.Count; i++)
                pts.Add(new Point(xs[i] * 1000, ys[i] * 1000));
            return pts;
        }

        static List<double> BuildSteps(double lo, double hi, double step)
        {
            var result = new List<double> { lo };
            int iLo = (int)Math.Ceiling(lo / step);
            int iHi = (int)Math.Floor(hi / step);
            for (int i = iLo; i <= iHi; i++)
            {
                double v = i * step;
                if (v > lo + step * 0.01 && v < hi - step * 0.01)
                    result.Add(v);
            }
            result.Add(hi);
            return result;
        }

        static double PolygonAreaMm2(List<(double X, double Y)> verts)
        {
            double area = 0;
            int n = verts.Count;
            for (int i = 0; i < n; i++)
            {
                var a = verts[i]; var b = verts[(i + 1) % n];
                area += (a.X * b.Y - b.X * a.Y);
            }
            return Math.Abs(area) * 0.5;
        }

        static bool PointInPolyMm(double px, double py, List<(double X, double Y)> verts)
        {
            int n = verts.Count; bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = verts[i].X, yi = verts[i].Y;
                double xj = verts[j].X, yj = verts[j].Y;
                if (((yi > py) != (yj > py)) &&
                    (px < (xj - xi) * (py - yi) / (yj - yi) + xi))
                    inside = !inside;
            }
            return inside;
        }

        static IReadOnlyList<Point>? ComputeNeutralAxisSegment(
            double kz, double ky, double e0mm,
            double xMin, double xMax, double yMin, double yMax)
        {
            var pts = new List<Point>();
            void TryAdd(double x, double y)
            {
                if (x < xMin - 1e-3 || x > xMax + 1e-3 ||
                    y < yMin - 1e-3 || y > yMax + 1e-3) return;
                foreach (var q in pts)
                    if (Math.Abs(x - q.X) < 1e-3 && Math.Abs(y - q.Y) < 1e-3) return;
                pts.Add(new Point(Math.Clamp(x, xMin, xMax), Math.Clamp(y, yMin, yMax)));
            }
            if (Math.Abs(ky) > 1e-12)
            {
                TryAdd(xMin, -(e0mm + kz * xMin) / ky);
                TryAdd(xMax, -(e0mm + kz * xMax) / ky);
            }
            if (Math.Abs(kz) > 1e-12)
            {
                TryAdd(-(e0mm + ky * yMin) / kz, yMin);
                TryAdd(-(e0mm + ky * yMax) / kz, yMax);
            }
            return pts.Count >= 2 ? pts.GetRange(0, 2) : null;
        }

        static IReadOnlyList<ColorBand> BuildColorBands(double min, double max, bool isRebar)
        {
            var bands = new List<ColorBand>(NumBands);
            for (int i = NumBands - 1; i >= 0; i--)  // top (max) to bottom (min)
            {
                double t = (i + 0.5) / NumBands;
                double val = min + t * (max - min);
                var color = isRebar ? ColormapHelper.RebarColor(t) : ColormapHelper.MainColor(t);
                var brush = new System.Windows.Media.SolidColorBrush(color);
                brush.Freeze();
                string label = $"{val:G4}";
                bands.Add(new ColorBand(brush, label));
            }
            return bands;
        }
    }
}
