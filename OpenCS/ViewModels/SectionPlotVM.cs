using CScore;
using OpenCS.Utilites;
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

    /// <summary>Данные для области без сетки (контурный интеграл).</summary>
    public record NoMeshAreaDrawData(
        IReadOnlyList<Point> Hull,
        IReadOnlyList<IReadOnlyList<Point>> Holes,
        Point GradientStart,            // нижняя точка bbox, мм
        Point GradientEnd,              // верхняя точка bbox, мм
        double ValueAtStart,
        double ValueAtEnd,
        bool IsRebar,
        string Tooltip);

    /// <summary>Данные для отрисовки арматурного стержня.</summary>
    public record RebarDrawData(
        Point Center,
        double RadiusMm,
        double Value,
        string Tooltip);

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

        // ── Чекбоксы ─────────────────────────────────────────────────
        bool _showConcrete = true, _showRebar = true,
             _showValues   = false, _showMaxCompr = false;

        public bool ShowConcrete
        {
            get => _showConcrete;
            set { _showConcrete = value; OnPropertyChanged(); OnPropertyChanged(nameof(NeedRedraw)); }
        }
        public bool ShowRebar
        {
            get => _showRebar;
            set { _showRebar = value; OnPropertyChanged(); OnPropertyChanged(nameof(NeedRedraw)); }
        }
        public bool ShowValues
        {
            get => _showValues;
            set { _showValues = value; OnPropertyChanged(); OnPropertyChanged(nameof(NeedRedraw)); }
        }
        public bool ShowMaxCompr
        {
            get => _showMaxCompr;
            set { _showMaxCompr = value; OnPropertyChanged(); OnPropertyChanged(nameof(NeedRedraw)); }
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
                        double val = mode == SectionPlotMode.Stress ? f.Sig : f.Eps;
                        var pts = ParseWkt(f.WKT);
                        if (pts == null || pts.Count < 3) continue;
                        var centroid = new Point(f.X * 1000, f.Y * 1000);
                        string tip = $"{area.Tag}\nx={f.X*1000:F1} мм  y={f.Y*1000:F1} мм\n" +
                                     (mode == SectionPlotMode.Stress
                                         ? $"σ = {f.Sig:+0.0;-0.0} МПа"
                                         : $"ε = {f.Eps:+0.00000;-0.00000}");
                        concrete.Add(new FiberDrawData(pts, centroid, val, isRebar, tip));
                    }
                }
                else if (area.Hull != null && !isRebar)
                {
                    // NoMesh-область
                    var hullPts = ToPointsMm(area.Hull.X, area.Hull.Y);
                    var holePts = area.Holes
                        .Select(h => (IReadOnlyList<Point>)ToPointsMm(h.X, h.Y))
                        .ToList();

                    double yMin = area.Hull.Y.Min(), yMax = area.Hull.Y.Max();
                    double xMid = (area.Hull.X.Min() + area.Hull.X.Max()) / 2.0;
                    double epsStart = k.e0 + k.ky * yMin + k.kz * xMid;
                    double epsEnd   = k.e0 + k.ky * yMax + k.kz * xMid;
                    // SigValue возвращает кПа → делим на 1000 для МПа
                    double valStart = mode == SectionPlotMode.Stress
                        ? dgr.SigValue(epsStart) / 1000.0 : epsStart;
                    double valEnd   = mode == SectionPlotMode.Stress
                        ? dgr.SigValue(epsEnd)   / 1000.0 : epsEnd;

                    string tip = $"{area.Tag} (без сетки)\nГрадиент: {valStart:G4} → {valEnd:G4}";
                    noMesh.Add(new NoMeshAreaDrawData(
                        hullPts, holePts,
                        new Point(xMid * 1000, yMin * 1000),
                        new Point(xMid * 1000, yMax * 1000),
                        valStart, valEnd, false, tip));
                }

                // Точечные фибры (арматура) → круги
                foreach (var f in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
                {
                    double val = mode == SectionPlotMode.Stress ? f.Sig : f.Eps;
                    string tip = $"{area.Tag} ⌀{f.Diameter*1000:F0} мм\n" +
                                 $"x={f.X*1000:F1}  y={f.Y*1000:F1} мм\n" +
                                 (mode == SectionPlotMode.Stress
                                     ? $"σ = {f.Sig:+0.0;-0.0} МПа"
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
                .Concat(noMesh.SelectMany(a => new[] { a.ValueAtStart, a.ValueAtEnd }))
                .ToList();
            ConcreteMin = concreteVals.Count > 0 ? concreteVals.Min() : -1;
            ConcreteMax = concreteVals.Count > 0 ? concreteVals.Max() :  1;

            var rebarVals = rebar.Select(r => r.Value).ToList();
            RebarMin = rebarVals.Count > 0 ? rebarVals.Min() : -1;
            RebarMax = rebarVals.Count > 0 ? rebarVals.Max() :  1;

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
    }
}
