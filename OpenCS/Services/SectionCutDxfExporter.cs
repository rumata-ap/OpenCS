using CScore;
using OpenCS.ViewModels;
using netDxf;
using netDxf.Entities;
using System;
using System.Globalization;
using System.Linq;

namespace OpenCS.Services
{
    /// <summary>Пишет эпюру разреза в DXF как редактируемые примитивы AutoCAD (LwPolyline/Line).</summary>
    public static class SectionCutDxfExporter
    {
        const double ScaleMmPerUnitValue = 40.0; // 1 единица σ[МПа]/ε → сколько "мм" отложить на чертеже
        const double TextHeight = 2.5;

        public static void Save(string path, SectionCutResult result, SectionPlotMode mode,
            bool horizontal, double? epsCu, bool asOnScreen = true)
        {
            var doc = new DxfDocument();
            double lengthMm = Distance(result.Start, result.End) * 1000.0;

            (double X, double Y) ToDrawing(double sMm, double value) => horizontal
                ? (sMm, -value * ScaleMmPerUnitValue)
                : (value * ScaleMmPerUnitValue, -sMm);

            string FormatV(double v) => mode == SectionPlotMode.Stress
                ? v.ToString("+0.##;-0.##", CultureInfo.InvariantCulture)
                : v.ToString("+0.#####;-0.#####", CultureInfo.InvariantCulture);

            foreach (var seg in result.Segments)
            {
                if (seg.Points.Count < 2) continue;
                var s0 = ToDrawing(seg.Points[0].S * 1000.0, 0);
                var s1 = ToDrawing(seg.Points[^1].S * 1000.0, 0);
                var line = new Line(new Vector3(s0.X, s0.Y, 0), new Vector3(s1.X, s1.Y, 0));
                if (seg.AreaIndex == null) line.Linetype = netDxf.Tables.Linetype.Dashed;
                doc.Entities.Add(line);
            }

            foreach (var seg in result.Segments)
            {
                if (seg.AreaIndex == null || seg.Points.Count < 2) continue;
                var pts = seg.Points.Where(p => (mode == SectionPlotMode.Stress ? p.Sig : p.Eps) != null).ToList();
                if (pts.Count < 2) continue;

                var vals = pts.Select(p => (mode == SectionPlotMode.Stress ? p.Sig : p.Eps)!.Value).ToList();

                foreach (var part in SectionCutDiagramStyle.SplitBySign(vals))
                {
                    var vertices = new System.Collections.Generic.List<Polyline2DVertex>();
                    for (int i = part.Start; i < part.EndExclusive; i++)
                    {
                        var (x, y) = ToDrawing(pts[i].S * 1000.0, vals[i]);
                        vertices.Add(new Polyline2DVertex(x, y));
                    }
                    if (vertices.Count < 2) continue;
                    var poly = new Polyline2D(vertices)
                    {
                        Color = SectionCutDiagramStyle.CurveIsPositive(vals[part.Start])
                            ? AciColor.Blue
                            : AciColor.Red
                    };
                    doc.Entities.Add(poly);
                }

                var (x0, y0) = ToDrawing(pts[0].S * 1000.0, vals[0]);
                var (x1, y1) = ToDrawing(pts[^1].S * 1000.0, vals[^1]);
                doc.Entities.Add(new Text(FormatV(vals[0]), new Vector3(x0, y0, 0), TextHeight));
                doc.Entities.Add(new Text(FormatV(vals[^1]), new Vector3(x1, y1, 0), TextHeight));
            }

            if (mode == SectionPlotMode.Strain && epsCu.HasValue)
            {
                var p0 = ToDrawing(0, epsCu.Value);
                var p1 = ToDrawing(lengthMm, epsCu.Value);
                var line = new Line(new Vector3(p0.X, p0.Y, 0), new Vector3(p1.X, p1.Y, 0))
                {
                    Linetype = netDxf.Tables.Linetype.Dashed
                };
                doc.Entities.Add(line);
            }

            if (asOnScreen)
            {
                foreach (var r in result.Rebars)
                {
                    double v = mode == SectionPlotMode.Stress ? r.Sig : r.Eps;
                    var (x, y) = ToDrawing(r.S * 1000.0, 0);
                    doc.Entities.Add(new Circle(new Vector3(x, y, 0), 2.0));
                }
            }

            doc.Save(path);
        }

        static double Distance((double X, double Y) a, (double X, double Y) b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
