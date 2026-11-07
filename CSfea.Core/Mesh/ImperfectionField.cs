namespace CSfea.Core;

/// <summary>
/// Начальное несовершенство схемы — поэлементные начальные перемещения u₀ (глобальные оси): стержень — 12 DOF концов
/// (поступательные и повороты, кубика Эрмита: парабола погиби представляется точно), оболочка — 3 поступательных на
/// узел. Разрыв u₀ между КЭ допустим (эквивалентные силы, а не совместная геометрия). Правая часть второго порядка —
/// −g, g_e = K_σ,e(N)·u₀,e (<see cref="StructuralMesh.ImperfectionForce"/>); напряжений u₀ не создаёт, усилия КЭ —
/// по дополнительным перемещениям u относительно несовершенной оси.
/// </summary>
public sealed class ImperfectionField
{
    public ImperfectionField(int beams, int shells)
    {
        Beam = new double[beams][];
        BeamInterior = new double[beams][];
        Shell = new double[shells][];
    }

    /// <summary>u₀ стержня (индекс в <see cref="StructuralMesh.Beams"/>), 12 чисел в глобальных осях; null — нет.</summary>
    public double[]?[] Beam { get; }

    /// <summary>
    /// Внутренний член стержня при единичной продольной силе (индекс в <see cref="StructuralMesh.Beams"/>), 12 чисел в
    /// глобальных осях по DOF концов: ĝ = ∫(w₀ − w_Эрмит)′·δw′ dx — поправка к K_σ·u₀ (<see cref="Beam"/>), когда форма
    /// u₀ между концами КЭ не кубика (форма потери устойчивости с дроблением мельче сетки). Умножается на N_e КЭ
    /// (<see cref="StructuralMesh.ImperfectionForceElement"/>); null — нет. Готовится <see cref="Interior"/>.
    /// </summary>
    public double[]?[] BeamInterior { get; }

    /// <summary>u₀ оболочки (индекс в <see cref="StructuralMesh.Shells"/>), 3·n узлов (ux, uy, uz), глобальные оси; null — нет.</summary>
    public double[]?[] Shell { get; }

    /// <summary>Ни у одного КЭ нет u₀.</summary>
    public bool IsEmpty => Beam.All(b => b == null) && BeamInterior.All(b => b == null) && Shell.All(s => s == null);

    /// <summary>Наибольшее по модулю поступательное u₀ (м) по всем КЭ — для отчёта.</summary>
    public double MaxTranslation()
    {
        double m = 0.0;
        foreach (var b in Beam)
            if (b != null)
                for (int k = 0; k < 12; k++)
                    if (k % 6 < 3) m = Math.Max(m, Math.Abs(b[k]));
        foreach (var s in Shell)
            if (s != null)
                foreach (double v in s) m = Math.Max(m, Math.Abs(v));
        return m;
    }

    /// <summary>Проверка размеров под сетку <paramref name="mesh"/>.</summary>
    public void CheckFor(StructuralMesh mesh)
    {
        if (Beam.Length != mesh.Beams.Count || Shell.Length != mesh.Shells.Count)
            throw new ArgumentException($"Несовершенство задано для {Beam.Length} стержней и {Shell.Length} оболочек, " +
                                        $"а в сетке {mesh.Beams.Count} и {mesh.Shells.Count}.");
        for (int i = 0; i < Beam.Length; i++)
            if (Beam[i] is { Length: not 12 })
                throw new ArgumentException($"Несовершенство стержня {i}: нужно 12 чисел, задано {Beam[i]!.Length}.");
        for (int i = 0; i < Beam.Length; i++)
            if (BeamInterior[i] is { Length: not 12 })
                throw new ArgumentException($"Внутренний член несовершенства стержня {i}: нужно 12 чисел, задано {BeamInterior[i]!.Length}.");
        for (int i = 0; i < Shell.Length; i++)
            if (Shell[i] is { } s && s.Length != 3 * mesh.Shells[i].Nodes.Length)
                throw new ArgumentException($"Несовершенство оболочки {i}: нужно {3 * mesh.Shells[i].Nodes.Length} чисел, задано {s.Length}.");
    }

