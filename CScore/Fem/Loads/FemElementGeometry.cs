using CScore.Planar;

namespace CScore.Fem.Loads;

/// <summary>
/// Геометрия КЭ сетки для переноса нагрузок: узлы в порядке хранения и обход контура пластины. Четырёхугольник
/// хранится «1 2 4 3» (как в ЛИРЕ/SCAD) — контур 1 → 2 → 4 → 3; треугольник — по порядку.
/// </summary>
public sealed class FemElementGeometry
{
    /// <summary>Теги узлов в порядке хранения.</summary>
    public IReadOnlyList<string> NodeTags { get; }
    /// <summary>Координаты узлов в порядке хранения, м.</summary>
    public IReadOnlyList<PlanarVector3> Points { get; }
    /// <summary>Индексы узлов хранения в порядке обхода контура (пластина) или [0, 1] (стержень).</summary>
    public IReadOnlyList<int> Contour { get; }
    /// <summary>Стержень (2 узла).</summary>
    public bool IsBar => Points.Count == 2;
    /// <summary>Пластина (3 или 4 узла).</summary>
    public bool IsShell => Points.Count is 3 or 4;

    FemElementGeometry(IReadOnlyList<string> tags, IReadOnlyList<PlanarVector3> points)
    {
        NodeTags = tags;
        Points = points;
        Contour = points.Count == 4 ? [0, 1, 3, 2] : Enumerable.Range(0, points.Count).ToArray();
    }

