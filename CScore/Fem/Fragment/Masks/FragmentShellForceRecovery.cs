using CScore.Planar;
using CScore.Submodel;

namespace CScore.Fem.Fragment.Masks;

/// <summary>
/// Усилия стены в точке кромки (спека разд. 4.3): действие на линию по усилиям КЭ и восстановление по патчу центров КЭ
/// (линейный МНК; центр КЭ — суперсходящаяся точка усилий).
/// </summary>
public static class FragmentShellForceRecovery
{
    /// <summary>
    /// Действие на единицу длины линии с нормалью <paramref name="n"/> (в плоскости КЭ) со стороны, куда смотрит
    /// <paramref name="n"/>: сила <c>N·n + (Q·n)·ζ</c>, момент <c>ζ × (M·n)</c>, общие оси.
    /// </summary>
    public static Dof6 Traction(FragmentShellForces f, PlanarVector3 n)
    {
        var ax = f.AxisX.Normalize();
        var ay = f.AxisY.Normalize();
        var az = ax.Cross(ay).Normalize();
        double nx = n.Dot(ax), ny = n.Dot(ay);
        var v = f.Values;
        var force = ax * (v[0] * nx + v[2] * ny) + ay * (v[2] * nx + v[1] * ny) + az * (v[6] * nx + v[7] * ny);
        var mn = ax * (v[3] * nx + v[5] * ny) + ay * (v[5] * nx + v[4] * ny);
        return Dof6.FromParts(force, az.Cross(mn));
    }

    /// <summary>
    /// Действие в точке по патчу: линейная аппроксимация <c>a + b·u + c·v</c> по центрам КЭ патча (оси стены с началом в
    /// точке), значение — <c>a</c>. Вырожденный патч (&lt; 3 центров или на одной прямой) — линейная интерполяция по
    /// нормали между ближайшими центрами по обе стороны, иначе значение КЭ, содержащего точку
    /// (<paramref name="degenerate"/> = true). null — у КЭ, содержащего точку, нет усилий.
    /// </summary>
    public static Dof6? Recover(FragmentWallCutGeometry g, FragmentWallPoint point, Func<string, Dof6?> traction,
        out bool degenerate)
    {
        degenerate = false;
        if (point.Containing.Count == 0 || traction(point.Containing[0].E.Tag) is not { } own) return null;
        var e1 = g.Axis;
        var e2 = g.CutNormal;
        var data = new List<(double U, double V, Dof6 T)>();
        foreach (var e in g.Patch(point))
            if (traction(e.Tag) is { } t)
                data.Add(((e.Centroid - point.X).Dot(e1), (e.Centroid - point.X).Dot(e2), t));

        if (Fit(data) is { } fitted) return fitted;
        degenerate = true;
        var below = data.Where(d => d.V < -1e-9).OrderBy(d => d.U * d.U + d.V * d.V).FirstOrDefault();
        var above = data.Where(d => d.V > 1e-9).OrderBy(d => d.U * d.U + d.V * d.V).FirstOrDefault();
        if (below.T != null && above.T != null)
            return below.T + (above.T - below.T).Scale(-below.V / (above.V - below.V));
        return own;
    }

    /// <summary>МНК по [1, u, v]; null — меньше трёх точек или матрица нормальных уравнений вырождена.</summary>
    static Dof6? Fit(IReadOnlyList<(double U, double V, Dof6 T)> data)
    {
        if (data.Count < 3) return null;
        double h2 = data.Average(d => d.U * d.U + d.V * d.V);
        if (h2 <= 0) return null;
        var a = new double[3, 3];
        foreach (var (u, v, _) in data)
        {
            double[] r = [1, u, v];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++) a[i, j] += r[i] * r[j];
        }
        double det = Det(a);
        if (Math.Abs(det) < 1e-8 * Math.Pow(data.Count, 3) * h2 * h2) return null;
        var c = new double[6];
        for (int k = 0; k < 6; k++)
        {
            double b0 = 0, b1 = 0, b2 = 0;
            foreach (var (u, v, t) in data) { b0 += t[k]; b1 += u * t[k]; b2 += v * t[k]; }
            // Правило Крамера для первой неизвестной (значение в точке).
            var m = (double[,])a.Clone();
            m[0, 0] = b0; m[1, 0] = b1; m[2, 0] = b2;
            c[k] = Det(m) / det;
        }
        return new Dof6(c[0], c[1], c[2], c[3], c[4], c[5]);
    }

    static double Det(double[,] m) =>
        m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1])
        - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0])
        + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
}
