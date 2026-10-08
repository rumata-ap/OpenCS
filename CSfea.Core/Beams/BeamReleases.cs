namespace CSfea.Core;

/// <summary>
/// Шарниры (освобождения) концов стержня: статическая конденсация освобождённых местных DOF в локальной 12×12.
/// Маска КЭ — 12 бит: биты 0–5 — DOF конца i (u, v, w, θx, θy, θz местных осей; усилия N, Qy, Qz, T, My, Mz), биты 6–11
/// — то же конца j (<see cref="Mask"/>). Освобождённые DOF исключаются последовательно (Гаусс по одному DOF): строка и
/// столбец DOF обнуляются, усилие КЭ по нему — 0. DOF, жёсткость которого после предыдущих исключений нулевая
/// (механизм внутри КЭ: например, кручение освобождено на обоих концах), просто обнуляется — у неотрицательно
/// определённой матрицы его связи с остальными тогда тоже нулевые.
/// </summary>
public static class BeamReleases
{
    /// <summary>Все шесть DOF конца.</summary>
    public const int AllEnd = 0b111111;

    // Порог «нулевой» ведущей жёсткости — доля исходного диагонального члена DOF.
    private const double ZeroPivot = 1e-10;

    /// <summary>12-битная маска КЭ по маскам концов (биты 0–5 каждого конца).</summary>
    public static int Mask(int releaseI, int releaseJ)
    {
        if ((releaseI & ~AllEnd) != 0 || (releaseJ & ~AllEnd) != 0)
            throw new ArgumentException($"Маска освобождений конца — биты 0–5, задано {releaseI}/{releaseJ}.");
        return releaseI | (releaseJ << 6);
    }

    /// <summary>Сконденсированная локальная матрица: освобождённые строки и столбцы — нули.</summary>
    public static double[,] Condense(double[,] k, int mask)
    {
        if (mask == 0) return k;
        return Eliminate(k, mask).K;
    }

    /// <summary>
    /// Локальная узловая нагрузка КЭ, приведённая к сохранённым DOF: f_r − K_rc·K_cc⁻¹·f_c, по освобождённым — 0
    /// (например, момент wL²/12 у шарнирного конца переходит в поперечные силы концов).
    /// </summary>
    public static double[] CondenseLoad(double[,] k, int mask, double[] f)
    {
        if (mask == 0) return f;
        var r = (double[])f.Clone();
        foreach (var s in Eliminate(k, mask).Steps)
        {
            if (s.Pivot > 0.0)
                for (int j = 0; j < 12; j++)
                    if (j != s.Dof) r[j] -= s.Row[j] / s.Pivot * r[s.Dof];
            r[s.Dof] = 0.0;
        }
        return r;
    }

    /// <summary>
    /// Полные локальные перемещения концов КЭ: освобождённые DOF узла заменяются перемещениями конца стержня из условия
    /// нулевых усилий по ним, d_c = −K_cc⁻¹·K_cr·d_r (без пролётной нагрузки). Для деформаций сечений: узел у шарнира
    /// поворачивается независимо от конца стержня.
    /// </summary>
    public static double[] Recover(double[,] k, int mask, double[] d)
    {
        if (mask == 0) return d;
        var r = (double[])d.Clone();
        var steps = Eliminate(k, mask).Steps;
        for (int t = steps.Count - 1; t >= 0; t--)
        {
            var s = steps[t];
            double sum = 0.0;
            if (s.Pivot > 0.0)
                for (int j = 0; j < 12; j++)
                    if (j != s.Dof) sum += s.Row[j] * r[j];
            r[s.Dof] = s.Pivot > 0.0 ? -sum / s.Pivot : 0.0;
        }
        return r;
    }

    private sealed record Step(int Dof, double Pivot, double[] Row);

    // Последовательное исключение: строка DOF фиксируется на момент исключения (уже исключённые DOF в ней — нули).
    private static (double[,] K, List<Step> Steps) Eliminate(double[,] k0, int mask)
    {
        if (k0.GetLength(0) != 12 || k0.GetLength(1) != 12)
            throw new ArgumentException("Конденсация шарниров — для локальной матрицы стержня 12×12.");
        if ((mask & ~0xFFF) != 0) throw new ArgumentException($"Маска освобождений КЭ — 12 бит, задано {mask}.");
        var k = (double[,])k0.Clone();
        var steps = new List<Step>();
        for (int c = 0; c < 12; c++)
        {
            if ((mask & (1 << c)) == 0) continue;
            double pivot = k[c, c];
            var row = new double[12];
            for (int j = 0; j < 12; j++) row[j] = k[c, j];
            bool zero = !(pivot > ZeroPivot * Math.Abs(k0[c, c]));
            if (!zero)
                for (int i = 0; i < 12; i++)
                {
                    if (i == c || k[i, c] == 0.0) continue;
                    double m = k[i, c] / pivot;
                    for (int j = 0; j < 12; j++) k[i, j] -= m * row[j];
                }
            for (int i = 0; i < 12; i++) { k[i, c] = 0.0; k[c, i] = 0.0; }
            steps.Add(new Step(c, zero ? 0.0 : pivot, row));
        }
        // Симметрия против накопления округлений.
        for (int i = 0; i < 12; i++)
            for (int j = i + 1; j < 12; j++)
                k[i, j] = k[j, i] = 0.5 * (k[i, j] + k[j, i]);
        return (k, steps);
    }
}