    /// <summary>Геометрия КЭ; null — неизвестный узел или не 2–4 узла.</summary>
    public static FemElementGeometry? Of(FemElement element, IReadOnlyDictionary<string, FemMeshNode> nodesByTag)
    {
        int[] ids;
        try { ids = System.Text.Json.JsonSerializer.Deserialize<int[]>(element.NodeIdsJson) ?? []; }
        catch (System.Text.Json.JsonException) { return null; }
        if (ids.Length is < 2 or > 4) return null;
        var tags = new string[ids.Length];
        var points = new PlanarVector3[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            tags[i] = ids[i].ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!nodesByTag.TryGetValue(tags[i], out var n)) return null;
            points[i] = new PlanarVector3(n.X, n.Y, n.Z);
        }
        return new FemElementGeometry(tags, points);
    }

    /// <summary>Длина стержня, м.</summary>
    public double Length => (Points[1] - Points[0]).Length;

    /// <summary>Центр: среднее узлов.</summary>
    public PlanarVector3 Centroid => Points.Aggregate(PlanarVector3.Zero, (s, p) => s + p) * (1.0 / Points.Count);

    /// <summary>
    /// Местные оси пластины (как у SCAD/ЛИРЫ): X1 — от узла 1 к узлу 2, Z1 — нормаль по обходу контура,
    /// Y1 = Z1 × X1. Угол осей выдачи усилий (<see cref="FemElement.LocalAxisAngleDeg"/>) сюда не входит.
    /// </summary>
    public (PlanarVector3 X, PlanarVector3 Y, PlanarVector3 Z) ShellFrame()
    {
        var p0 = Points[Contour[0]];
        var x = (Points[Contour[1]] - p0).Normalize();
        var z = (Points[Contour[1]] - p0).Cross(Points[Contour[^1]] - p0).Normalize();
        return (x, z.Cross(x).Normalize(), z);
    }

    /// <summary>Площадь пластины, м².</summary>
    public double Area => ShellNodeWeights().Sum();

    /// <summary>
    /// ∫Nᵢ dA по узлам хранения: Q4 — Гаусс 2 × 2 по билинейным функциям контура, T3 — A/3.
    /// </summary>
    public double[] ShellNodeWeights()
    {
        var w = new double[Points.Count];
        foreach (var (n, j) in Quadrature())
            for (int k = 0; k < w.Length; k++) w[k] += n[k] * j;
        return w;
    }

    /// <summary>∫NᵢNⱼ dA по узлам хранения (согласованная матрица для интенсивности, заданной в узлах).</summary>
    public double[,] ShellConsistentMatrix()
    {
        int c = Points.Count;
        var m = new double[c, c];
        foreach (var (n, j) in Quadrature())
            for (int a = 0; a < c; a++)
                for (int b = 0; b < c; b++) m[a, b] += n[a] * n[b] * j;
        return m;
    }

    /// <summary>
    /// Функции формы пластины в точке (по узлам хранения); точка проецируется на плоскость КЭ. null — точка вне КЭ
    /// (допуск 1 % размера) или КЭ вырожден.
    /// </summary>
    public double[]? ShellShapeAt(PlanarVector3 point)
    {
        var (ex, ey, _) = ShellFrame();
        var p0 = Points[Contour[0]];
        (double U, double V) Local(PlanarVector3 p) => ((p - p0).Dot(ex), (p - p0).Dot(ey));
        var q = Local(point);
        var c = Contour.Select(i => Local(Points[i])).ToArray();
        var result = new double[Points.Count];
        const double tol = 0.01;
        if (c.Length == 3)
        {
            double det = (c[1].U - c[0].U) * (c[2].V - c[0].V) - (c[2].U - c[0].U) * (c[1].V - c[0].V);
            if (Math.Abs(det) < 1e-14) return null;
            double l1 = ((q.U - c[0].U) * (c[2].V - c[0].V) - (c[2].U - c[0].U) * (q.V - c[0].V)) / det;
            double l2 = ((c[1].U - c[0].U) * (q.V - c[0].V) - (q.U - c[0].U) * (c[1].V - c[0].V)) / det;
            double[] l = [1 - l1 - l2, l1, l2];
            if (l.Any(v => v < -tol)) return null;
            for (int k = 0; k < 3; k++) result[Contour[k]] = l[k];
            return result;
        }

        // Q4: обратное билинейное отображение Ньютоном.
        double xi = 0, eta = 0;
        for (int it = 0; it < 30; it++)
        {
            var n = Bilinear(xi, eta, out var dXi, out var dEta);
            double u = 0, v = 0, uXi = 0, vXi = 0, uEta = 0, vEta = 0;
            for (int k = 0; k < 4; k++)
            {
                u += n[k] * c[k].U; v += n[k] * c[k].V;
                uXi += dXi[k] * c[k].U; vXi += dXi[k] * c[k].V;
                uEta += dEta[k] * c[k].U; vEta += dEta[k] * c[k].V;
            }
            double det = uXi * vEta - uEta * vXi;
            if (Math.Abs(det) < 1e-14) return null;
            double ru = q.U - u, rv = q.V - v;
            double dx = (vEta * ru - uEta * rv) / det, dy = (-vXi * ru + uXi * rv) / det;
            xi += dx; eta += dy;
            if (Math.Abs(dx) + Math.Abs(dy) < 1e-12) break;
        }
        if (Math.Abs(xi) > 1 + 2 * tol || Math.Abs(eta) > 1 + 2 * tol) return null;
        var shape = Bilinear(xi, eta, out _, out _);
        for (int k = 0; k < 4; k++) result[Contour[k]] = shape[k];
        return result;
    }

    /// <summary>Точки интегрирования: функции формы по узлам хранения и вес·якобиан.</summary>
    IEnumerable<(double[] N, double WJ)> Quadrature()
    {
        if (Points.Count == 3)
        {
            double area = 0.5 * (Points[1] - Points[0]).Cross(Points[2] - Points[0]).Length;
            // Три точки в серединах сторон — точно для квадратичных функций (∫NᵢNⱼ).
            foreach (var l in new[] { new[] { 0.5, 0.5, 0 }, new[] { 0, 0.5, 0.5 }, new[] { 0.5, 0, 0.5 } })
                yield return (l, area / 3);
            yield break;
        }
        double g = 1 / Math.Sqrt(3);
        foreach (double gx in (double[])[-g, g])
            foreach (double gy in (double[])[-g, g])
            {
                var n = Bilinear(gx, gy, out var dXi, out var dEta);
                var dx = PlanarVector3.Zero;
                var dy = PlanarVector3.Zero;
                for (int k = 0; k < 4; k++)
                {
                    dx += Points[Contour[k]] * dXi[k];
                    dy += Points[Contour[k]] * dEta[k];
                }
                var stored = new double[4];
                for (int k = 0; k < 4; k++) stored[Contour[k]] = n[k];
                yield return (stored, dx.Cross(dy).Length);
            }
    }

    /// <summary>Билинейные функции формы по контуру (−1,−1), (1,−1), (1,1), (−1,1) и их производные.</summary>
    static double[] Bilinear(double xi, double eta, out double[] dXi, out double[] dEta)
    {
        double[] sx = [-1, 1, 1, -1], sy = [-1, -1, 1, 1];
        var n = new double[4];
        dXi = new double[4];
        dEta = new double[4];
        for (int k = 0; k < 4; k++)
        {
            n[k] = 0.25 * (1 + sx[k] * xi) * (1 + sy[k] * eta);
            dXi[k] = 0.25 * sx[k] * (1 + sy[k] * eta);
            dEta[k] = 0.25 * sy[k] * (1 + sx[k] * xi);
        }
        return n;
    }
}
