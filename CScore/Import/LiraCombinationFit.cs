namespace CScore.Import;

/// <summary>
/// Уравнение подбора: значение компоненты усилия в сочетании и та же компонента в каждом загружении
/// (порядок — как у списка загружений подбора).
/// </summary>
/// <param name="Target">Компонента в сочетании (строка РСУ/РСН ЛИРЫ).</param>
/// <param name="Cases">Компонента в загружениях.</param>
public sealed record LiraFitEquation(double Target, IReadOnlyList<double> Cases);

/// <summary>
/// Итог подбора коэффициентов сочетания.
/// </summary>
/// <param name="Terms">Загружение (номер в программе) и коэффициент; пусто — подбор не удался.</param>
/// <param name="Residual">Относительная невязка ‖W(A·k − b)‖ / ‖W·b‖ (W — масштаб строк).</param>
/// <param name="Failure">Почему коэффициенты не найдены или не приняты; null — подбор принят.</param>
public sealed record LiraCombinationFitResult(IReadOnlyList<(int LoadCase, double Factor)> Terms, double Residual, string? Failure)
{
    /// <summary>Подбор принят.</summary>
    public bool Ok => Failure == null;
}

/// <summary>
/// Раскладка сочетания ЛИРЫ на загружения. API ЛИРЫ отдаёт у строки РСУ только номера входящих загружений
/// (<c>GetLoadCases</c>), у РСН — ни номеров, ни коэффициентов; усилия сочетания линейны по загружениям, поэтому
/// коэффициенты находятся МНК по компонентам усилий тех же стержней: A·k = b, столбцы A — загружения. Строки
/// масштабируются к своему наибольшему модулю (силы и моменты в одних весах); строки на уровне шума отбрасываются.
/// </summary>
public static class LiraCombinationFit
{
    /// <summary>Допуск относительной невязки по умолчанию.</summary>
    public const double DefaultTolerance = 0.01;

    /// <summary>Строка отбрасывается, если её модуль меньше этой доли наибольшего модуля строк.</summary>
    const double NoiseShare = 1e-4;

    /// <summary>Столбец считается линейно зависимым, если после ортогонализации от него остаётся меньше этой доли.</summary>
    const double RankShare = 1e-6;

    /// <summary>
    /// Подбирает коэффициенты загружений <paramref name="loadCases"/> по уравнениям <paramref name="equations"/>.
    /// Не принимается: нет уравнений, загружение не влияет на компоненты (коэффициент не определяется), невязка больше
    /// <paramref name="tolerance"/>.
    /// </summary>
    public static LiraCombinationFitResult Fit(IReadOnlyList<int> loadCases, IReadOnlyList<LiraFitEquation> equations,
        double tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(loadCases);
        ArgumentNullException.ThrowIfNull(equations);
        int n = loadCases.Count;
        if (n == 0) return new([], 0, "в сочетании нет загружений");
        foreach (var e in equations)
            if (e.Cases.Count != n) throw new ArgumentException("Число компонент загружений в уравнении не равно числу загружений.", nameof(equations));

        // Масштаб строк и отбор строк выше шума.
        var scales = equations.Select(e => Math.Max(Math.Abs(e.Target), e.Cases.Max(Math.Abs))).ToArray();
        double top = scales.Length == 0 ? 0 : scales.Max();
        if (top <= 0) return new([], 0, "усилия стержней в сочетании и загружениях нулевые");
        var rows = Enumerable.Range(0, equations.Count).Where(i => scales[i] >= NoiseShare * top).ToList();
        int m = rows.Count;
        if (m < n) return new([], 0, $"компонент усилий ({m}) меньше, чем загружений ({n})");

        // Взвешенная система: a[j] — столбец загружения j, b — правая часть.
        var a = new double[n][];
        for (int j = 0; j < n; j++)
        {
            a[j] = new double[m];
            for (int r = 0; r < m; r++) a[j][r] = equations[rows[r]].Cases[j] / scales[rows[r]];
        }
        var b = new double[m];
        for (int r = 0; r < m; r++) b[r] = equations[rows[r]].Target / scales[rows[r]];

        // Модифицированный Грам — Шмидт: A = Q·R.
        var q = new double[n][];
        var rr = new double[n, n];
        for (int j = 0; j < n; j++)
        {
            var v = (double[])a[j].Clone();
            double norm0 = Norm(v);
            for (int i = 0; i < j; i++)
            {
                double d = Dot(q[i], v);
                rr[i, j] = d;
                for (int r = 0; r < m; r++) v[r] -= d * q[i][r];
            }
            double norm = Norm(v);
            if (norm0 == 0 || norm < RankShare * norm0)
                return new([], 0, norm0 == 0
                    ? $"загружение {loadCases[j]} не влияет на усилия стержней — коэффициент не определяется"
                    : $"загружение {loadCases[j]} линейно зависит от других по усилиям стержней — коэффициент не определяется");
            rr[j, j] = norm;
            q[j] = v.Select(x => x / norm).ToArray();
        }

        // R·k = Qᵀ·b.
        var k = new double[n];
        for (int j = n - 1; j >= 0; j--)
        {
            double s = Dot(q[j], b);
            for (int i = j + 1; i < n; i++) s -= rr[j, i] * k[i];
            k[j] = s / rr[j, j];
        }

        var res = (double[])b.Clone();
        for (int j = 0; j < n; j++)
            for (int r = 0; r < m; r++) res[r] -= k[j] * a[j][r];
        double bn = Norm(b);
        double residual = bn > 0 ? Norm(res) / bn : Norm(res);
        var terms = Enumerable.Range(0, n).Select(j => (loadCases[j], Math.Round(k[j], 4))).ToList();
        return residual > tolerance
            ? new(terms, residual, $"невязка подбора {residual:P1} больше допуска {tolerance:P0}")
            : new(terms, residual, null);
    }

    static double Dot(double[] x, double[] y)
    {
        double s = 0;
        for (int i = 0; i < x.Length; i++) s += x[i] * y[i];
        return s;
    }

    static double Norm(double[] x) => Math.Sqrt(Dot(x, x));
}
