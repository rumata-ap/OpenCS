using CScore;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using netDxf;
using netDxf.Entities;
using netDxf.Tables;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenCS.Services
{
    /// <summary>
    /// Пишет эпюру разреза в DXF в координатах текущего вида окна (слои по типам объектов).
    /// Заливка не экспортируется; штриховка — отрезками от базы к кривой.
    /// </summary>
    public static class SectionCutDxfExporter
    {
        const double TextHeight = 2.5;
        const double EndTickLen = 8.0;
        const double HatchStepPx = 5.0;
        const double TargetGridSpacingPx = 50.0;
        const double RebarHandleR = 4.0;

        const string LayerFrame = "CUT_FRAME";
        const string LayerGrid = "CUT_GRID";
        const string LayerAxes = "CUT_AXES";
        const string LayerBase = "CUT_BASE";
        const string LayerCurve = "CUT_CURVE";
        const string LayerHatch = "CUT_HATCH";
        const string LayerRebar = "CUT_REBAR";
        const string LayerLabels = "CUT_LABELS";
        const string LayerMarkers = "CUT_MARKERS";
        const string LayerEpsCu = "CUT_EPSCU";

        public static void Save(string path, SectionCutExportArgs args)
        {
            var result = args.Result;
            var view = args.View ?? BuildFallbackView(args);
            bool horizontal = view.Horizontal;
            bool chrome = args.AsOnScreen;
            double lengthMm = view.LengthMm > 1e-6 ? view.LengthMm : 1;

            var doc = new DxfDocument();
            EnsureLayers(doc);

            (double X, double Y) ToDxf(double sMm, double value) => view.ToDxf(sMm, value);
            (double X, double Y) ScreenToDxf(double sx, double sy) => view.ScreenToDxf(sx, sy);

            AciColor ColorOf(double v) => SectionCutDiagramStyle.CurveIsPositive(v) ? AciColor.Blue : AciColor.Red;

            if (chrome)
            {
                var frameTl = ScreenToDxf(view.PlotOx, view.PlotOy);
                var frameBr = ScreenToDxf(view.PlotOx + view.PlotW, view.PlotOy + view.PlotH);
                AddRect(doc, LayerFrame, frameTl, frameBr, AciColor.Default);
                AppendGrid(doc, view, args.Mode);
                AppendAxisTitles(doc, view, args.Mode);
                AppendEndMarkers(doc, view, lengthMm);
            }

            foreach (var seg in result.Segments)
            {
                if (seg.Points.Count < 2) continue;
                var s0 = ToDxf(seg.Points[0].S * 1000.0, 0);
                var s1 = ToDxf(seg.Points[^1].S * 1000.0, 0);
                var line = new Line(V(s0), V(s1)) { Layer = doc.Layers[LayerBase] };
                if (seg.AreaIndex == null)
                {
                    line.Linetype = Linetype.Dashed;
                    line.Color = AciColor.LightGray;
                }
                doc.Entities.Add(line);
            }

            foreach (var seg in result.Segments)
            {
                if (seg.AreaIndex == null || seg.Points.Count < 2) continue;
                var pts = seg.Points.Where(p => (args.Mode == SectionPlotMode.Stress ? p.Sig : p.Eps) != null).ToList();
                if (pts.Count < 2) continue;

                var vals = pts.Select(p => (args.Mode == SectionPlotMode.Stress ? p.Sig : p.Eps)!.Value).ToList();
                var sMm = pts.Select(p => p.S * 1000.0).ToList();

                if (chrome && args.HatchMode)
                {
                    foreach (var region in SectionCutDiagramStyle.BuildSignedFillCurves(sMm, vals))
                    {
                        if (region.Curve.Count < 1) continue;
                        double sample = region.Curve.Where(p => Math.Abs(p.V) > SectionCutDiagramStyle.SignEpsilon)
                            .Select(p => p.V)
                            .DefaultIfEmpty(region.Positive ? 1.0 : -1.0)
                            .Average();
                        var color = ColorOf(sample);
                        double s0 = region.Curve[0].S, s1 = region.Curve[^1].S;
                        double ds = HatchStepPx / Math.Max(view.ScaleS, 1e-9);
                        if (s1 < s0) (s0, s1) = (s1, s0);
                        for (double s = s0; s <= s1 + ds * 0.5; s += ds)
                        {
                            double v = InterpolateV(region.Curve, s);
                            var a = ToDxf(s, 0);
                            var b = ToDxf(s, v);
                            doc.Entities.Add(new Line(V(a), V(b))
                            {
                                Layer = doc.Layers[LayerHatch],
                                Color = color
                            });
                        }
                    }
                }

                foreach (var part in SectionCutDiagramStyle.SplitBySign(vals))
                {
                    var vertices = new List<Polyline2DVertex>();
                    for (int i = part.Start; i < part.EndExclusive; i++)
                    {
                        var (x, y) = ToDxf(pts[i].S * 1000.0, vals[i]);
                        vertices.Add(new Polyline2DVertex(x, y));
                    }
                    if (vertices.Count < 2) continue;
                    doc.Entities.Add(new Polyline2D(vertices)
                    {
                        Layer = doc.Layers[LayerCurve],
                        Color = ColorOf(vals[part.Start])
                    });
                }

                AppendEndCap(doc, ToDxf, pts[0].S * 1000.0, vals[0]);
                AppendEndCap(doc, ToDxf, pts[^1].S * 1000.0, vals[^1]);

                var t0 = ToDxf(pts[0].S * 1000.0, vals[0]);
                var t1 = ToDxf(pts[^1].S * 1000.0, vals[^1]);
                doc.Entities.Add(new Text(args.FormatValue(vals[0]), V(t0), TextHeight)
                {
                    Layer = doc.Layers[LayerLabels]
                });
                doc.Entities.Add(new Text(args.FormatValue(vals[^1]), V(t1), TextHeight)
                {
                    Layer = doc.Layers[LayerLabels]
                });
            }

            if (args.Mode == SectionPlotMode.Strain && args.EpsCu is { } epsCu)
            {
                var p0 = ToDxf(0, epsCu);
                var p1 = ToDxf(lengthMm, epsCu);
                doc.Entities.Add(new Line(V(p0), V(p1))
                {
                    Layer = doc.Layers[LayerEpsCu],
                    Linetype = Linetype.Dashed,
                    Color = AciColor.Magenta
                });
            }

            if (chrome)
            {
                foreach (var r in result.Rebars)
                    AppendRebar(doc, view, args, r, onCurve: true);
                foreach (var r in result.NearbyRebars)
                    AppendRebar(doc, view, args, r, onCurve: false);
            }

            doc.Save(path);
        }

        public static void Save(string path, SectionCutResult result, SectionPlotMode mode,
            bool horizontal, double? epsCu, bool asOnScreen = true) =>
            Save(path, new SectionCutExportArgs
            {
                Result = result,
                Mode = mode,
                Horizontal = horizontal,
                AsOnScreen = asOnScreen,
                FillMode = false,
                HatchMode = false,
                ShowRebarForce = false,
                EpsCu = epsCu
            });

        static void EnsureLayers(DxfDocument doc)
        {
            void Add(string name, AciColor color)
            {
                if (!doc.Layers.Contains(name))
                    doc.Layers.Add(new Layer(name) { Color = color });
            }
            Add(LayerFrame, AciColor.Default);
            Add(LayerGrid, AciColor.LightGray);
            Add(LayerAxes, AciColor.Default);
            Add(LayerBase, AciColor.Default);
            Add(LayerCurve, AciColor.Blue);
            Add(LayerHatch, AciColor.Cyan);
            Add(LayerRebar, AciColor.Yellow);
            Add(LayerLabels, AciColor.Yellow);
            Add(LayerMarkers, AciColor.Default);
            Add(LayerEpsCu, AciColor.Magenta);
        }

        static void AppendGrid(DxfDocument doc, SectionCutViewTransform view, SectionPlotMode mode)
        {
            double sStep = NiceStep(TargetGridSpacingPx / Math.Max(view.ScaleS, 1e-9));
            double vStep = NiceStep(TargetGridSpacingPx / Math.Max(view.ScaleV, 1e-9));
            double ox = view.PlotOx, oy = view.PlotOy, pw = view.PlotW, ph = view.PlotH;
            var layer = doc.Layers[LayerGrid];
            var labelLayer = doc.Layers[LayerLabels];

            if (view.Horizontal)
            {
                double sMin = view.ScreenToS(ox);
                double sMax = view.ScreenToS(ox + pw);
                double vTop = view.ScreenToV(oy);
                double vBot = view.ScreenToV(oy + ph);

                for (double s = FloorToStep(sMin, sStep); s <= sMax + sStep * 0.5; s += sStep)
                {
                    var p = view.ToScreen(s, 0);
                    if (p.X < ox - 1 || p.X > ox + pw + 1) continue;
                    // Вертикальная линия на всю высоту рамки.
                    var a = view.ScreenToDxf(p.X, oy);
                    var b = view.ScreenToDxf(p.X, oy + ph);
                    doc.Entities.Add(new Line(V(a), V(b)) { Layer = layer, Color = AciColor.LightGray });
                    var tick = view.ScreenToDxf(p.X, oy + ph + 14);
                    string label = sStep < 1
                        ? s.ToString("F1", CultureInfo.InvariantCulture)
                        : s.ToString("F0", CultureInfo.InvariantCulture);
                    doc.Entities.Add(new Text(label, V(tick), TextHeight * 0.8)
                    {
                        Layer = labelLayer,
                        Color = AciColor.DarkGray
                    });
                }
                for (double v = FloorToStep(Math.Min(vBot, vTop), vStep); v <= Math.Max(vBot, vTop) + vStep * 0.5; v += vStep)
                {
                    var p = view.ToScreen(0, v);
                    if (p.Y < oy - 1 || p.Y > oy + ph + 1) continue;
                    // Горизонтальная линия на всю ширину рамки.
                    var a = view.ScreenToDxf(ox, p.Y);
                    var b = view.ScreenToDxf(ox + pw, p.Y);
                    bool zero = Math.Abs(v) < vStep * 0.01;
                    doc.Entities.Add(new Line(V(a), V(b))
                    {
                        Layer = layer,
                        Color = zero ? AciColor.DarkGray : AciColor.LightGray
                    });
                    var tick = view.ScreenToDxf(ox - 6, p.Y);
                    doc.Entities.Add(new Text(FormatTickV(v, vStep, mode), V(tick), TextHeight * 0.8)
                    {
                        Layer = labelLayer,
                        Color = AciColor.DarkGray
                    });
                }
            }
            else
            {
                double sMin = view.ScreenToS(oy);
                double sMax = view.ScreenToS(oy + ph);
                double vMin = view.ScreenToV(ox);
                double vMax = view.ScreenToV(ox + pw);

                for (double s = FloorToStep(sMin, sStep); s <= sMax + sStep * 0.5; s += sStep)
                {
                    var p = view.ToScreen(s, 0);
                    if (p.Y < oy - 1 || p.Y > oy + ph + 1) continue;
                    // Горизонтальная (на экране) линия — на всю ширину рамки.
                    var a = view.ScreenToDxf(ox, p.Y);
                    var b = view.ScreenToDxf(ox + pw, p.Y);
                    doc.Entities.Add(new Line(V(a), V(b)) { Layer = layer, Color = AciColor.LightGray });
                    var tick = view.ScreenToDxf(ox + pw + 6, p.Y);
                    string label = sStep < 1
                        ? s.ToString("F1", CultureInfo.InvariantCulture)
                        : s.ToString("F0", CultureInfo.InvariantCulture);
                    doc.Entities.Add(new Text(label, V(tick), TextHeight * 0.8)
                    {
                        Layer = labelLayer,
                        Color = AciColor.DarkGray
                    });
                }
                for (double v = FloorToStep(vMin, vStep); v <= vMax + vStep * 0.5; v += vStep)
                {
                    var p = view.ToScreen(0, v);
                    if (p.X < ox - 1 || p.X > ox + pw + 1) continue;
                    var a = view.ScreenToDxf(p.X, oy);
                    var b = view.ScreenToDxf(p.X, oy + ph);
                    bool zero = Math.Abs(v) < vStep * 0.01;
                    doc.Entities.Add(new Line(V(a), V(b))
                    {
                        Layer = layer,
                        Color = zero ? AciColor.DarkGray : AciColor.LightGray
                    });
                    var tick = view.ScreenToDxf(p.X, oy - 6);
                    doc.Entities.Add(new Text(FormatTickV(v, vStep, mode), V(tick), TextHeight * 0.8)
                    {
                        Layer = labelLayer,
                        Color = AciColor.DarkGray
                    });
                }
            }
        }

        static void AppendAxisTitles(DxfDocument doc, SectionCutViewTransform view, SectionPlotMode mode)
        {
            string axisS = Loc.S("SectionCutAxisS");
            string axisV = mode == SectionPlotMode.Stress
                ? Loc.S("SectionCutAxisSigma") : Loc.S("SectionCutAxisEps");
            var layer = doc.Layers[LayerAxes];
            if (view.Horizontal)
            {
                var pS = view.ScreenToDxf(view.PlotOx + view.PlotW / 2, view.CanvasHeight - 8);
                var pV = view.ScreenToDxf(12, view.PlotOy + view.PlotH / 2);
                doc.Entities.Add(new Text(axisS, V(pS), TextHeight) { Layer = layer });
                doc.Entities.Add(new Text(axisV, V(pV), TextHeight) { Layer = layer, Rotation = 90 });
            }
            else
            {
                var pS = view.ScreenToDxf(view.CanvasWidth - 10, view.PlotOy + view.PlotH / 2);
                var pV = view.ScreenToDxf(view.PlotOx + view.PlotW / 2, 18);
                doc.Entities.Add(new Text(axisS, V(pS), TextHeight) { Layer = layer, Rotation = 90 });
                doc.Entities.Add(new Text(axisV, V(pV), TextHeight) { Layer = layer });
            }
        }

        static void AppendEndMarkers(DxfDocument doc, SectionCutViewTransform view, double lengthMm)
        {
            bool horizontal = view.Horizontal;
            var s0 = view.ToScreen(0, 0);
            var s1 = view.ToScreen(lengthMm, 0);
            double h = EndTickLen / 2;
            if (horizontal)
            {
                var a0 = view.ScreenToDxf(s0.X, s0.Y - h);
                var b0 = view.ScreenToDxf(s0.X, s0.Y + h);
                var a1 = view.ScreenToDxf(s1.X, s1.Y - h);
                var b1 = view.ScreenToDxf(s1.X, s1.Y + h);
                doc.Entities.Add(new Line(V(a0), V(b0)) { Layer = doc.Layers[LayerMarkers] });
                doc.Entities.Add(new Line(V(a1), V(b1)) { Layer = doc.Layers[LayerMarkers] });
            }
            else
            {
                var a0 = view.ScreenToDxf(s0.X - h, s0.Y);
                var b0 = view.ScreenToDxf(s0.X + h, s0.Y);
                var a1 = view.ScreenToDxf(s1.X - h, s1.Y);
                var b1 = view.ScreenToDxf(s1.X + h, s1.Y);
                doc.Entities.Add(new Line(V(a0), V(b0)) { Layer = doc.Layers[LayerMarkers] });
                doc.Entities.Add(new Line(V(a1), V(b1)) { Layer = doc.Layers[LayerMarkers] });
            }
            var p0 = view.ToDxf(0, 0);
            var p1 = view.ToDxf(lengthMm, 0);
            doc.Entities.Add(new Text("A", V(p0), TextHeight) { Layer = doc.Layers[LayerMarkers] });
            doc.Entities.Add(new Text("B", V(p1), TextHeight) { Layer = doc.Layers[LayerMarkers] });
        }

        static void AppendEndCap(DxfDocument doc, Func<double, double, (double X, double Y)> toDxf, double sMm, double v)
        {
            var a = toDxf(sMm, 0);
            var b = toDxf(sMm, v);
            doc.Entities.Add(new Line(V(a), V(b))
            {
                Layer = doc.Layers[LayerCurve],
                Color = SectionCutDiagramStyle.CurveIsPositive(v) ? AciColor.Blue : AciColor.Red
            });
        }

        static void AppendRebar(DxfDocument doc, SectionCutViewTransform view,
            SectionCutExportArgs args, CutRebarMarker r, bool onCurve)
        {
            double sMm = r.S * 1000.0;
            var layer = doc.Layers[LayerRebar];
            var bs = view.ToScreen(sMm, 0);
            var basePt = view.ScreenToDxf(bs.X, bs.Y);

            if (!onCurve)
            {
                doc.Entities.Add(new Circle(V(basePt), RebarHandleR)
                {
                    Layer = layer,
                    Color = AciColor.DarkGray
                });
                if (r.Num is int num)
                    doc.Entities.Add(new Text($"N{num}", new Vector3(basePt.X + 2, basePt.Y + 2, 0), TextHeight * 0.8)
                    {
                        Layer = doc.Layers[LayerLabels],
                        Color = AciColor.DarkGray
                    });
                return;
            }

            double val = args.RebarDisplayValue(r);
            double len = Math.Max(0, args.RebarLengthPx(r));
            int sign = val < 0 ? -1 : 1;
            var es = view.Horizontal
                ? (bs.X, bs.Y - sign * len)
                : (bs.X + sign * len, bs.Y);
            var endPt = view.ScreenToDxf(es.Item1, es.Item2);
            var color = SectionCutDiagramStyle.CurveIsPositive(val) ? AciColor.Blue : AciColor.Red;
            doc.Entities.Add(new Line(V(basePt), V(endPt)) { Layer = layer, Color = color });
            doc.Entities.Add(new Circle(V(basePt), RebarHandleR) { Layer = layer, Color = color });
            doc.Entities.Add(new Circle(V(endPt), RebarHandleR) { Layer = layer, Color = color });
            doc.Entities.Add(new Text(args.FormatRebarLabel(val),
                new Vector3(endPt.X + 1.5, endPt.Y + 1.5, 0), TextHeight)
            {
                Layer = doc.Layers[LayerLabels],
                Color = color
            });
        }

        static void AddRect(DxfDocument doc, string layerName, (double X, double Y) tl, (double X, double Y) br, AciColor color)
        {
            double x0 = Math.Min(tl.X, br.X), x1 = Math.Max(tl.X, br.X);
            double y0 = Math.Min(tl.Y, br.Y), y1 = Math.Max(tl.Y, br.Y);
            var layer = doc.Layers[layerName];
            doc.Entities.Add(new Line(new Vector3(x0, y0, 0), new Vector3(x1, y0, 0)) { Layer = layer, Color = color });
            doc.Entities.Add(new Line(new Vector3(x1, y0, 0), new Vector3(x1, y1, 0)) { Layer = layer, Color = color });
            doc.Entities.Add(new Line(new Vector3(x1, y1, 0), new Vector3(x0, y1, 0)) { Layer = layer, Color = color });
            doc.Entities.Add(new Line(new Vector3(x0, y1, 0), new Vector3(x0, y0, 0)) { Layer = layer, Color = color });
        }

        static SectionCutViewTransform BuildFallbackView(SectionCutExportArgs args)
        {
            double lengthMm = Distance(args.Result.Start, args.Result.End) * 1000.0;
            if (lengthMm < 1e-6) lengthMm = 1;
            CollectValueRange(args.Result, args.Mode, args.EpsCu, out double vMin, out double vMax);
            const double w = 900, h = 520, padL = 58, padR = 56, padT = 40, padB = 48;
            double plotW = w - padL - padR, plotH = h - padT - padB;
            bool horizontal = args.Horizontal;
            double sAxis = horizontal ? plotW : plotH;
            double vAxis = horizontal ? plotH : plotW;
            double scaleS = sAxis * 0.9 / lengthMm;
            double vRange = Math.Max(vMax - vMin, 1e-9);
            double scaleV = vAxis * 0.9 / vRange;
            return new SectionCutViewTransform
            {
                CanvasWidth = w,
                CanvasHeight = h,
                PlotOx = padL,
                PlotOy = padT,
                PlotW = plotW,
                PlotH = plotH,
                PanX = 0,
                PanY = 0,
                ScaleS = scaleS,
                ScaleV = scaleV,
                LengthMm = lengthMm,
                FitVMin = vMin,
                FitVMax = vMax,
                Horizontal = horizontal
            };
        }

        static void CollectValueRange(SectionCutResult result, SectionPlotMode mode, double? epsCu,
            out double vMin, out double vMax)
        {
            vMin = double.PositiveInfinity;
            vMax = double.NegativeInfinity;
            foreach (var seg in result.Segments)
            {
                if (seg.AreaIndex == null) continue;
                foreach (var p in seg.Points)
                {
                    double? v = mode == SectionPlotMode.Stress ? p.Sig : p.Eps;
                    if (v == null) continue;
                    if (v < vMin) vMin = v.Value;
                    if (v > vMax) vMax = v.Value;
                }
            }
            if (epsCu is { } ec && mode == SectionPlotMode.Strain)
            {
                if (ec < vMin) vMin = ec;
                if (ec > vMax) vMax = ec;
            }
            if (double.IsInfinity(vMin) || double.IsInfinity(vMax))
            {
                vMin = -1;
                vMax = 1;
            }
            if (vMax < vMin) (vMin, vMax) = (vMax, vMin);
        }

        static double InterpolateV(IReadOnlyList<(double S, double V)> curve, double s)
        {
            if (curve.Count == 1) return curve[0].V;
            if (s <= curve[0].S) return curve[0].V;
            if (s >= curve[^1].S) return curve[^1].V;
            for (int i = 1; i < curve.Count; i++)
            {
                if (s <= curve[i].S)
                {
                    double t = (s - curve[i - 1].S) / Math.Max(curve[i].S - curve[i - 1].S, 1e-12);
                    return curve[i - 1].V + t * (curve[i].V - curve[i - 1].V);
                }
            }
            return curve[^1].V;
        }

        static double NiceStep(double rawStep)
        {
            if (rawStep <= 1e-12) return 1;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
            double norm = rawStep / mag;
            double nice = norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 5 ? 5 : 10;
            return nice * mag;
        }

        static double FloorToStep(double value, double step) =>
            Math.Floor(value / step - 1e-9) * step;

        static string FormatTickV(double v, double step, SectionPlotMode mode)
        {
            if (mode == SectionPlotMode.Stress)
            {
                if (step < 0.05) return v.ToString("+0.000;-0.000", CultureInfo.InvariantCulture);
                if (step < 0.5) return v.ToString("+0.00;-0.00", CultureInfo.InvariantCulture);
                return v.ToString("+0.##;-0.##", CultureInfo.InvariantCulture);
            }
            if (step < 0.0001) return v.ToString("+0.00000;-0.00000", CultureInfo.InvariantCulture);
            return v.ToString("+0.###;-0.###", CultureInfo.InvariantCulture);
        }

        static Vector3 V((double X, double Y) p) => new(p.X, p.Y, 0);

        static double Distance((double X, double Y) a, (double X, double Y) b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
