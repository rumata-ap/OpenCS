using CScore;
using CScore.Fire;
using OpenCS.Utilites;
using CSfea.Thermal;
using System.Globalization;
using System.Windows;

namespace OpenCS.ViewModels;

/// <summary>Построение геометрии для <see cref="FireMeshPlotVM"/>.</summary>
internal static class FireMeshPlotBuilder
{
    public static FireMeshPlotVM CreateTemperaturePlot(FireThermalResult thermal, int initialSnapshot = -1)
        => new(FireMeshPlotMode.Temperature, "T, °C", thermal.TimesMin,
            snap => BuildTemperatureSnapshot(thermal, snap), initialSnapshot);

    public static FireMeshPlotVM CreateGammaPlot(
        FireFiberSection fiber, FireThermalResult thermal, int initialSnapshot = -1)
        => new(FireMeshPlotMode.Gamma, "γ", thermal.TimesMin, snap =>
        {
            fiber.SetSnapshot(snap);
            return BuildScalarFromMesh(thermal, fiber.ConcreteElements, fiber.RebarElements,
                c => c.GammaBt,
                r => Math.Min(r.GammaStC, r.GammaStT),
                v => string.Format(CultureInfo.InvariantCulture, "γ = {0:F4}", v));
        }, initialSnapshot);

    public static FireMeshPlotVM CreateStressPlot(
        FireFiberSection fiber, FireThermalResult thermal, Kurvature k, CalcType calc, int initialSnapshot = -1)
        => new(FireMeshPlotMode.Stress, "σ, МПа", thermal.TimesMin, snap =>
        {
            fiber.SetSnapshot(snap);
            return BuildStressFromMesh(fiber, thermal, k, calc);
        }, initialSnapshot);

    public static FireMeshPlotVM CreateStrainPlot(
        FireFiberSection fiber, FireThermalResult thermal, Kurvature k, int initialSnapshot = -1)
        => new(FireMeshPlotMode.Strain, "ε", thermal.TimesMin, snap =>
        {
            fiber.SetSnapshot(snap);
            return BuildStrainFromMesh(fiber, thermal, k);
        }, initialSnapshot);

    public static FireLineChartVM CreateRebarChart(FireThermalResult thermal)
    {
        var series = new List<FireLineSeries>();
        foreach (var kv in thermal.RebarTemperatureHistory.OrderBy(k => k.Key))
        {
            if (kv.Value.Length != thermal.TimesMin.Length) continue;
            series.Add(new FireLineSeries(
                $"#{kv.Key}",
                thermal.TimesMin,
                kv.Value,
                PickColor(kv.Key)));
        }

        return new FireLineChartVM(
            Loc.S("FireThermal_ChartRebarTitle"),
            Loc.S("FireThermal_ChartTimeX"),
            Loc.S("FireThermal_ChartTempY"),
            series);
    }

    public static (FireLineChartVM Picard, FireLineChartVM Residual) CreateConvergenceCharts(FireThermalResult thermal)
    {
        var log = thermal.ConvergenceLog;
        if (log.Count == 0)
        {
            var empty = new FireLineChartVM("", "", "", []);
            return (empty, empty);
        }

        double[] tMin = log.Select(e => e.Time_s / 60.0).ToArray();
        double[] iters = log.Select(e => (double)e.NPicardIter).ToArray();
        double[] resids = log.Select(e => Math.Max(e.MaxResidualCelsius, 1e-6)).ToArray();

        var picard = new FireLineChartVM(
            Loc.S("FireThermal_ChartPicardTitle"),
            Loc.S("FireThermal_ChartTimeX"),
            Loc.S("FireThermal_ChartPicardY"),
            [new FireLineSeries("Picard", tMin, iters, "#2563EB")]);

        var resid = new FireLineChartVM(
            Loc.S("FireThermal_ChartResidTitle"),
            Loc.S("FireThermal_ChartTimeX"),
            Loc.S("FireThermal_ChartResidY"),
            [new FireLineSeries("max ΔT", tMin, resids, "#DC2626")],
            logY: true);

        return (picard, resid);
    }

