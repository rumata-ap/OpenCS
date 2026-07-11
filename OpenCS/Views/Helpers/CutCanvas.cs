using CScore;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenCS.Views.Helpers
{
    /// <summary>
    /// Панель эпюры σ/ε вдоль разреза сечения — учебниковый вид с осями, сеткой, легендой и зумом.
    /// </summary>
    public class CutCanvas : FrameworkElement
    {
        public static readonly DependencyProperty CutViewModelProperty =
            DependencyProperty.Register(nameof(CutViewModel), typeof(SectionCutVM),
                typeof(CutCanvas), new FrameworkPropertyMetadata(null, OnCutViewModelChanged));

        public SectionCutVM? CutViewModel
        {
            get => (SectionCutVM?)GetValue(CutViewModelProperty);
            set => SetValue(CutViewModelProperty, value);
        }

        public static readonly DependencyProperty PlotModeProperty =
            DependencyProperty.Register(nameof(PlotMode), typeof(SectionPlotMode),
                typeof(CutCanvas), new FrameworkPropertyMetadata(SectionPlotMode.Stress,
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public SectionPlotMode PlotMode
        {
            get => (SectionPlotMode)GetValue(PlotModeProperty);
            set => SetValue(PlotModeProperty, value);
        }

        public static readonly DependencyProperty ShowChromeProperty =
            DependencyProperty.Register(nameof(ShowChrome), typeof(bool),
                typeof(CutCanvas), new FrameworkPropertyMetadata(true,
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public bool ShowChrome
        {
            get => (bool)GetValue(ShowChromeProperty);
            set => SetValue(ShowChromeProperty, value);
        }

        /// <summary>Упрощённый режим экспорта: без заливки/штриховки/арматуры (подписи кривой остаются).</summary>
        public bool ExportPlainMode { get; set; }

        const double MarginLeft = 54;
        const double MarginRight = 16;
        const double MarginTop = 16;
        const double MarginBottom = 46;
        const double SnapRadiusPx = 12.0;
        const double EndTickLen = 8.0;
        const double MaxRebarLinePx = 18.0;
        const double RebarLineThickness = 2.8;
        const double RebarHandleRadius = 5.5;
        const double RebarHandleHitRadius = 10.0;

        double _baseScaleS = 1.0, _baseScaleV = 1.0;
        double _plotOx, _plotOy, _plotW, _plotH;
        double _panX, _panY;
        double _lengthMm, _vAbsMax, _rebarVAbsMax;
        bool _fitted;
        bool? _lastHorizontal;

        Point _dragStart;
        bool _panning;
        SectionCutVM.RebarLineKey? _dragRebarKey;

        static readonly Pen _basePen = MakePen(Brushes.Black, 1.5);
        static readonly Pen _holeBasePen = MakeDashedPen(Color.FromRgb(0xB0, 0xB0, 0xB0), 1.2);
        static readonly Pen _boundaryPen = MakePen(Brushes.Black, 1.0);
        static readonly Pen _epsCuPen = MakeDashedPen(Colors.Purple, 1.2);
        static readonly Pen _nearbyPen = MakeDashedPen(Color.FromRgb(0x88, 0x88, 0x88), 1.0);
        static readonly Pen _gridPen = MakePen(new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)), 0.8);
        static readonly Pen _axisPen = MakePen(Brushes.Black, 1.0);
        static readonly Pen _zeroPen = MakePen(new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)), 1.0);
        static readonly Pen _endTickPen = MakePen(Brushes.Black, 1.0);
        static readonly Pen _hoverPen = MakeDashedPen(Color.FromRgb(0x66, 0x66, 0x66), 0.8);

        bool _hoverVisible;
        double _hoverSmm, _hoverV;

        public CutCanvas()
        {
            ClipToBounds = true;
            Focusable = true;
            BuildContextMenu();
            SizeChanged += (_, _) =>
            {
                if (CutViewModel?.Result == null) return;
                FitToView();
                InvalidateVisual();
            };
        }

        void BuildContextMenu()
        {
            var menu = new ContextMenu();
            var fitItem = new MenuItem();
            fitItem.SetResourceReference(HeaderedItemsControl.HeaderProperty, "PlotFitAll");
            fitItem.Click += (_, _) => CutViewModel?.FitCommand.Execute(null);

            var exportItem = new MenuItem();
            exportItem.SetResourceReference(HeaderedItemsControl.HeaderProperty, "SectionCutExport");
            exportItem.Click += (_, _) => CutViewModel?.ExportCommand.Execute(null);

            menu.Items.Add(fitItem);
            menu.Items.Add(exportItem);
            ContextMenu = menu;
        }

        static void OnCutViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var cc = (CutCanvas)d;
            if (e.OldValue is SectionCutVM old)
            {
                old.Changed -= cc.OnCutChanged;
                old.FitRequested -= cc.OnFitRequested;
            }
            if (e.NewValue is SectionCutVM vm)
            {
                vm.Changed += cc.OnCutChanged;
                vm.FitRequested += cc.OnFitRequested;
            }
            cc._fitted = false;
            cc.InvalidateVisual();
        }

        void OnCutChanged()
        {
            if (CutViewModel?.Result != null)
                FitToView();
            InvalidateVisual();
        }

        void OnFitRequested()
        {
            _panX = _panY = 0;
            _fitted = false;
            FitToView();
            InvalidateVisual();
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (!_fitted && CutViewModel?.Result != null)
            {
                _fitted = true;
                FitToView();
            }
            return finalSize;
        }

        void FitToView()
        {
            var result = CutViewModel?.Result;
            _plotOx = MarginLeft;
            _plotOy = MarginTop;
            _plotW = Math.Max(ActualWidth - MarginLeft - MarginRight, 10);
            _plotH = Math.Max(ActualHeight - MarginTop - MarginBottom, 10);

            if (result == null || ActualWidth < 1 || ActualHeight < 1)
            {
                _baseScaleS = _baseScaleV = 1;
                return;
            }

            bool horizontal = CutViewModel?.IsHorizontal ?? true;
            if (_lastHorizontal is bool prev && prev != horizontal)
                _panX = _panY = 0;
            _lastHorizontal = horizontal;

            _lengthMm = Distance(result.Start, result.End) * 1000.0;
            if (_lengthMm < 1e-6) _lengthMm = 1;

            double vMin = 0, vMax = 0;
            CollectValueRange(result, ref vMin, ref vMax);
            _vAbsMax = Math.Max(Math.Abs(vMin), Math.Abs(vMax));
            if (_vAbsMax < 1e-12) _vAbsMax = 1;

            double padFrac = 0.1;
            if (horizontal)
            {
                _baseScaleS = _plotW / (_lengthMm * (1 + 2 * padFrac));
                _baseScaleV = _plotH / 2 / _vAbsMax / (1 + padFrac);
            }
            else
            {
                _baseScaleS = _plotH / (_lengthMm * (1 + 2 * padFrac));
                _baseScaleV = _plotW / 2 / _vAbsMax / (1 + padFrac);
            }
            _fitted = true;
        }

        void CollectValueRange(SectionCutResult result, ref double vMin, ref double vMax)
        {
            foreach (var seg in result.Segments)
                foreach (var p in seg.Points)
                {
                    double? v = PlotMode == SectionPlotMode.Stress ? p.Sig : p.Eps;
                    if (v == null) continue;
                    if (v < vMin) vMin = v.Value;
                    if (v > vMax) vMax = v.Value;
                }
            foreach (var r in result.Rebars)
            {
                double v = PlotMode == SectionPlotMode.Stress ? r.Sig : r.Eps;
                if (v < vMin) vMin = v;
                if (v > vMax) vMax = v;
            }
            if (PlotMode == SectionPlotMode.Strain && CutViewModel?.EpsCu is { } epsCu)
            {
                if (epsCu < vMin) vMin = epsCu;
                if (epsCu > vMax) vMax = epsCu;
            }
        }

        double ScaleS => _baseScaleS * (CutViewModel?.ScaleS ?? 1.0);
        double ScaleV => _baseScaleV * (CutViewModel?.ScaleV ?? 1.0);

        double BaseScreenS(double sMm)
        {
            bool horizontal = CutViewModel?.IsHorizontal ?? true;
            return horizontal ? _plotOx + _lengthMm * 0.1 * ScaleS + sMm * ScaleS : _plotOy + _lengthMm * 0.1 * ScaleS + sMm * ScaleS;
        }

        double BaseScreenV0()
        {
            bool horizontal = CutViewModel?.IsHorizontal ?? true;
            return horizontal ? _plotOy + _plotH / 2 : _plotOx + _plotW / 2;
        }

        Point ToScreen(double sMm, double value)
        {
            bool horizontal = CutViewModel?.IsHorizontal ?? true;
            if (horizontal)
                return new Point(BaseScreenS(sMm) + _panX, BaseScreenV0() + _panY - value * ScaleV);
            return new Point(BaseScreenV0() + _panX + value * ScaleV, BaseScreenS(sMm) + _panY);
        }

        void UpdateRebarValueRange(SectionCutResult result)
        {
            _rebarVAbsMax = 0;
            foreach (var r in result.Rebars)
            {
                double v = PlotMode == SectionPlotMode.Stress ? r.Sig : r.Eps;
                double av = Math.Abs(v);
                if (av > _rebarVAbsMax) _rebarVAbsMax = av;
            }
            if (_rebarVAbsMax < 1e-12) _rebarVAbsMax = 1;
        }

        /// <summary>Длина линии усилия арматуры в пикселях — не зависит от ScaleV эпюры бетона.</summary>
        double ResolveRebarLengthPx(SectionCutVM vm, CutRebarMarker r, double computedV)
        {
            var key = SectionCutVM.RebarKey(r);
            if (vm.TryGetRebarLengthPxOverride(key, out double px)) return px;
            if (_rebarVAbsMax < 1e-12) return 0;
            return Math.Min(MaxRebarLinePx, Math.Abs(computedV) / _rebarVAbsMax * MaxRebarLinePx);
        }

        static Point RebarEndScreen(Point basePt, double computedV, double lengthPx, bool horizontal)
        {
            if (lengthPx < 1e-6 || Math.Abs(computedV) < 1e-12) return basePt;
            int sign = computedV > 0 ? 1 : -1;
            return horizontal
                ? new Point(basePt.X, basePt.Y - sign * lengthPx)
                : new Point(basePt.X + sign * lengthPx, basePt.Y);
        }

        double RebarLengthPxFromScreen(Point basePt, Point screen, bool horizontal) =>
            horizontal ? Math.Abs(basePt.Y - screen.Y) : Math.Abs(basePt.X - screen.X);

        static Pen MakeRebarForcePen(Color color)
        {
            var b = new SolidColorBrush(color); b.Freeze();
            var pen = new Pen(b, RebarLineThickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            pen.Freeze();
            return pen;
        }

        static double Distance((double X, double Y) a, (double X, double Y) b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(SystemColors.WindowBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

            var vm = CutViewModel;
            var result = vm?.Result;
            if (vm == null)
            {
                DrawHint(dc, Loc.S("SectionCutHintNoTool"));
                return;
            }
            if (result == null)
            {
                DrawHint(dc, vm.Mode == CutMode.GradientSnap
                    ? Loc.S("SectionCutHintClickOnePoint")
                    : Loc.S("SectionCutHintClickTwoPoints"));
                return;
            }

            if (!_fitted) FitToView();

            UpdateRebarValueRange(result);

            if (ShowChrome)
                DrawGridAndAxes(dc);

            foreach (var seg in result.Segments)
            {
                if (seg.Points.Count < 2) continue;
                double s0 = seg.Points[0].S * 1000.0, s1 = seg.Points[^1].S * 1000.0;
                var pen = seg.AreaIndex == null ? _holeBasePen : _basePen;
                dc.DrawLine(pen, ToScreen(s0, 0), ToScreen(s1, 0));
            }

            if (ShowChrome)
                DrawEndMarkers(dc, result);

            foreach (var seg in result.Segments)
            {
                if (seg.AreaIndex == null || seg.Points.Count < 2) continue;
                DrawSegment(dc, seg, vm);
            }

            if (PlotMode == SectionPlotMode.Strain && vm.EpsCu is { } epsCu)
                dc.DrawLine(_epsCuPen, ToScreen(0, epsCu), ToScreen(_lengthMm, epsCu));

            if (!ExportPlainMode)
            {
                foreach (var r in result.Rebars)
                    DrawRebarOnCurve(dc, r, vm, onCurve: true);

                foreach (var r in result.NearbyRebars)
                    DrawRebarOnCurve(dc, r, vm, onCurve: false);
            }

            if (_hoverVisible && !ExportPlainMode)
                DrawHover(dc, vm);
        }

        const double TargetGridSpacingPx = 50.0;

        double SOriginMm() => _lengthMm * 0.1;

        double ScreenToS(double screenCoord, bool horizontal)
        {
            if (horizontal)
                return (screenCoord - _panX - _plotOx) / ScaleS - SOriginMm();
            return (screenCoord - _panY - _plotOy) / ScaleS - SOriginMm();
        }

        double ScreenToV(double screenCoord, bool horizontal)
        {
            if (horizontal)
                return (BaseScreenV0() + _panY - screenCoord) / ScaleV;
            return (screenCoord - BaseScreenV0() - _panX) / ScaleV;
        }

        static double NiceStepValue(double rawStep)
        {
            if (rawStep <= 1e-12) return 1;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
            double norm = rawStep / mag;
            double nice = norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 5 ? 5 : 10;
            return nice * mag;
        }

        double GridStepS() => NiceStepValue(TargetGridSpacingPx / Math.Max(ScaleS, 1e-9));
        double GridStepV() => NiceStepValue(TargetGridSpacingPx / Math.Max(ScaleV, 1e-9));

        static double FloorToStep(double value, double step) =>
            Math.Floor(value / step - 1e-9) * step;

        static double CeilToStep(double value, double step) =>
            Math.Ceiling(value / step - 1e-9) * step;

        void DrawGridAndAxes(DrawingContext dc)
        {
            bool horizontal = CutViewModel?.IsHorizontal ?? true;
            var plotRect = new Rect(_plotOx, _plotOy, _plotW, _plotH);
            dc.DrawRectangle(null, _axisPen, plotRect);

            double sStep = GridStepS();
            double vStep = GridStepV();

            if (horizontal)
            {
                double sMin = ScreenToS(_plotOx, true);
                double sMax = ScreenToS(_plotOx + _plotW, true);
                double vMax = ScreenToV(_plotOy, true);
                double vMin = ScreenToV(_plotOy + _plotH, true);

                for (double s = FloorToStep(sMin, sStep); s <= sMax + sStep * 0.5; s += sStep)
                {
                    var p = ToScreen(s, 0);
                    if (p.X < _plotOx - 1 || p.X > _plotOx + _plotW + 1) continue;
                    dc.DrawLine(_gridPen, new Point(p.X, _plotOy), new Point(p.X, _plotOy + _plotH));
                    DrawTickLabel(dc, FormatS(s, sStep), new Point(p.X, _plotOy + _plotH + 4), horizontal: true);
                }
                for (double v = FloorToStep(vMin, vStep); v <= vMax + vStep * 0.5; v += vStep)
                {
                    var p = ToScreen(0, v);
                    if (p.Y < _plotOy - 1 || p.Y > _plotOy + _plotH + 1) continue;
                    dc.DrawLine(Math.Abs(v) < vStep * 0.01 ? _zeroPen : _gridPen,
                        new Point(_plotOx, p.Y), new Point(_plotOx + _plotW, p.Y));
                    DrawTickLabel(dc, FormatValueForStep(v, vStep), new Point(_plotOx - 4, p.Y), horizontal: false);
                }
                DrawAxisTitle(dc, Loc.S("SectionCutAxisS"), new Point(_plotOx + _plotW / 2, ActualHeight - 8), center: true);
                DrawAxisTitle(dc, PlotMode == SectionPlotMode.Stress
                    ? Loc.S("SectionCutAxisSigma") : Loc.S("SectionCutAxisEps"),
                    new Point(10, _plotOy + _plotH / 2), center: false, vertical: true);
            }
            else
            {
                double sMin = ScreenToS(_plotOy, false);
                double sMax = ScreenToS(_plotOy + _plotH, false);
                double vMin = ScreenToV(_plotOx, false);
                double vMax = ScreenToV(_plotOx + _plotW, false);

                for (double s = FloorToStep(sMin, sStep); s <= sMax + sStep * 0.5; s += sStep)
                {
                    var p = ToScreen(s, 0);
                    if (p.Y < _plotOy - 1 || p.Y > _plotOy + _plotH + 1) continue;
                    dc.DrawLine(_gridPen, new Point(_plotOx, p.Y), new Point(_plotOx + _plotW, p.Y));
                    DrawTickLabel(dc, FormatS(s, sStep), new Point(_plotOx + _plotW + 4, p.Y), horizontal: true);
                }
                for (double v = FloorToStep(vMin, vStep); v <= vMax + vStep * 0.5; v += vStep)
                {
                    var p = ToScreen(0, v);
                    if (p.X < _plotOx - 1 || p.X > _plotOx + _plotW + 1) continue;
                    dc.DrawLine(Math.Abs(v) < vStep * 0.01 ? _zeroPen : _gridPen,
                        new Point(p.X, _plotOy), new Point(p.X, _plotOy + _plotH));
                    DrawTickLabel(dc, FormatValueForStep(v, vStep), new Point(p.X, _plotOy - 4), horizontal: true);
                }
                DrawAxisTitle(dc, Loc.S("SectionCutAxisS"), new Point(ActualWidth - 8, _plotOy + _plotH / 2), center: true, vertical: true);
                DrawAxisTitle(dc, PlotMode == SectionPlotMode.Stress
                    ? Loc.S("SectionCutAxisSigma") : Loc.S("SectionCutAxisEps"),
                    new Point(_plotOx + _plotW / 2, 10), center: true);
            }
        }

        string FormatS(double s, double step) =>
            step < 1 ? $"{s:F1}" : $"{s:F0}";

        string FormatValueForStep(double v, double step)
        {
            if (PlotMode == SectionPlotMode.Stress)
            {
                if (step < 0.05) return $"{v:+0.000;-0.000}";
                if (step < 0.5) return $"{v:+0.00;-0.00}";
                if (step < 5) return $"{v:+0.##;-0.##}";
                return $"{v:+0.#;-0.#}";
            }
            if (step < 0.00001) return $"{v:+0.000000;-0.000000}";
            if (step < 0.0001) return $"{v:+0.00000;-0.00000}";
            if (step < 0.001) return $"{v:+0.0000;-0.0000}";
            return $"{v:+0.###;-0.###}";
        }

        string FormatValue(double v) => FormatValueForStep(v, GridStepV());

        void DrawEndMarkers(DrawingContext dc, SectionCutResult result)
        {
            bool horizontal = CutViewModel?.IsHorizontal ?? true;
            double s0 = 0, s1 = _lengthMm;
            var p0 = ToScreen(s0, 0);
            var p1 = ToScreen(s1, 0);

            if (horizontal)
            {
                dc.DrawLine(_endTickPen, new Point(p0.X, p0.Y - EndTickLen / 2), new Point(p0.X, p0.Y + EndTickLen / 2));
                dc.DrawLine(_endTickPen, new Point(p1.X, p1.Y - EndTickLen / 2), new Point(p1.X, p1.Y + EndTickLen / 2));
                DrawTickLabel(dc, "A", new Point(p0.X, p0.Y + EndTickLen + 2), horizontal: true);
                DrawTickLabel(dc, "B", new Point(p1.X, p1.Y + EndTickLen + 2), horizontal: true);
            }
            else
            {
                dc.DrawLine(_endTickPen, new Point(p0.X - EndTickLen / 2, p0.Y), new Point(p0.X + EndTickLen / 2, p0.Y));
                dc.DrawLine(_endTickPen, new Point(p1.X - EndTickLen / 2, p1.Y), new Point(p1.X + EndTickLen / 2, p1.Y));
                DrawTickLabel(dc, "A", new Point(p0.X - EndTickLen - 2, p0.Y), horizontal: false);
                DrawTickLabel(dc, "B", new Point(p1.X - EndTickLen - 2, p1.Y), horizontal: false);
            }
        }

        void DrawSegment(DrawingContext dc, CutSegment seg, SectionCutVM vm)
        {
            var pts = seg.Points
                .Select(p => new { p, v = PlotMode == SectionPlotMode.Stress ? p.Sig : p.Eps })
                .Where(x => x.v != null)
                .ToList();
            if (pts.Count < 2) return;

            var vals = pts.Select(x => x.v!.Value).ToList();

            if ((vm.FillMode || vm.HatchMode) && !ExportPlainMode)
            {
                var fillGeom = new StreamGeometry();
                using (var ctx = fillGeom.Open())
                {
                    ctx.BeginFigure(ToScreen(pts[0].p.S * 1000.0, 0), true, true);
                    foreach (var x in pts)
                        ctx.LineTo(ToScreen(x.p.S * 1000.0, x.v!.Value), true, false);
                    ctx.LineTo(ToScreen(pts[^1].p.S * 1000.0, 0), true, false);
                }
                fillGeom.Freeze();

                var matType = vm.GetAreaMatType(seg.AreaIndex);
                double vMax = _vAbsMax;

                if (vm.FillMode)
                {
                    double avg = pts.Average(x => x.v!.Value);
                    var color = CutDiagramColors.Get(matType, avg, vMax);
                    var brush = new SolidColorBrush(Color.FromArgb(90, color.R, color.G, color.B));
                    dc.DrawGeometry(brush, null, fillGeom);
                }

                if (vm.HatchMode)
                {
                    dc.PushClip(fillGeom);
                    double avg = pts.Average(x => x.v!.Value);
                    var color = CutDiagramColors.Get(matType, avg, vMax);
                    var pen = new Pen(new SolidColorBrush(Color.FromArgb(160, color.R, color.G, color.B)), 0.8);
                    pen.Freeze();
                    var bounds = fillGeom.Bounds;
                    bool horizontal = CutViewModel?.IsHorizontal ?? true;
                    if (horizontal)
                    {
                        for (double x = bounds.Left + 3; x < bounds.Right; x += 5)
                            dc.DrawLine(pen, new Point(x, bounds.Top), new Point(x, bounds.Bottom));
                    }
                    else
                    {
                        for (double y = bounds.Top + 3; y < bounds.Bottom; y += 5)
                            dc.DrawLine(pen, new Point(bounds.Left, y), new Point(bounds.Right, y));
                    }
                    dc.Pop();
                }
            }

            foreach (var part in SectionCutDiagramStyle.SplitBySign(vals))
            {
                var pen = MakeSignPen(vals[part.Start]);
                var geom = new StreamGeometry();
                using (var ctx = geom.Open())
                {
                    bool first = true;
                    for (int i = part.Start; i < part.EndExclusive; i++)
                    {
                        var sc = ToScreen(pts[i].p.S * 1000.0, vals[i]);
                        if (first) { ctx.BeginFigure(sc, false, false); first = false; }
                        else ctx.LineTo(sc, true, false);
                    }
                }
                geom.Freeze();
                dc.DrawGeometry(null, pen, geom);
            }

            DrawSegmentEndCap(dc, pts[0].p.S * 1000.0, vals[0], MakeSignPen(vals[0]));
            DrawSegmentEndCap(dc, pts[^1].p.S * 1000.0, vals[^1], MakeSignPen(vals[^1]));
            DrawSegmentValueLabels(dc, pts[0].p.S * 1000.0, vals[0], pts[^1].p.S * 1000.0, vals[^1]);
        }

        static Pen MakeSignPen(double value)
        {
            var (r, g, b) = SectionCutDiagramStyle.CurveStrokeRgb(value);
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            var pen = new Pen(brush, 1.5);
            pen.Freeze();
            return pen;
        }

        void DrawSegmentValueLabels(DrawingContext dc, double s0Mm, double v0, double s1Mm, double v1)
        {
            var p0 = ToScreen(s0Mm, v0);
            var p1 = ToScreen(s1Mm, v1);
            DrawTickLabel(dc, FormatValue(v0), new Point(p0.X + 4, p0.Y - 12), horizontal: true);
            DrawTickLabel(dc, FormatValue(v1), new Point(p1.X + 4, p1.Y - 12), horizontal: true);
        }

        /// <summary>Отрезок от базы до кривой на границе сегмента (начало / конец / разрыв).</summary>
        void DrawSegmentEndCap(DrawingContext dc, double sMm, double value, Pen pen)
        {
            var basePt = ToScreen(sMm, 0);
            var curvePt = ToScreen(sMm, value);
            dc.DrawLine(pen, basePt, curvePt);
        }

        void DrawRebarOnCurve(DrawingContext dc, CutRebarMarker r, SectionCutVM vm, bool onCurve)
        {
            double computedV = PlotMode == SectionPlotMode.Stress ? r.Sig : r.Eps;
            double sMm = r.S * 1000.0;
            var basePt = ToScreen(sMm, 0);
            bool horizontal = vm.IsHorizontal;

            if (onCurve)
            {
                var color = CutDiagramColors.Get(MatType.ReSteelF, computedV, _rebarVAbsMax);
                var forcePen = MakeRebarForcePen(color);
                var fillBrush = new SolidColorBrush(color);
                fillBrush.Freeze();
                dc.DrawEllipse(fillBrush, forcePen, basePt, RebarHandleRadius, RebarHandleRadius);
                double lengthPx = ResolveRebarLengthPx(vm, r, computedV);
                var endPt = RebarEndScreen(basePt, computedV, lengthPx, horizontal);
                dc.DrawLine(forcePen, basePt, endPt);
                dc.DrawEllipse(Brushes.White, forcePen, endPt, RebarHandleRadius, RebarHandleRadius);
                string val = PlotMode == SectionPlotMode.Stress
                    ? $"σ={computedV:+0.##;-0.##}"
                    : $"ε={computedV:+0.00000;-0.00000}";
                var labelPt = horizontal
                    ? new Point(endPt.X + 6, endPt.Y + 6)
                    : new Point(endPt.X + 6, endPt.Y + 12);
                DrawTickLabel(dc, val, labelPt, horizontal: true);
            }
            else
            {
                var faint = new SolidColorBrush(Color.FromArgb(140, 0x66, 0x66, 0x66));
                faint.Freeze();
                dc.DrawEllipse(faint, new Pen(faint, 1.2), basePt, RebarHandleRadius, RebarHandleRadius);
                var labelPt = horizontal
                    ? new Point(basePt.X + 4, basePt.Y + RebarHandleRadius + 4)
                    : new Point(basePt.X + RebarHandleRadius + 4, basePt.Y + 8);
                DrawTickLabel(dc, $"№{r.Num}", labelPt, horizontal: true);
            }
        }

        bool TryHitRebarHandle(Point screen, out SectionCutVM.RebarLineKey key)
        {
            key = default;
            var vm = CutViewModel;
            var result = vm?.Result;
            if (vm == null || result == null) return false;

            bool horizontal = vm.IsHorizontal;
            double best = RebarHandleHitRadius;
            SectionCutVM.RebarLineKey? found = null;
            foreach (var r in result.Rebars)
            {
                double computedV = PlotMode == SectionPlotMode.Stress ? r.Sig : r.Eps;
                var basePt = ToScreen(r.S * 1000.0, 0);
                double lengthPx = ResolveRebarLengthPx(vm, r, computedV);
                var endPt = RebarEndScreen(basePt, computedV, lengthPx, horizontal);
                double d = (endPt - screen).Length;
                if (d < best)
                {
                    best = d;
                    found = SectionCutVM.RebarKey(r);
                }
            }
            if (found is not { } k) return false;
            key = k;
            return true;
        }

        void DrawHover(DrawingContext dc, SectionCutVM vm)
        {
            bool horizontal = vm.IsHorizontal;
            var pt = ToScreen(_hoverSmm, _hoverV);
            if (horizontal)
                dc.DrawLine(_hoverPen, new Point(pt.X, _plotOy), new Point(pt.X, _plotOy + _plotH));
            else
                dc.DrawLine(_hoverPen, new Point(_plotOx, pt.Y), new Point(_plotOx + _plotW, pt.Y));

            string sLabel = string.Format(CultureInfo.CurrentCulture, Loc.S("SectionCutHoverS"), _hoverSmm);
            string vLabel = PlotMode == SectionPlotMode.Stress
                ? string.Format(CultureInfo.CurrentCulture, Loc.S("SectionCutHoverSigma"), _hoverV)
                : string.Format(CultureInfo.CurrentCulture, Loc.S("SectionCutHoverEps"), _hoverV);
            DrawTickLabel(dc, sLabel, new Point(pt.X + 8, _plotOy + 4), horizontal: true);
            DrawTickLabel(dc, vLabel, new Point(pt.X + 8, pt.Y - 14), horizontal: true);
        }

        void DrawTickLabel(DrawingContext dc, string text, Point pos, bool horizontal)
        {
            var tf = new Typeface("Segoe UI");
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                tf, 10, Brushes.Black, 1.0);
            if (horizontal)
                dc.DrawText(ft, new Point(pos.X - ft.Width / 2, pos.Y));
            else
                dc.DrawText(ft, new Point(pos.X - ft.Width, pos.Y - ft.Height / 2));
        }

        void DrawAxisTitle(DrawingContext dc, string text, Point pos, bool center, bool vertical = false)
        {
            var tf = new Typeface("Segoe UI");
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                tf, 11, Brushes.Black, 1.0);
            if (vertical)
            {
                dc.PushTransform(new RotateTransform(-90, pos.X, pos.Y));
                dc.DrawText(ft, new Point(pos.X - ft.Width / 2, pos.Y));
                dc.Pop();
            }
            else
            {
                double x = center ? pos.X - ft.Width / 2 : pos.X;
                dc.DrawText(ft, new Point(x, pos.Y - ft.Height));
            }
        }

        void DrawHint(DrawingContext dc, string text)
        {
            var tf = new Typeface("Segoe UI");
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                tf, 12, Brushes.Gray, 1.0);
            dc.DrawText(ft, new Point((ActualWidth - ft.Width) / 2, (ActualHeight - ft.Height) / 2));
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            if (CutViewModel == null) return;

            var pos = e.GetPosition(this);
            double factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
            bool horizontal = CutViewModel.IsHorizontal;
            var mods = Keyboard.Modifiers;

            double s = horizontal ? ScreenToS(pos.X, true) : ScreenToS(pos.Y, false);
            double v = horizontal ? ScreenToV(pos.Y, true) : ScreenToV(pos.X, false);

            if (mods.HasFlag(ModifierKeys.Control))
                CutViewModel.ScaleV = Math.Clamp(CutViewModel.ScaleV * factor, 0.05, 50.0);
            else if (mods.HasFlag(ModifierKeys.Shift))
                CutViewModel.ScaleS = Math.Clamp(CutViewModel.ScaleS * factor, 0.1, 5.0);
            else
            {
                CutViewModel.ScaleS = Math.Clamp(CutViewModel.ScaleS * factor, 0.1, 5.0);
                CutViewModel.ScaleV = Math.Clamp(CutViewModel.ScaleV * factor, 0.05, 50.0);
            }

            var pt = ToScreen(s, v);
            _panX += pos.X - pt.X;
            _panY += pos.Y - pt.Y;

            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                OnFitRequested();
                e.Handled = true;
                return;
            }

            var pos = e.GetPosition(this);
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) &&
                TryHitRebarHandle(pos, out var key))
            {
                _dragRebarKey = key;
                CaptureMouse();
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                _panning = true;
                _dragStart = pos;
                CaptureMouse();
                e.Handled = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var screenPos = e.GetPosition(this);

            if (_dragRebarKey is { } dragKey && IsMouseCaptured && CutViewModel is { } vm && vm.Result is { } cutResult)
            {
                foreach (var r in cutResult.Rebars)
                {
                    if (SectionCutVM.RebarKey(r) != dragKey) continue;
                    var basePt = ToScreen(r.S * 1000.0, 0);
                    double computedV = PlotMode == SectionPlotMode.Stress ? r.Sig : r.Eps;
                    double px = RebarLengthPxFromScreen(basePt, screenPos, vm.IsHorizontal);
                    vm.SetRebarLengthPxOverride(dragKey, px);
                    break;
                }
                bool horizontal = vm.IsHorizontal;
                Cursor = horizontal ? Cursors.SizeNS : Cursors.SizeWE;
                InvalidateVisual();
                return;
            }

            if (_panning && IsMouseCaptured)
            {
                var pos = e.GetPosition(this);
                _panX += pos.X - _dragStart.X;
                _panY += pos.Y - _dragStart.Y;
                _dragStart = pos;
                InvalidateVisual();
                return;
            }

            var result = CutViewModel?.Result;
            if (result == null)
            {
                if (_hoverVisible)
                {
                    _hoverVisible = false;
                    InvalidateVisual();
                }
                return;
            }

            if (TryHitRebarHandle(screenPos, out _))
                Cursor = Cursors.SizeAll;
            else
                Cursor = null;

            CutSampleHit? nearest = null;
            double best = SnapRadiusPx;
            foreach (var seg in result.Segments)
                foreach (var p in seg.Points)
                {
                    double? v = PlotMode == SectionPlotMode.Stress ? p.Sig : p.Eps;
                    if (v == null) continue;
                    var sc = ToScreen(p.S * 1000.0, v.Value);
                    double d = (sc - screenPos).Length;
                    if (d < best) { best = d; nearest = new CutSampleHit(p.S * 1000.0, v.Value); }
                }

            bool wasHover = _hoverVisible;
            double prevS = _hoverSmm, prevV = _hoverV;
            if (nearest != null)
            {
                _hoverVisible = true;
                _hoverSmm = nearest.Value.S;
                _hoverV = nearest.Value.Value;
            }
            else
            {
                _hoverVisible = false;
            }
            ToolTipService.SetIsEnabled(this, false);

            if (wasHover != _hoverVisible ||
                Math.Abs(prevS - _hoverSmm) > 1e-9 ||
                Math.Abs(prevV - _hoverV) > 1e-12)
                InvalidateVisual();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            if (_hoverVisible)
            {
                _hoverVisible = false;
                InvalidateVisual();
            }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (_dragRebarKey != null)
            {
                _dragRebarKey = null;
                ReleaseMouseCapture();
                Cursor = null;
            }
            if (_panning)
            {
                _panning = false;
                ReleaseMouseCapture();
            }
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                _panning = true;
                _dragStart = e.GetPosition(this);
                CaptureMouse();
                e.Handled = true;
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle && _panning)
            {
                _panning = false;
                ReleaseMouseCapture();
            }
        }


        static Pen MakePen(Brush brush, double thickness)
        {
            var b = brush.Clone(); b.Freeze();
            var pen = new Pen(b, thickness); pen.Freeze();
            return pen;
        }

        static Pen MakeDashedPen(Color color, double thickness)
        {
            var b = new SolidColorBrush(color); b.Freeze();
            var pen = new Pen(b, thickness) { DashStyle = DashStyles.Dash }; pen.Freeze();
            return pen;
        }

        readonly record struct CutSampleHit(double S, double Value);
    }

    /// <summary>Цвета заливки/штриховки: бетон — сжатие красное, растяжение синее; арматура наоборот.</summary>
    static class CutDiagramColors
    {
        static readonly Color RedLight = Color.FromRgb(0xFF, 0xCC, 0xCC);
        static readonly Color RedDark = Color.FromRgb(0xCC, 0x00, 0x00);
        static readonly Color BlueLight = Color.FromRgb(0xCC, 0xDD, 0xFF);
        static readonly Color BlueDark = Color.FromRgb(0x00, 0x44, 0xCC);

        public static Color Get(MatType matType, double value, double vMax)
        {
            bool compression = value < 0;
            bool isRebar = matType is MatType.ReSteelF or MatType.ReSteelU or MatType.Steel;
            bool useRed = compression ? !isRebar : isRebar;
            double t = vMax > 1e-12 ? Math.Clamp(Math.Abs(value) / vMax, 0.15, 1.0) : 0.5;
            var light = useRed ? RedLight : BlueLight;
            var dark = useRed ? RedDark : BlueDark;
            return Color.FromRgb(
                (byte)(light.R + (dark.R - light.R) * t),
                (byte)(light.G + (dark.G - light.G) * t),
                (byte)(light.B + (dark.B - light.B) * t));
        }
    }
}
