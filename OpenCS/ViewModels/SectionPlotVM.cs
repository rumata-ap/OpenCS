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
        double Value,                   // σ [МПа] или ε
        bool IsRebar,
        string Tooltip);

    /// <summary>Данные для области без сетки — контур + зоны сжатия/растяжения.</summary>
    public record NoMeshAreaDrawData(
        IReadOnlyList<Point> Hull,                        // мм, CCW
        IReadOnlyList<IReadOnlyList<Point>> Holes,        // мм, CW
        IReadOnlyList<Point>? CompressionZone,            // обрезанный контур зоны ε<0, мм (null если нет)
        IReadOnlyList<Point>? TensionZone,                // обрезанный контур зоны ε>0, мм (null если нет)
        IReadOnlyList<(Point pt, double val)> HullValues, // значения в вершинах hull
        string Tooltip);

    /// <summary>Данные для отрисовки арматурного стержня.</summary>
    public record RebarDrawData(
        Point Center,
        double RadiusMm,
        double Value,
        string Tooltip);

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

        public SectionPlotVM(CrossSection section, Kurvature k, CalcType calcType, SectionPlotMode mode)
        {
            Mode = mode;

            var concrete   = new List<FiberDrawData>();
            var noMesh     = new List<NoMeshAreaDrawData>();
            var rebar      = new List<RebarDrawData>();

            foreach (var area in section.Areas)
            {
                if (!area.Diagramms.TryGetValue(calcType, out var dgr)) continue;
                bool isRebar = area.Category != AreaCategory.Region;
                bool hasMesh = area.Fibers.Any(f => f.TypeFiber != FiberType.point);

                if (hasMesh)
                {
                    foreach (var f in area.Fibers.Where(f => f.TypeFiber != FiberType.point))
                    {
                        // f.Sig в кПа → делим на 1000 для МПа
                        double val = mode == SectionPlotMode.Stress ? f.Sig / 1000.0 : f.Eps;
                        var pts = ParseWkt(f.WKT);
                        if (pts == null || pts.Count < 3) continue;
                        var centroid = new Point(f.X * 1000, f.Y * 1000);
                        string tip = $"{area.Tag}\nx={f.X*1000:F1} мм  y={f.Y*1000:F1} мм\n" +
                                     (mode == SectionPlotMode.Stress
                                         ? $"σ = {f.Sig / 1000.0:+0.0;-0.0} МПа"
                                         : $"ε = {f.Eps:+0.00000;-0.00000}");
                        concrete.Add(new FiberDrawData(pts, centroid, val, isRebar, tip));
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

                    // Нейтральная ось ε=0: kz*x + ky*y + e0 = 0
                    // Hull координаты в мм
                    var hullMm = area.Hull.X
                        .Zip(area.Hull.Y, (x, y) => (X: x * 1000, Y: y * 1000))
                        .SkipLast(1)                  // убираем дублирующую последнюю точку
                        .ToList();

                    bool anyNeg = hullMm.Any(p => k.e0 + k.ky*(p.Y/1000) + k.kz*(p.X/1000) < -1e-9);
                    bool anyPos = hullMm.Any(p => k.e0 + k.ky*(p.Y/1000) + k.kz*(p.X/1000) >  1e-9);

                    IReadOnlyList<Point>? comprZone = null;
                    IReadOnlyList<Point>? tensZone  = null;

                    if (!anyNeg && !anyPos)
                    {
                        // нулевые деформации — пусто
                    }
                    else if (!anyPos)
                    {
                        comprZone = hullPts;  // вся область в сжатии
                    }
                    else if (!anyNeg)
                    {
                        tensZone = hullPts;   // вся область в растяжении
                    }
                    else
                    {
                        // нейтральная ось пересекает сечение → клиппинг
                        // Точка на нейтральной оси в мм
                        double px_mm = 0, py_mm = 0;
                        if (Math.Abs(k.ky) > 1e-12)
                            py_mm = -(k.e0 * 1000 + k.kz * px_mm) / k.ky;
                        else if (Math.Abs(k.kz) > 1e-12)
                            px_mm = -(k.e0 * 1000 + k.ky * py_mm) / k.kz;

                        // Зона сжатия (ε < 0): нормаль (-kz, -ky)
                        var clipNeg = GridSplit.ClipByHalfPlane(hullMm, px_mm, py_mm, -k.kz, -k.ky);
                        if (clipNeg.Count >= 3)
                            comprZone = clipNeg.Select(p => new Point(p.X, p.Y)).ToList();

                        // Зона растяжения (ε > 0): нормаль (kz, ky)
                        var clipPos = GridSplit.ClipByHalfPlane(hullMm, px_mm, py_mm, k.kz, k.ky);
                        if (clipPos.Count >= 3)
                            tensZone = clipPos.Select(p => new Point(p.X, p.Y)).ToList();
                    }

                    string tip = $"{area.Tag} (без сетки)";
                    noMesh.Add(new NoMeshAreaDrawData(hullPts, holePts, comprZone, tensZone, hullVals, tip));
                }

                // Точечные фибры (арматура) → круги
                foreach (var f in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
                {
                    // f.Sig в кПа → делим на 1000 для МПа
                    double val = mode == SectionPlotMode.Stress ? f.Sig / 1000.0 : f.Eps;
                    string tip = $"{area.Tag} ⌀{f.Diameter*1000:F0} мм\n" +
                                 $"x={f.X*1000:F1}  y={f.Y*1000:F1} мм\n" +
                                 (mode == SectionPlotMode.Stress
                                     ? $"σ = {f.Sig / 1000.0:+0.0;-0.0} МПа"
                                     : $"ε = {f.Eps:+0.00000;-0.00000}");
                    rebar.Add(new RebarDrawData(
                        new Point(f.X * 1000, f.Y * 1000),
                        f.Diameter / 2.0 * 1000,
                        val, tip));
                }
            }

            ConcreteFibers = concrete;
            NoMeshAreas    = noMesh;
            RebarFibers    = rebar;

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