    static (IReadOnlyList<FireTriDraw>, IReadOnlyList<FirePointDraw>) BuildTemperatureSnapshot(
        FireThermalResult thermal, int snapIdx)
    {
        snapIdx = ClampSnap(thermal, snapIdx);
        double[] tField = thermal.Snapshots[snapIdx];
        var mesh = thermal.MeshInfo.Mesh;

        var tris = new List<FireTriDraw>(mesh.Elements.Length);
        for (int e = 0; e < mesh.Elements.Length; e++)
        {
            var tri = mesh.Elements[e];
            var verts = new List<Point>(3);
            double tSum = 0;
            for (int i = 0; i < 3; i++)
            {
                int ni = tri[i];
                verts.Add(Mm(mesh.X[ni], mesh.Y[ni]));
                tSum += tField[ni];
            }
            double t = tSum / 3.0;
            double cx = (mesh.X[tri[0]] + mesh.X[tri[1]] + mesh.X[tri[2]]) / 3.0;
            double cy = (mesh.Y[tri[0]] + mesh.Y[tri[1]] + mesh.Y[tri[2]]) / 3.0;
            tris.Add(new FireTriDraw(verts, Mm(cx, cy), t,
                string.Format(CultureInfo.InvariantCulture, "T = {0:F1} °C", t)));
        }

        var pts = new List<FirePointDraw>();
        foreach (var r in thermal.MeshInfo.Rebars)
        {
            double t = ResolveRebarT(thermal, r.Id, snapIdx);
            pts.Add(new FirePointDraw(Mm(r.X, r.Y), RebarMarkerRadiusPx, t,
                string.Format(CultureInfo.InvariantCulture, "#{0}: T = {1:F1} °C", r.Id, t)));
        }
        return (tris, pts);
    }

    static (IReadOnlyList<FireTriDraw>, IReadOnlyList<FirePointDraw>) BuildScalarFromMesh(
        FireThermalResult thermal,
        IReadOnlyList<FireConcreteElement> concrete,
        IReadOnlyList<FireRebarElement> rebars,
        Func<FireConcreteElement, double> concVal,
        Func<FireRebarElement, double> rebarVal,
        Func<double, string> fmt)
    {
        var mesh = thermal.MeshInfo.Mesh;
        var tris = new List<FireTriDraw>(mesh.Elements.Length);
        for (int e = 0; e < mesh.Elements.Length && e < concrete.Count; e++)
        {
            var tri = mesh.Elements[e];
            var c = concrete[e];
            var verts = TriVertsMm(mesh, tri);
            double v = concVal(c);
            tris.Add(new FireTriDraw(verts, Mm(c.Cx, c.Cy), v, fmt(v)));
        }

        var pts = new List<FirePointDraw>();
        foreach (var r in rebars)
        {
            double v = rebarVal(r);
            pts.Add(new FirePointDraw(Mm(r.X, r.Y), Math.Max(1.5, r.Diameter * 500), v, fmt(v)));
        }
        return (tris, pts);
    }

