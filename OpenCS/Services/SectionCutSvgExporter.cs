using CScore;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenCS.Services
{
    /// <summary>Пишет эпюру разреза в SVG в координатах текущего вида окна.</summary>
    public static class SectionCutSvgExporter
    {
        const double HatchStepPx = 5.0;
        const double EndTickLen = 8.0;
        const double RebarHandleR = 4.0;
        const double TargetGridSpacingPx = 50.0;

        public static void Save(string path, SectionCutExportArgs args) =>
            File.WriteAllText(path, Build(args), Encoding.UTF8);

        public static void Save(string path, SectionCutResult result, SectionPlotMode mode,
            bool horizontal, bool fillMode, double? epsCu, bool asOnScreen = true) =>
            Save(path, new SectionCutExportArgs
            {
                Result = result,
                Mode = mode,
                Horizontal = horizontal,
                AsOnScreen = asOnScreen,
                FillMode = fillMode,
                HatchMode = false,
                ShowRebarForce = false,
                EpsCu = epsCu
            });

        public static string Build(SectionCutExportArgs args)
        {
            var result = args.Result;
            var view = args.View ?? BuildFallbackView(args);
            bool horizontal = view.Horizontal;
            bool chrome = args.AsOnScreen;
            double lengthMm = view.LengthMm > 1e-6 ? view.LengthMm : 1;

            Func<double, double, (double X, double Y)> toScreen = view.ToScreen;

            var sb = new StringBuilder();
            sb.AppendLine(FormattableString.Invariant(
                $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{view.CanvasWidth:F0}\" height=\"{view.CanvasHeight:F0}\" viewBox=\"0 0 {view.CanvasWidth:F1} {view.CanvasHeight:F1}\">"));
            sb.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");

            if (chrome)
            {
                sb.AppendLine(FormattableString.Invariant(
                    $"<rect x=\"{view.PlotOx:F1}\" y=\"{view.PlotOy:F1}\" width=\"{view.PlotW:F1}\" height=\"{view.PlotH:F1}\" fill=\"none\" stroke=\"black\" stroke-width=\"1\"/>"));
                AppendGrid(sb, view, args.Mode);
                AppendAxisTitles(sb, view, args.Mode);
                AppendEndMarkers(sb, toScreen, horizontal, lengthMm);
            }

            foreach (var seg in result.Segments)
            {
                if (seg.Points.Count < 2) continue;
                var s0 = toScreen(seg.Points[0].S * 1000.0, 0);
                var s1 = toScreen(seg.Points[^1].S * 1000.0, 0);
                string dash = seg.AreaIndex == null ? " stroke-dasharray=\"4,3\"" : "";
                string stroke = seg.AreaIndex == null ? "#B0B0B0" : "black";
                sb.AppendLine(FormattableString.Invariant(
                    $"<line x1=\"{s0.X:F1}\" y1=\"{s0.Y:F1}\" x2=\"{s1.X:F1}\" y2=\"{s1.Y:F1}\" stroke=\"{stroke}\" stroke-width=\"1.5\"{dash}/>"));
            }

            foreach (var seg in result.Segments)
            {
                if (seg.AreaIndex == null || seg.Points.Count < 2) continue;
                var pts = seg.Points.Where(p => (args.Mode == SectionPlotMode.Stress ? p.Sig : p.Eps) != null).ToList();
                if (pts.Count < 2) continue;

                var vals = pts.Select(p => (args.Mode == SectionPlotMode.Stress ? p.Sig : p.Eps)!.Value).ToList();
                var sMm = pts.Select(p => p.S * 1000.0).ToList();

                if (chrome && (args.FillMode || args.HatchMode))
                {
                    int clipId = 0;
                    foreach (var region in SectionCutDiagramStyle.BuildSignedFillCurves(sMm, vals))
                    {
                        if (region.Curve.Count < 1) continue;
                        var baseStart = toScreen(region.Curve[0].S, 0);
                        var baseEnd = toScreen(region.Curve[^1].S, 0);
                        string polyPts = FormattableString.Invariant($"{baseStart.X:F1},{baseStart.Y:F1} ") +
                            string.Join(" ", region.Curve.Select(pt =>
                            {
                                var sc = toScreen(pt.S, pt.V);
                                return FormattableString.Invariant($"{sc.X:F1},{sc.Y:F1}");
                            })) +
                            FormattableString.Invariant($" {baseEnd.X:F1},{baseEnd.Y:F1}");

                        double sample = region.Curve.Where(p => Math.Abs(p.V) > SectionCutDiagramStyle.SignEpsilon)
                            .Select(p => p.V)
                            .DefaultIfEmpty(region.Positive ? 1.0 : -1.0)
                            .Average();
                        var (fr, fg, fb) = SectionCutDiagramStyle.CurveStrokeRgb(sample);
                        string hex = FormattableString.Invariant($"{fr:X2}{fg:X2}{fb:X2}");

                        if (args.FillMode)
                        {
                            sb.AppendLine(FormattableString.Invariant(
                                $"<polygon points=\"{polyPts}\" fill=\"#{hex}\" fill-opacity=\"0.35\" stroke=\"none\"/>"));
                        }

                        if (args.HatchMode)
                        {
                            string id = $"h{seg.AreaIndex}_{clipId++}";
                            sb.AppendLine($"<defs><clipPath id=\"{id}\"><polygon points=\"{polyPts}\"/></clipPath></defs>");
                            sb.AppendLine($"<g clip-path=\"url(#{id})\">");
                            AppendHatchLines(sb, toScreen, region.Curve, view.ScaleS, hex);
                            sb.AppendLine("</g>");
                        }
                    }
                }

                foreach (var part in SectionCutDiagramStyle.SplitBySign(vals))
                {
                    var slice = new List<(double X, double Y)>();
                    for (int i = part.Start; i < part.EndExclusive; i++)
                        slice.Add(toScreen(pts[i].S * 1000.0, vals[i]));
                    if (slice.Count < 2) continue;
                    var (r, g, b) = SectionCutDiagramStyle.CurveStrokeRgb(vals[part.Start]);
                    string stroke = FormattableString.Invariant($"#{r:X2}{g:X2}{b:X2}");
                    string polyline = string.Join(" ", slice.Select(pt => FormattableString.Invariant($"{pt.X:F1},{pt.Y:F1}")));
                    sb.AppendLine($"<polyline points=\"{polyline}\" fill=\"none\" stroke=\"{stroke}\" stroke-width=\"1.5\"/>");
                }

                AppendEndCap(sb, toScreen, pts[0].S * 1000.0, vals[0]);
                AppendEndCap(sb, toScreen, pts[^1].S * 1000.0, vals[^1]);

                var t0 = toScreen(pts[0].S * 1000.0, vals[0]);
                var t1 = toScreen(pts[^1].S * 1000.0, vals[^1]);
                sb.AppendLine(FormattableString.Invariant(
                    $"<text x=\"{t0.X + 4:F1}\" y=\"{t0.Y - 4:F1}\" font-size=\"10\" font-family=\"Segoe UI\">{args.FormatValue(vals[0])}</text>"));
                sb.AppendLine(FormattableString.Invariant(
                    $"<text x=\"{t1.X + 4:F1}\" y=\"{t1.Y - 4:F1}\" font-size=\"10\" font-family=\"Segoe UI\">{args.FormatValue(vals[^1])}</text>"));
            }

            if (args.Mode == SectionPlotMode.Strain && args.EpsCu is { } epsCu)
            {
                var p0 = toScreen(0, epsCu);
                var p1 = toScreen(lengthMm, epsCu);
                sb.AppendLine(FormattableString.Invariant(
                    $"<line x1=\"{p0.X:F1}\" y1=\"{p0.Y:F1}\" x2=\"{p1.X:F1}\" y2=\"{p1.Y:F1}\" stroke=\"purple\" stroke-width=\"1.2\" stroke-dasharray=\"5,3\"/>"));
            }

            if (chrome)
            {
                foreach (var r in result.Rebars)
                    AppendRebar(sb, toScreen, args, r, onCurve: true, horizontal);
                foreach (var r in result.NearbyRebars)
                    AppendRebar(sb, toScreen, args, r, onCurve: false, horizontal);
            }

            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        static SectionCutViewTransform BuildFallbackView(SectionCutExportArgs args)
        {
            double lengthMm = Distance(args.Result.Start, args.Result.End) * 1000.0;
            if (lengthMm < 1e-6) lengthMm = 1;
            CollectValueRange(args.Result, args.Mode, args.EpsCu, out double vMin, out double vMax, out _);
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

        static void AppendGrid(StringBuilder sb, SectionCutViewTransform view, SectionPlotMode mode)
        {
            double sStep = NiceStep(TargetGridSpacingPx / Math.Max(view.ScaleS, 1e-9));
            double vStep = NiceStep(TargetGridSpacingPx / Math.Max(view.ScaleV, 1e-9));
            double ox = view.PlotOx, oy = view.PlotOy, pw = view.PlotW, ph = view.PlotH;

            if (view.Horizontal)
            {
                double sMin = view.ScreenToS(ox);
                double sMax = view.ScreenToS(ox + pw);
                double vMax = view.ScreenToV(oy);
                double vMin = view.ScreenToV(oy + ph);

                for (double s = FloorToStep(sMin, sStep); s <= sMax + sStep * 0.5; s += sStep)
                {
                    var p = view.ToScreen(s, 0);
                    if (p.X < ox - 1 || p.X > ox + pw + 1) continue;
                    sb.AppendLine(FormattableString.Invariant(
                        $"<line x1=\"{p.X:F1}\" y1=\"{oy:F1}\" x2=\"{p.X:F1}\" y2=\"{oy + ph:F1}\" stroke=\"#E0E0E0\" stroke-width=\"0.8\"/>"));
                    string label = sStep < 1 ? s.ToString("F1", CultureInfo.InvariantCulture) : s.ToString("F0", CultureInfo.InvariantCulture);
                    sb.AppendLine(FormattableString.Invariant(
                        $"<text x=\"{p.X:F1}\" y=\"{oy + ph + 14:F1}\" text-anchor=\"middle\" font-size=\"10\" font-family=\"Segoe UI\">{label}</text>"));
                }
                for (double v = FloorToStep(vMin, vStep); v <= vMax + vStep * 0.5; v += vStep)
                {
                    var p = view.ToScreen(0, v);
                    if (p.Y < oy - 1 || p.Y > oy + ph + 1) continue;
                    bool zero = Math.Abs(v) < vStep * 0.01;
                    sb.AppendLine(FormattableString.Invariant(
                        $"<line x1=\"{ox:F1}\" y1=\"{p.Y:F1}\" x2=\"{ox + pw:F1}\" y2=\"{p.Y:F1}\" stroke=\"{(zero ? "#A0A0A0" : "#E0E0E0")}\" stroke-width=\"{(zero ? 1.0 : 0.8)}\"/>"));
                    sb.AppendLine(FormattableString.Invariant(
                        $"<text x=\"{ox - 6:F1}\" y=\"{p.Y + 3:F1}\" text-anchor=\"end\" font-size=\"10\" font-family=\"Segoe UI\">{FormatTickV(v, vStep, mode)}</text>"));
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
                    // Горизонтальная линия на всю ширину рамки.
                    sb.AppendLine(FormattableString.Invariant(
                        $"<line x1=\"{ox:F1}\" y1=\"{p.Y:F1}\" x2=\"{ox + pw:F1}\" y2=\"{p.Y:F1}\" stroke=\"#E0E0E0\" stroke-width=\"0.8\"/>"));
                    string label = sStep < 1 ? s.ToString("F1", CultureInfo.InvariantCulture) : s.ToString("F0", CultureInfo.InvariantCulture);
                    sb.AppendLine(FormattableString.Invariant(
                        $"<text x=\"{ox + pw + 6:F1}\" y=\"{p.Y + 3:F1}\" font-size=\"10\" font-family=\"Segoe UI\">{label}</text>"));
                }
                for (double v = FloorToStep(vMin, vStep); v <= vMax + vStep * 0.5; v += vStep)
                {
                    var p = view.ToScreen(0, v);
                    if (p.X < ox - 1 || p.X > ox + pw + 1) continue;
                    bool zero = Math.Abs(v) < vStep * 0.01;
                    sb.AppendLine(FormattableString.Invariant(
                        $"<line x1=\"{p.X:F1}\" y1=\"{oy:F1}\" x2=\"{p.X:F1}\" y2=\"{oy + ph:F1}\" stroke=\"{(zero ? "#A0A0A0" : "#E0E0E0")}\" stroke-width=\"{(zero ? 1.0 : 0.8)}\"/>"));
                    sb.AppendLine(FormattableString.Invariant(
                        $"<text x=\"{p.X:F1}\" y=\"{oy - 6:F1}\" text-anchor=\"middle\" font-size=\"10\" font-family=\"Segoe UI\">{FormatTickV(v, vStep, mode)}</text>"));
                }
            }
        }

        static void AppendAxisTitles(StringBuilder sb, SectionCutViewTransform view, SectionPlotMode mode)
        {
            string axisS = Esc(Loc.S("SectionCutAxisS"));
            string axisV = Esc(mode == SectionPlotMode.Stress
                ? Loc.S("SectionCutAxisSigma") : Loc.S("SectionCutAxisEps"));
            if (view.Horizontal)
            {
                sb.AppendLine(FormattableString.Invariant(
                    $"<text x=\"{view.PlotOx + view.PlotW / 2:F1}\" y=\"{view.CanvasHeight - 8:F1}\" text-anchor=\"middle\" font-size=\"11\" font-family=\"Segoe UI\">{axisS}</text>"));
                double cy = view.PlotOy + view.PlotH / 2;
                sb.AppendLine(FormattableString.Invariant(
                    $"<text x=\"12\" y=\"{cy:F1}\" text-anchor=\"middle\" font-size=\"11\" font-family=\"Segoe UI\" transform=\"rotate(-90 12 {cy:F1})\">{axisV}</text>"));
            }
            else
            {
                double cy = view.PlotOy + view.PlotH / 2;
                sb.AppendLine(FormattableString.Invariant(
                    $"<text x=\"{view.CanvasWidth - 10:F1}\" y=\"{cy:F1}\" text-anchor=\"middle\" font-size=\"11\" font-family=\"Segoe UI\" transform=\"rotate(-90 {view.CanvasWidth - 10:F1} {cy:F1})\">{axisS}</text>"));
                sb.AppendLine(FormattableString.Invariant(
                    $"<text x=\"{view.PlotOx + view.PlotW / 2:F1}\" y=\"18\" text-anchor=\"middle\" font-size=\"11\" font-family=\"Segoe UI\">{axisV}</text>"));
            }
        }

        static void AppendEndMarkers(StringBuilder sb, Func<double, double, (double X, double Y)> toScreen,
            bool horizontal, double lengthMm)
        {
            var p0 = toScreen(0, 0);
            var p1 = toScreen(lengthMm, 0);
            if (horizontal)
            {
                sb.AppendLine(FormattableString.Invariant(
                    $"<line x1=\"{p0.X:F1}\" y1=\"{p0.Y - EndTickLen / 2:F1}\" x2=\"{p0.X:F1}\" y2=\"{p0.Y + EndTickLen / 2:F1}\" stroke=\"black\" stroke-width=\"1\"/>"));
                sb.AppendLine(FormattableString.Invariant(
                    $"<line x1=\"{p1.X:F1}\" y1=\"{p1.Y - EndTickLen / 2:F1}\" x2=\"{p1.X:F1}\" y2=\"{p1.Y + EndTickLen / 2:F1}\" stroke=\"black\" stroke-width=\"1\"/>"));
                sb.AppendLine(FormattableString.Invariant(
                    $"<text x=\"{p0.X:F1}\" y=\"{p0.Y + EndTickLen + 12:F1}\" text-anchor=\"middle\" font-size=\"11\" font-family=\"Segoe UI\">A</text>"));
                sb.AppendLine(FormattableString.Invariant(
                    $"<text x=\"{p1.X:F1}\" y=\"{p1.Y + EndTickLen + 12:F1}\" text-anchor=\"middle\" font-size=\"11\" font-family=\"Segoe UI\">B</text>"));
            }
            else
            {
                sb.AppendLine(FormattableString.Invariant(
                    $"<line x1=\"{p0.X - EndTickLen / 2:F1}\" y1=\"{p0.Y:F1}\" x2=\"{p0.X + EndTickLen / 2:F1}\" y2=\"{p0.Y:F1}\" stroke=\"black\" stroke-width=\"1\"/>"));
                sb.AppendLine(FormattableString.Invariant(
                    $"<line x1=\"{p1.X - EndTickLen / 2:F1}\" y1=\"{p1.Y:F1}\" x2=\"{p1.X + EndTickLen / 2:F1}\" y2=\"{p1.Y:F1}\" stroke=\"black\" stroke-width=\"1\"/>"));
                sb.AppendLine(FormattableString.Invariant(
                    $"<text x=\"{p0.X - EndTickLen - 4:F1}\" y=\"{p0.Y + 4:F1}\" text-anchor=\"end\" font-size=\"11\" font-family=\"Segoe UI\">A</text>"));
                sb.AppendLine(FormattableString.Invariant(
                    $"<text x=\"{p1.X - EndTickLen - 4:F1}\" y=\"{p1.Y + 4:F1}\" text-anchor=\"end\" font-size=\"11\" font-family=\"Segoe UI\">B</text>"));
            }
        }

        static void AppendHatchLines(StringBuilder sb, Func<double, double, (double X, double Y)> toScreen,
            IReadOnlyList<(double S, double V)> curve, double scaleS, string hex)
        {
            if (curve.Count < 1) return;
            double s0 = curve[0].S, s1 = curve[^1].S;
            double ds = HatchStepPx / Math.Max(scaleS, 1e-9);
            if (ds < 1e-9) ds = Math.Abs(s1 - s0) / 20;
            if (s1 < s0) (s0, s1) = (s1, s0);
            for (double s = s0; s <= s1 + ds * 0.5; s += ds)
            {
                double v = InterpolateV(curve, s);
                var a = toScreen(s, 0);
                var b = toScreen(s, v);
                sb.AppendLine(FormattableString.Invariant(
                    $"<line x1=\"{a.X:F1}\" y1=\"{a.Y:F1}\" x2=\"{b.X:F1}\" y2=\"{b.Y:F1}\" stroke=\"#{hex}\" stroke-width=\"0.8\" stroke-opacity=\"0.65\"/>"));
            }
        }

        static void AppendEndCap(StringBuilder sb, Func<double, double, (double X, double Y)> toScreen, double sMm, double v)
        {
            var a = toScreen(sMm, 0);
            var b = toScreen(sMm, v);
            var (r, g, bl) = SectionCutDiagramStyle.CurveStrokeRgb(v);
            sb.AppendLine(FormattableString.Invariant(
                $"<line x1=\"{a.X:F1}\" y1=\"{a.Y:F1}\" x2=\"{b.X:F1}\" y2=\"{b.Y:F1}\" stroke=\"#{r:X2}{g:X2}{bl:X2}\" stroke-width=\"1.5\"/>"));
        }

        static void AppendRebar(StringBuilder sb, Func<double, double, (double X, double Y)> toScreen,
            SectionCutExportArgs args, CutRebarMarker r, bool onCurve, bool horizontal)
        {
            double sMm = r.S * 1000.0;
            var basePt = toScreen(sMm, 0);
            if (!onCurve)
            {
                sb.AppendLine(FormattableString.Invariant(
                    $"<circle cx=\"{basePt.X:F1}\" cy=\"{basePt.Y:F1}\" r=\"{RebarHandleR}\" fill=\"#666666\" fill-opacity=\"0.55\" stroke=\"none\"/>"));
                if (r.Num is int num)
                {
                    var lp = horizontal
                        ? (basePt.X + 4, basePt.Y + RebarHandleR + 10)
                        : (basePt.X + RebarHandleR + 4, basePt.Y + 8);
                    sb.AppendLine(FormattableString.Invariant(
                        $"<text x=\"{lp.Item1:F1}\" y=\"{lp.Item2:F1}\" font-size=\"10\" font-family=\"Segoe UI\" fill=\"#666666\">№{num}</text>"));
                }
                return;
            }

            double val = args.RebarDisplayValue(r);
            double len = args.RebarLengthPx(r);
            int sign = val < 0 ? -1 : 1;
            var endPt = horizontal
                ? (basePt.X, basePt.Y - sign * len)
                : (basePt.X + sign * len, basePt.Y);
            var (cr, cg, cb) = SectionCutDiagramStyle.CurveStrokeRgb(val);
            string hex = FormattableString.Invariant($"{cr:X2}{cg:X2}{cb:X2}");
            sb.AppendLine(FormattableString.Invariant(
                $"<line x1=\"{basePt.X:F1}\" y1=\"{basePt.Y:F1}\" x2=\"{endPt.Item1:F1}\" y2=\"{endPt.Item2:F1}\" stroke=\"#{hex}\" stroke-width=\"2.5\" stroke-linecap=\"round\"/>"));
            sb.AppendLine(FormattableString.Invariant(
                $"<circle cx=\"{basePt.X:F1}\" cy=\"{basePt.Y:F1}\" r=\"{RebarHandleR}\" fill=\"#{hex}\" stroke=\"#{hex}\" stroke-width=\"1\"/>"));
            sb.AppendLine(FormattableString.Invariant(
                $"<circle cx=\"{endPt.Item1:F1}\" cy=\"{endPt.Item2:F1}\" r=\"{RebarHandleR}\" fill=\"white\" stroke=\"#{hex}\" stroke-width=\"1.5\"/>"));
            string label = Esc(args.FormatRebarLabel(val));
            var labelPt = horizontal
                ? (endPt.Item1 + 6, endPt.Item2 + 12)
                : (endPt.Item1 + 6, endPt.Item2 + 14);
            sb.AppendLine(FormattableString.Invariant(
                $"<text x=\"{labelPt.Item1:F1}\" y=\"{labelPt.Item2:F1}\" font-size=\"10\" font-family=\"Segoe UI\">{label}</text>"));
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

        static void CollectValueRange(SectionCutResult result, SectionPlotMode mode, double? epsCu,
            out double vMin, out double vMax, out double vAbsMax)
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
            vAbsMax = Math.Max(Math.Abs(vMin), Math.Abs(vMax));
            if (vAbsMax < 1e-12) vAbsMax = 1;
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

        static string Esc(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        static double Distance((double X, double Y) a, (double X, double Y) b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
