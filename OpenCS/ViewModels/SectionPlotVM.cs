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
        double AreaMm2)    // площадь фибры [мм²]
    {
        /// <summary>Кольца отверстий в координатах мм (пусто для большинства фибр).</summary>
        public IReadOnlyList<IReadOnlyList<Point>> Holes { get; init; } = [];
        /// <summary>Вершина с минимальным значением σ/ε для построения градиентной кисти.</summary>
        public (Point pt, double val) GradientMin { get; init; }
        /// <summary>Вершина с максимальным значением σ/ε для построения градиентной кисти.</summary>
        public (Point pt, double val) GradientMax { get; init; }
        /// <summary>Значения σ [МПа] или ε в каждой вершине Vertices (null для псевдофибр без повершинных значений).</summary>
        public IReadOnlyList<double>? VertexValues { get; init; }
    }

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

        /// <summary>Точка максимального сжатия: координаты мм, ε и σ [МПа] (вершина с мин. ε, независимо от режима).</summary>
        public (Point Pt, double Eps, double SigMpa)? MaxComprData { get; }

        public const int NumBands = 8;
        public IReadOnlyList<ColorBand> ConcreteColorBands { get; }
        public IReadOnlyList<ColorBand> RebarColorBands    { get; }

        // ── Чекбоксы ─────────────────────────────────────────────────
        bool _showConcrete = true, _showRebar = true,
             _showValues   = false, _showMaxCompr = false,
             _smoothColormap, _showFiberGrid;

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
        public bool SmoothColormap
        {
            get => _smoothColormap;
            set { _smoothColormap = value; OnPropertyChanged(); NeedRedraw++; OnPropertyChanged(nameof(NeedRedraw)); }
        }
        public bool ShowFiberGrid
        {
            get => _showFiberGrid;
            set { _showFiberGrid = value; OnPropertyChanged(); NeedRedraw++; OnPropertyChanged(nameof(NeedRedraw)); }
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

        SectionCutVM? _cutVM;
        /// <summary>Общий инструмент разреза — один экземпляр на результат, назначается и вкладке «Напряжения», и вкладке «Деформации» (см. CalcResultView/LimitForceResultView).</summary>
        public SectionCutVM? CutVM
        {
            get => _cutVM;
            set { _cutVM = value; OnPropertyChanged(); }
        }

        public SectionPlotVM(CrossSection section, Kurvature k, CalcType calcType, SectionPlotMode mode, CalcSettings? settings = null, bool ten = true)
        {
            Mode = mode;

            var cs = settings ?? CalcSettings.Default;
            _smoothColormap       = cs.SmoothColormap;
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
            bool anyMesh = false;
            // Аккумуляторы точки максимального сжатия
            Point? mcPt = null; double mcEps = double.MaxValue, mcSig = 0;

            foreach (var (area, ka) in section.EnumerateAreas(k))
            {
                if (!area.Diagramms.TryGetValue(calcType, out var dgr)) continue;
                bool isRebar = area.Category != AreaCategory.Region;
                bool hasMesh = area.Fibers.Any(f => f.TypeFiber != FiberType.point);
                double E0 = Math.Abs(dgr.SigValue(1e-7)) / 1e-7 / 1000.0;

                if (hasMesh)
                {
                    anyMesh = true;
                    foreach (var f in area.Fibers.Where(f => f.TypeFiber != FiberType.point))
                    {
                        // f.Sig в кПа → делим на 1000 для МПа
                        double val = mode == SectionPlotMode.Stress ? f.Sig / 1000.0 : f.Eps;
                        var (pts, fiberHoles) = ParseWktFull(f.WKT);
                        if (pts == null || pts.Count < 3) continue;
                        var centroid  = new Point(f.X * 1000, f.Y * 1000);
                        double sigMpa = f.Sig / 1000.0;
                        double aMm2   = f.Area * 1e6;
                        string tip = $"{area.Tag}\nx={f.X*1000:F1} мм  y={f.Y*1000:F1} мм\n" +
                                     $"σ = {sigMpa:+0.0;-0.0} МПа\n" +
                                     $"ε = {f.Eps:+0.00000;-0.00000}\n" +
                                     $"A = {aMm2:F0} мм²";
                        // Концы градиентной кисти: вершины с min/max значением σ или ε
                        var gMin = (pt: pts[0], val: double.MaxValue);
                        var gMax = (pt: pts[0], val: double.MinValue);
                        var vertVals = new double[pts.Count];
                        for (int vi = 0; vi < pts.Count; vi++)
                        {
                            var v = pts[vi];
                            double eps_v = ka.e0 + ka.ky * (v.Y / 1000.0) + ka.kz * (v.X / 1000.0);
                            double val_v = mode == SectionPlotMode.Stress
                                ? dgr.SigValue(eps_v, ten) / 1000.0 : eps_v;
                            vertVals[vi] = val_v;
                            if (val_v < gMin.val) gMin = (v, val_v);
                            if (val_v > gMax.val) gMax = (v, val_v);
                        }
                        concrete.Add(new FiberDrawData(pts, centroid, val, isRebar, tip, sigMpa, f.Eps, aMm2)
                            { Holes = fiberHoles, GradientMin = gMin, GradientMax = gMax, VertexValues = vertVals });
                        // Центр тяжести НДС
                        double esf = Math.Abs(f.Eps) > 1e-9 ? Math.Abs(f.Sig / 1000.0 / f.Eps) : E0;
                        double amm2f = f.Area * 1e6;
                        ea_c  += esf * amm2f;
                        esy_c += esf * amm2f * f.X * 1000;
                        esz_c += esf * amm2f * f.Y * 1000;
                    }
                    // Контуры hull/holes для сеточных областей — рисуются поверх фибр
                    if (area.Hull != null && !isRebar)
                    {
                        var hullPts = ToPointsMm(area.Hull.X, area.Hull.Y);
                        var holePts = area.Holes
                            .Select(h => (IReadOnlyList<Point>)ToPointsMm(h.X, h.Y))
                            .ToList();
                        noMesh.Add(new NoMeshAreaDrawData(hullPts, holePts, [], $"{area.Tag} (контур)"));
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
                        double eps = ka.e0 + ka.ky * ey + ka.kz * ex;
                        double v = mode == SectionPlotMode.Stress
                            ? dgr.SigValue(eps, ten) / 1000.0 : eps;
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
                        double eps_c = ka.e0 + ka.ky * (cy_mm / 1000) + ka.kz * (cx_mm / 1000);
                        double val = mode == SectionPlotMode.Stress
                            ? dgr.SigValue(eps_c, ten) / 1000.0 : eps_c;
                        var cellPts = (IReadOnlyList<Point>)cell
                            .Select(p => new Point(p.X, p.Y)).ToList();
                        var cellVals = new double[cellPts.Count];
                        for (int vi = 0; vi < cellPts.Count; vi++)
                        {
                            var cv = cellPts[vi];
                            double eps_v = ka.e0 + ka.ky * (cv.Y / 1000) + ka.kz * (cv.X / 1000);
                            cellVals[vi] = mode == SectionPlotMode.Stress
                                ? dgr.SigValue(eps_v, ten) / 1000.0 : eps_v;
                        }
                        double cellSigMpa = dgr.SigValue(eps_c, ten) / 1000.0;
                        double cellAmm2   = PolygonAreaMm2(cell);
                        string cellTip = $"{area.Tag}\nx={cx_mm:F1} мм  y={cy_mm:F1} мм\n" +
                            $"σ = {cellSigMpa:+0.0;-0.0} МПа\n" +
                            $"ε = {eps_c:+0.00000;-0.00000}\n" +
                            $"A = {cellAmm2:F0} мм²";
                        concrete.Add(new FiberDrawData(cellPts,
                            new Point(cx_mm, cy_mm), val, false, cellTip, cellSigMpa, eps_c, cellAmm2)
                            { VertexValues = cellVals });
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

                // Поиск точки максимального сжатия по вершинам hull текущей области
                if (area.Hull != null && !isRebar)
                    for (int i = 0; i < area.Hull.X.Count; i++)
                    {
                        double vx = area.Hull.X[i], vy = area.Hull.Y[i];
                        double eps = ka.e0 + ka.ky * vy + ka.kz * vx;
                        if (eps < mcEps)
                        {
                            mcEps = eps;
                            mcPt  = new Point(vx * 1000, vy * 1000);
                            mcSig = dgr.SigValue(eps) / 1000.0;
                        }
                    }
            }

            ConcreteFibers  = concrete;
            NoMeshAreas     = noMesh;
            RebarFibers     = rebar;
            _showFiberGrid  = anyMesh;
            // Запасной вариант: hull не найден — берём центроид наиболее сжатой фибры
            if (mcPt == null)
                foreach (var f in concrete)
                    if (f.Eps < mcEps) { mcEps = f.Eps; mcPt = f.Centroid; mcSig = f.Sigma; }
            MaxComprData = mcPt.HasValue ? (mcPt.Value, mcEps, mcSig) : null;

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

        static (IReadOnlyList<Point>? outer, IReadOnlyList<IReadOnlyList<Point>> holes) ParseWktFull(string? wkt)
        {
            if (string.IsNullOrEmpty(wkt)) return (null, []);
            WktHelper.ParseWKTPolygon(wkt, out var xs, out var ys, out var holeXs, out var holeYs);
            if (xs == null || xs.Count < 3) return (null, []);
            var pts = new List<Point>(xs.Count);
            for (int i = 0; i < xs.Count; i++)
                pts.Add(new Point(xs[i] * 1000, ys[i] * 1000));
            var holes = new List<IReadOnlyList<Point>>(holeXs.Count);
            for (int h = 0; h < holeXs.Count; h++)
            {
                var hx = holeXs[h]; var hy = holeYs[h];
                if (hx.Count < 3) continue;
                var hpts = new List<Point>(hx.Count);
                for (int i = 0; i < hx.Count; i++)
                    hpts.Add(new Point(hx[i] * 1000, hy[i] * 1000));
                holes.Add(hpts);
            }
            return (pts, holes);
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
                var color = ColormapHelper.GetColor(val, min, max, isRebar);
                var brush = new System.Windows.Media.SolidColorBrush(color);
                brush.Freeze();
                string label = $"{val:G4}";
                bands.Add(new ColorBand(brush, label));
            }
            return bands;
        }
    }
}