    static (IReadOnlyList<FireTriDraw>, IReadOnlyList<FirePointDraw>) BuildStressFromMesh(
        FireFiberSection fiber, FireThermalResult thermal, Kurvature k, CalcType calc)
    {
        var mesh = thermal.MeshInfo.Mesh;
        var tris = new List<FireTriDraw>(mesh.Elements.Length);
        for (int e = 0; e < mesh.Elements.Length && e < fiber.ConcreteElements.Count; e++)
        {
            var c = fiber.ConcreteElements[e];
            double eps = k.e0 + k.ky * c.Cy + k.kz * c.Cx;
            var d = GetDiagram(c.Material, calc);
            double sig = d.Sig(eps, out _) * c.GammaBt / 1000.0;
            var tri = mesh.Elements[e];
            tris.Add(new FireTriDraw(TriVertsMm(mesh, tri), Mm(c.Cx, c.Cy), sig,
                string.Format(CultureInfo.InvariantCulture, "σ = {0:+0.0;-0.0} МПа", sig)));
        }

        var pts = new List<FirePointDraw>();
        foreach (var r in fiber.RebarElements)
        {
            double eps = k.e0 + k.ky * r.Y + k.kz * r.X;
            var d = GetDiagram(r.Material, calc);
            double gamma = eps < 0 ? r.GammaStC : r.GammaStT;
            double sig = d.Sig(eps, out _) * gamma / 1000.0;
            pts.Add(new FirePointDraw(Mm(r.X, r.Y), Math.Max(1.5, r.Diameter * 500), sig,
                string.Format(CultureInfo.InvariantCulture, "#{0}: σ = {1:+0.0;-0.0} МПа", r.RebarId, sig)));
        }
        return (tris, pts);
    }

    static (IReadOnlyList<FireTriDraw>, IReadOnlyList<FirePointDraw>) BuildStrainFromMesh(
        FireFiberSection fiber, FireThermalResult thermal, Kurvature k)
    {
        var mesh = thermal.MeshInfo.Mesh;
        var tris = new List<FireTriDraw>(mesh.Elements.Length);
        for (int e = 0; e < mesh.Elements.Length && e < fiber.ConcreteElements.Count; e++)
        {
            var c = fiber.ConcreteElements[e];
            double eps = k.e0 + k.ky * c.Cy + k.kz * c.Cx;
            var tri = mesh.Elements[e];
            tris.Add(new FireTriDraw(TriVertsMm(mesh, tri), Mm(c.Cx, c.Cy), eps,
                string.Format(CultureInfo.InvariantCulture, "ε = {0:+0.000000;-0.000000}", eps)));
        }

        var pts = new List<FirePointDraw>();
        foreach (var r in fiber.RebarElements)
        {
            double eps = k.e0 + k.ky * r.Y + k.kz * r.X;
            pts.Add(new FirePointDraw(Mm(r.X, r.Y), Math.Max(1.5, r.Diameter * 500), eps,
                string.Format(CultureInfo.InvariantCulture, "#{0}: ε = {1:+0.000000;-0.000000}", r.RebarId, eps)));
        }
        return (tris, pts);
    }

    static Diagramm GetDiagram(Material mat, CalcType calc)
    {
        var d = mat.GetDiagramms(DiagrammType.L2);
        if (d != null && d.TryGetValue(calc, out var dg)) return dg;
        throw new InvalidOperationException($"Диаграмма {mat.Tag} не найдена.");
    }

    static List<Point> TriVertsMm(HeatMesh mesh, int[] tri)
    {
        var verts = new List<Point>(3);
        for (int i = 0; i < 3; i++)
            verts.Add(Mm(mesh.X[tri[i]], mesh.Y[tri[i]]));
        return verts;
    }

    static int ClampSnap(FireThermalResult thermal, int snapIdx)
    {
        if (thermal.Snapshots.Length == 0) return 0;
        if (snapIdx < 0) return thermal.Snapshots.Length - 1;
        return Math.Clamp(snapIdx, 0, thermal.Snapshots.Length - 1);
    }

    static double ResolveRebarT(FireThermalResult thermal, int rebarId, int snapIdx)
    {
        if (thermal.RebarTemperatureHistory.TryGetValue(rebarId, out var hist) &&
            snapIdx < hist.Length)
            return hist[snapIdx];
        return 20.0;
    }

    static Point Mm(double xM, double yM) => new(xM * 1000, yM * 1000);

    const double RebarMarkerRadiusPx = 6.0;

    static string PickColor(int id) => id switch
    {
        0 => "#2563EB",
        1 => "#DC2626",
        2 => "#16A34A",
        3 => "#9333EA",
        _ => "#CA8A04"
    };
}
