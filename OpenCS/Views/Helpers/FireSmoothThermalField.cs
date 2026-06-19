using CSfea.Thermal;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace OpenCS.Views.Helpers;

/// <summary>Плавное T3-поле температуры (барицентрическая подсетка, координаты мм).</summary>
public static class FireSmoothThermalField
{
    const double MmPerM = 1000.0;
    static readonly Pen s_edgePen = new(Brushes.Transparent, 0);

    public static DrawingGroup? BuildT3(HeatMesh mesh, double[] nodalT)
    {
        if (mesh.Elements.Length == 0 || nodalT.Length != mesh.NNodes)
            return null;

        double vmin = nodalT.Min();
        double vmax = nodalT.Max();
        if (vmax - vmin < 1e-9)
        {
            vmin -= 0.5;
            vmax += 0.5;
        }

        var dg = new DrawingGroup();
        using (var dc = dg.Open())
        {
            var verts = new Point[3];
            var vals = new double[3];

            foreach (var el in mesh.Elements)
            {
                if (el.Length != 3)
                    continue;

                for (int i = 0; i < 3; i++)
                {
                    int ni = el[i];
                    verts[i] = new Point(mesh.X[ni] * MmPerM, mesh.Y[ni] * MmPerM);
                    vals[i] = nodalT[ni];
                }

                FireThermalMeshRenderer.DrawSmoothTriangle(
                    dc, verts, vals, vmin, vmax, static p => p, s_edgePen);
            }
        }

        dg.Freeze();
        return dg;
    }

    /// <summary>Построение с периодической отдачей UI-потока для анимации ожидания.</summary>
    public static async Task<DrawingGroup?> BuildT3Async(
        HeatMesh mesh,
        double[] nodalT,
        Dispatcher dispatcher,
        int yieldEvery = 24)
    {
        if (mesh.Elements.Length == 0 || nodalT.Length != mesh.NNodes)
            return null;

        double vmin = nodalT.Min();
        double vmax = nodalT.Max();
        if (vmax - vmin < 1e-9)
        {
            vmin -= 0.5;
            vmax += 0.5;
        }

        var dg = new DrawingGroup();
        using (var dc = dg.Open())
        {
            var verts = new Point[3];
            var vals = new double[3];
            int drawn = 0;

            foreach (var el in mesh.Elements)
            {
                if (el.Length != 3)
                    continue;

                for (int i = 0; i < 3; i++)
                {
                    int ni = el[i];
                    verts[i] = new Point(mesh.X[ni] * MmPerM, mesh.Y[ni] * MmPerM);
                    vals[i] = nodalT[ni];
                }

                FireThermalMeshRenderer.DrawSmoothTriangle(
                    dc, verts, vals, vmin, vmax, static p => p, s_edgePen);

                drawn++;
                if (drawn % yieldEvery == 0)
                    await dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render);
            }
        }

        dg.Freeze();
        return dg;
    }
}
