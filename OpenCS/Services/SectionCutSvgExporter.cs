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
    /// <summary>Пишет эпюру разреза в SVG напрямую по данным SectionCutResult (без обхода WPF Visual Tree).</summary>
    public static class SectionCutSvgExporter
    {
        const int Width = 900, Height = 400, Pad = 40;

        public static void Save(string path, SectionCutResult result, SectionPlotMode mode,
            bool horizontal, bool fillMode, double? epsCu, bool asOnScreen = true)
            => File.WriteAllText(path, Build(result, mode, horizontal, fillMode, epsCu, asOnScreen), Encoding.UTF8);

        public static string Build(SectionCutResult result, SectionPlotMode mode,
            bool horizontal, bool fillMode, double? epsCu, bool asOnScreen = true)
        {
            double lengthMm = Distance(result.Start, result.End) * 1000.0;
            if (lengthMm < 1e-6) lengthMm = 1;

            double vMin = 0, vMax = 0;
            foreach (var seg in result.Segments)
                foreach (var p in seg.Points)
                {
                    double? v = mode == SectionPlotMode.Stress ? p.Sig : p.Eps;
                    if (v == null) continue;
                    if (v < vMin) vMin = v.Value;
                    if (v > vMax) vMax = v.Value;
                }
            if (epsCu is { } ec && mode == SectionPlotMode.Strain)
            {
                if (ec < vMin) vMin = ec;
                if (ec > vMax) vMax = ec;
            }
            double vAbsMax = Math.Max(Math.Abs(vMin), Math.Abs(vMax));
            if (vAbsMax < 1e-12) vAbsMax = 1;

            double sw = Width - 2 * Pad, sh = Height - 2 * Pad;
            double scaleS = horizontal ? sw / lengthMm : sh / lengthMm;
            double scaleV = (horizontal ? sh : sw) / 2 / vAbsMax;
            double originX = horizontal ? Pad : Pad + sw / 2;
            double originY = horizontal ? Pad + sh / 2 : Pad;

            (double X, double Y) ToScreen(double sMm, double value) => horizontal
                ? (originX + sMm * scaleS, originY - value * scaleV)
                : (originX + value * scaleV, originY + sMm * scaleS);

            string FormatV(double v) => mode == SectionPlotMode.Stress
                ? v.ToString("+0.##;-0.##", CultureInfo.InvariantCulture)
                : v.ToString("+0.#####;-0.#####", CultureInfo.InvariantCulture);

            var sb = new StringBuilder();
            sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Width}\" height=\"{Height}\" viewBox=\"0 0 {Width} {Height}\">");
            sb.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");

            if (asOnScreen)
            {
                sb.AppendLine(FormattableString.Invariant(
                    $"<rect x=\"{Pad}\" y=\"{Pad}\" width=\"{sw}\" height=\"{sh}\" fill=\"none\" stroke=\"black\" stroke-width=\"1\"/>"));
                string axisS = Loc.S("SectionCutAxisS");
                string axisV = mode == SectionPlotMode.Stress
                    ? Loc.S("SectionCutAxisSigma") : Loc.S("SectionCutAxisEps");
                sb.AppendLine(FormattableString.Invariant(
                    $"<text x=\"{Pad + sw / 2:F1}\" y=\"{Height - 8}\" text-anchor=\"middle\" font-size=\"11\">{Esc(axisS)}</text>"));
                sb.AppendLine(FormattableString.Invariant(
                    $"<text x=\"12\" y=\"{Pad + sh / 2:F1}\" text-anchor=\"middle\" font-size=\"11\" transform=\"rotate(-90 12 {Pad + sh / 2:F1})\">{Esc(axisV)}</text>"));
            }

            foreach (var seg in result.Segments)
            {
                if (seg.Points.Count < 2) continue;
                var s0 = ToScreen(seg.Points[0].S * 1000.0, 0);
                var s1 = ToScreen(seg.Points[^1].S * 1000.0, 0);
                string dash = seg.AreaIndex == null ? " stroke-dasharray=\"4,3\"" : "";
                string stroke = seg.AreaIndex == null ? "#B0B0B0" : "black";
                sb.AppendLine(FormattableString.Invariant(
                    $"<line x1=\"{s0.X:F1}\" y1=\"{s0.Y:F1}\" x2=\"{s1.X:F1}\" y2=\"{s1.Y:F1}\" stroke=\"{stroke}\" stroke-width=\"1.5\"{dash}/>"));
            }

            foreach (var seg in result.Segments)
            {
                if (seg.AreaIndex == null || seg.Points.Count < 2) continue;
                var pts = seg.Points.Where(p => (mode == SectionPlotMode.Stress ? p.Sig : p.Eps) != null).ToList();
                if (pts.Count < 2) continue;

                var vals = pts.Select(p => (mode == SectionPlotMode.Stress ? p.Sig : p.Eps)!.Value).ToList();

                if (fillMode && asOnScreen)
                {
                    var pathPts = pts.Select(p =>
                    {
                        double v = (mode == SectionPlotMode.Stress ? p.Sig : p.Eps)!.Value;
                        return ToScreen(p.S * 1000.0, v);
                    }).ToList();
                    string polyline = string.Join(" ", pathPts.Select(pt => FormattableString.Invariant($"{pt.X:F1},{pt.Y:F1}")));
                    var baseStart = ToScreen(pts[0].S * 1000.0, 0);
                    var baseEnd = ToScreen(pts[^1].S * 1000.0, 0);
                    string fillPts = FormattableString.Invariant($"{baseStart.X:F1},{baseStart.Y:F1} ") + polyline +
                                      FormattableString.Invariant($" {baseEnd.X:F1},{baseEnd.Y:F1}");
                    var (fr, fg, fb) = SectionCutDiagramStyle.CurveStrokeRgb(vals.Average());
                    sb.AppendLine(FormattableString.Invariant(
                        $"<polygon points=\"{fillPts}\" fill=\"#{fr:X2}{fg:X2}{fb:X2}\" fill-opacity=\"0.25\" stroke=\"none\"/>"));
                }

                foreach (var part in SectionCutDiagramStyle.SplitBySign(vals))
                {
                    var slice = new List<(double X, double Y)>();
                    for (int i = part.Start; i < part.EndExclusive; i++)
                        slice.Add(ToScreen(pts[i].S * 1000.0, vals[i]));
                    if (slice.Count < 2) continue;
                    var (r, g, b) = SectionCutDiagramStyle.CurveStrokeRgb(vals[part.Start]);
                    string stroke = FormattableString.Invariant($"#{r:X2}{g:X2}{b:X2}");
                    string polyline = string.Join(" ", slice.Select(pt => FormattableString.Invariant($"{pt.X:F1},{pt.Y:F1}")));
                    sb.AppendLine($"<polyline points=\"{polyline}\" fill=\"none\" stroke=\"{stroke}\" stroke-width=\"1.5\"/>");
                }

                var t0 = ToScreen(pts[0].S * 1000.0, vals[0]);
                var t1 = ToScreen(pts[^1].S * 1000.0, vals[^1]);
                sb.AppendLine(FormattableString.Invariant(
                    $"<text x=\"{t0.X + 4:F1}\" y=\"{t0.Y - 4:F1}\" font-size=\"10\">{FormatV(vals[0])}</text>"));
                sb.AppendLine(FormattableString.Invariant(
                    $"<text x=\"{t1.X + 4:F1}\" y=\"{t1.Y - 4:F1}\" font-size=\"10\">{FormatV(vals[^1])}</text>"));
            }

            if (mode == SectionPlotMode.Strain && epsCu.HasValue)
            {
                var p0 = ToScreen(0, epsCu.Value);
                var p1 = ToScreen(lengthMm, epsCu.Value);
                sb.AppendLine(FormattableString.Invariant(
                    $"<line x1=\"{p0.X:F1}\" y1=\"{p0.Y:F1}\" x2=\"{p1.X:F1}\" y2=\"{p1.Y:F1}\" stroke=\"purple\" stroke-width=\"1.2\" stroke-dasharray=\"5,3\"/>"));
            }

            if (asOnScreen)
            {
                foreach (var r in result.Rebars)
                {
                    double v = mode == SectionPlotMode.Stress ? r.Sig : r.Eps;
                    var sc = ToScreen(r.S * 1000.0, 0);
                    sb.AppendLine(FormattableString.Invariant(
                        $"<circle cx=\"{sc.X:F1}\" cy=\"{sc.Y:F1}\" r=\"4\" fill=\"white\" stroke=\"black\" stroke-width=\"1\"/>"));
                }
            }

            sb.AppendLine("</svg>");
            return sb.ToString();
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
