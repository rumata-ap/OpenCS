using CScore;
using CScore.Fire;
using CScore.Fire.Entities;
using OpenCS.Utilites;
using System.Globalization;
using System.Windows;

namespace OpenCS.ViewModels;

/// <summary>
/// σ / ε R-проверки огнестойкости в виде <see cref="SectionPlotVM"/> — как у обычной задачи НДС:
/// фибры — треугольники T3 теплового расчёта (σ с γbt по температуре, растянутый бетон не работает по
/// п. 8.42 СП 468), стержни — с γst, поверх — контуры областей сечения.
/// </summary>
internal static class FireSectionStatePlotBuilder
{
    public static SectionPlotVM Build(SectionPlotMode mode, FireFiberSection fiber, FireThermalResult thermal,
        CrossSection section, Kurvature k, CalcType calc, CalcSettings settings)
    {
        var inv = CultureInfo.InvariantCulture;
        var mesh = thermal.MeshInfo.Mesh;
        var nodeT = fiber.NodeTemperatures;
        bool stress = mode == SectionPlotMode.Stress;

        double Eps(double x, double y) => k.e0 + k.ky * y + k.kz * x;

        var concrete = new List<FiberDrawData>(fiber.ConcreteElements.Count);
        for (int e = 0; e < mesh.Elements.Length && e < fiber.ConcreteElements.Count; e++)
        {
            var c = fiber.ConcreteElements[e];
            int[] tri = mesh.Elements[e];

            double eps = Eps(c.Cx, c.Cy);
            double sig = eps >= 0.0 ? 0.0 : fiber.ConcreteStress(c, eps, calc) / 1000.0;

            var verts = new Point[tri.Length];
            var vertVals = new double[tri.Length];
            var gMin = (pt: new Point(), val: double.MaxValue);
            var gMax = (pt: new Point(), val: double.MinValue);
            for (int i = 0; i < tri.Length; i++)
            {
                int n = tri[i];
                double x = mesh.X[n], y = mesh.Y[n];
                verts[i] = new Point(x * 1000, y * 1000);
                double epsV = Eps(x, y);
                double v = !stress ? epsV
                    : epsV >= 0.0 ? 0.0
                    : fiber.ConcreteStress(c.Material, epsV, calc, fiber.GammaBtAt(nodeT[n])) / 1000.0;
                vertVals[i] = v;
                if (v < gMin.val) gMin = (verts[i], v);
                if (v > gMax.val) gMax = (verts[i], v);
            }

            double aMm2 = c.Area * 1e6;
            string tip = string.Format(inv,
                "{0}\nx={1:F1} мм  y={2:F1} мм\nσ = {3:+0.0;-0.0} МПа{4}\nε = {5:+0.00000;-0.00000}\nA = {6:F0} мм²\nT = {7:F0} °C, γbt = {8:F3}",
                c.Material.Tag, c.Cx * 1000, c.Cy * 1000, sig,
                eps >= 0.0 ? " (растянутый бетон не учитывается)" : "",
                eps, aMm2, c.Temperature, c.GammaBt);
            concrete.Add(new FiberDrawData(verts, new Point(c.Cx * 1000, c.Cy * 1000), stress ? sig : eps,
                false, tip, sig, eps, aMm2)
            {
                GradientMin = gMin,
                GradientMax = gMax,
                VertexValues = vertVals
            });
        }

        var contours = new List<NoMeshAreaDrawData>();
        var rebarGroups = new List<(double X, double Y, string Tag, Fiber Fiber)>();
        foreach (var (area, _) in section.EnumerateAreas(k))
        {
            if (area.Category == AreaCategory.Region && area.Hull != null)
            {
                var holes = area.Holes.Select(h => (IReadOnlyList<Point>)ToPointsMm(h.X, h.Y)).ToList();
                contours.Add(new NoMeshAreaDrawData(ToPointsMm(area.Hull.X, area.Hull.Y), holes, [],
                    $"{area.Tag} (контур)"));
            }
            foreach (var f in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
                rebarGroups.Add((f.X, f.Y, area.Tag, f));
        }

        var rebar = new List<RebarDrawData>(fiber.RebarElements.Count);
        foreach (var r in fiber.RebarElements)
        {
            double eps = Eps(r.X, r.Y);
            double sig = fiber.RebarStress(r, eps, calc) / 1000.0;
            double aMm2 = r.Area * 1e6;
            string group = rebarGroups
                .Where(g => Math.Abs(g.X - r.X) < 1e-6 && Math.Abs(g.Y - r.Y) < 1e-6)
                .Select(g => g.Tag).FirstOrDefault() ?? "";
            string tip = string.Format(inv,
                "{0} ⌀{1:F0} мм\nx={2:F1}  y={3:F1} мм\nσ = {4:+0.0;-0.0} МПа\nε = {5:+0.00000;-0.00000}\nA = {6:F0} мм²\nT = {7:F0} °C, γst = {8:F3}",
                group.Length > 0 ? group : r.Material.Tag, r.Diameter * 1000, r.X * 1000, r.Y * 1000,
                sig, eps, aMm2, r.Temperature, r.GammaSt);
            rebar.Add(new RebarDrawData(new Point(r.X * 1000, r.Y * 1000), r.Diameter / 2.0 * 1000,
                stress ? sig : eps, tip, sig, eps, aMm2, group));
        }

        return new SectionPlotVM(mode, k, concrete, contours, rebar, settings);
    }

    static IReadOnlyList<Point> ToPointsMm(IList<double> xs, IList<double> ys)
    {
        var pts = new List<Point>(xs.Count);
        for (int i = 0; i < xs.Count; i++)
            pts.Add(new Point(xs[i] * 1000, ys[i] * 1000));
        return pts;
    }
}