    /// <summary>
    /// Внутренний член стержня <see cref="BeamInterior"/> (12 чисел, глобальные оси, на единичную N): форма u₀ между
    /// концами — кубический сплайн поперечных перемещений через концы и равноотстоящие промежуточные точки
    /// <paramref name="points"/> (по порядку от I к J); краевое условие конца — наклон θ × t по повороту конца из
    /// <paramref name="endI"/>/<paramref name="endJ"/> (6 чисел: перемещения и повороты), у конца с шарниром по изгибу
    /// (<paramref name="hingeI"/>, <paramref name="hingeJ"/>) — нулевая кривизна. ĝ = ∫(w₀ − w_Эрмит)′·δw′ dx, w_Эрмит —
    /// кубика Эрмита по концам (та, что даёт K_σ·u₀); Гаусс 4 точки на интервал сплайна. Продольная составляющая u₀
    /// в член не входит (как в K_σ). Сумма K_σ·u₀ + N·ĝ = N·∫w₀′·δw′ dx — согласованный вектор формы целиком.
    /// </summary>
    public static double[] Interior(double[] xI, double[] xJ, double[] endI, double[] endJ, IReadOnlyList<double[]> points,
        bool hingeI = false, bool hingeJ = false)
    {
        var axis = new[] { xJ[0] - xI[0], xJ[1] - xI[1], xJ[2] - xI[2] };
        double len = Math.Sqrt(axis[0] * axis[0] + axis[1] * axis[1] + axis[2] * axis[2]);
        if (!(len > 0)) throw new ArgumentException("Внутренний член несовершенства: нулевая длина стержня.");
        var t = new[] { axis[0] / len, axis[1] / len, axis[2] / len };
        double[] Transverse(double[] u)
        {
            double a = u[0] * t[0] + u[1] * t[1] + u[2] * t[2];
            return [u[0] - a * t[0], u[1] - a * t[1], u[2] - a * t[2]];
        }
        double[] Slope(double[] end) => Cross([end[3], end[4], end[5]], t);   // θ × t — поперечный наклон конца

        int n = points.Count + 1;                 // интервалов сплайна
        double h = len / n;
        var p = new double[n + 1][];
        p[0] = Transverse(endI);
        for (int k = 0; k < points.Count; k++) p[k + 1] = Transverse(points[k]);
        p[n] = Transverse(endJ);
        var mI = Slope(endI);
        var mJ = Slope(endJ);
        var m = SplineSlopes(p, h, hingeI ? null : mI, hingeJ ? null : mJ);

        ReadOnlySpan<double> gx = [-0.8611363115940526, -0.3399810435848563, 0.3399810435848563, 0.8611363115940526];
        ReadOnlySpan<double> gw = [0.3478548451374538, 0.6521451548625461, 0.6521451548625461, 0.3478548451374538];
        var g = new double[12];
        var delta = new double[3];
        for (int k = 0; k < n; k++)
            for (int q = 0; q < 4; q++)
            {
                double r = 0.5 * (gx[q] + 1.0);              // доля интервала
                double w = 0.5 * h * gw[q];
                double xi = (k + r) * h / len;
                // Производные функций Эрмита интервала сплайна и КЭ по x.
                double d1 = (-6 * r + 6 * r * r) / h, d2 = 1 - 4 * r + 3 * r * r, d3 = (6 * r - 6 * r * r) / h, d4 = -2 * r + 3 * r * r;
                double n1 = (-6 * xi + 6 * xi * xi) / len, n2 = 1 - 4 * xi + 3 * xi * xi, n3 = (6 * xi - 6 * xi * xi) / len, n4 = -2 * xi + 3 * xi * xi;
                for (int c = 0; c < 3; c++)
                    delta[c] = d1 * p[k][c] + d2 * m[k][c] + d3 * p[k + 1][c] + d4 * m[k + 1][c]
                             - (n1 * p[0][c] + n2 * mI[c] + n3 * p[n][c] + n4 * mJ[c]);
                // δw′ = N1′·δuI + N2′·(δθI × t) + N3′·δuJ + N4′·(δθJ × t); (δθ × t)·Δ′ = δθ·(t × Δ′).
                var txd = Cross(t, delta);
                for (int c = 0; c < 3; c++)
                {
                    g[c] += w * n1 * delta[c];
                    g[3 + c] += w * n2 * txd[c];
                    g[6 + c] += w * n3 * delta[c];
                    g[9 + c] += w * n4 * txd[c];
                }
            }
        return g;
    }

    // Наклоны кубического сплайна (по компонентам) в равноотстоящих узлах шага h: наклон конца задан или (null) —
    // нулевая кривизна конца; трёхдиагональная система, прогонка.
    private static double[][] SplineSlopes(double[][] p, double h, double[]? m0, double[]? mn)
    {
        int n = p.Length - 1;
        var result = new double[n + 1][];
        for (int i = 0; i <= n; i++) result[i] = new double[3];
        var a = new double[n + 1];
        var b = new double[n + 1];
        var c1 = new double[n + 1];
        var d = new double[n + 1];
        for (int c = 0; c < 3; c++)
        {
            Array.Clear(a); Array.Clear(b); Array.Clear(c1); Array.Clear(d);
            if (m0 != null) { b[0] = 1; d[0] = m0[c]; }
            else { b[0] = 2; c1[0] = 1; d[0] = 3 * (p[1][c] - p[0][c]) / h; }
            for (int i = 1; i < n; i++)
            {
                a[i] = 1; b[i] = 4; c1[i] = 1;
                d[i] = 3 * (p[i + 1][c] - p[i - 1][c]) / h;
            }
            if (mn != null) { b[n] = 1; d[n] = mn[c]; }
            else { a[n] = 1; b[n] = 2; d[n] = 3 * (p[n][c] - p[n - 1][c]) / h; }
            for (int i = 1; i <= n; i++)
            {
                double f = a[i] / b[i - 1];
                b[i] -= f * c1[i - 1];
                d[i] -= f * d[i - 1];
            }
            result[n][c] = d[n] / b[n];
            for (int i = n - 1; i >= 0; i--) result[i][c] = (d[i] - c1[i] * result[i + 1][c]) / b[i];
        }
        return result;
    }

    private static double[] Cross(double[] a, double[] b) =>
        [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
}
