namespace CScore.Sp63.Normal;

/// <summary>Ветвь расчёта кольцевого сечения по п. Д.1.</summary>
public enum Sp63AnnularBranch
{
    /// <summary>0,15 &lt; ξcir &lt; 0,6 — формула (Д.2).</summary>
    A = 1,
    /// <summary>ξcir ≤ 0,15 — формула (Д.3).</summary>
    B = 2,
    /// <summary>ξcir ≥ 0,6 — формула (Д.4).</summary>
    C = 3
}

/// <summary>Результат расчёта кольцевого сечения по п. Д.1.</summary>
/// <param name="Branch">Выбранная ветвь.</param>
/// <param name="XiCir">ξcir по (Д.1).</param>
/// <param name="XiCir1">ξcir1 для ветви б), иначе NaN.</param>
/// <param name="XiCir2">ξcir2 по (Д.5) для ветви в), иначе NaN.</param>
/// <param name="Mult">Предельный момент, кН·м (0 при перегрузке).</param>
/// <param name="Overloaded">ξcir2 ≥ 1: сжатие превышает несущую способность сечения.</param>
public readonly record struct Sp63AnnularCapacity(Sp63AnnularBranch Branch, double XiCir,
    double XiCir1, double XiCir2, double Mult, bool Overloaded);

/// <summary>Результат расчёта круглого сечения по п. Д.2.</summary>
/// <param name="ConditionD7">Выполнено ли условие (Д.7).</param>
/// <param name="XiCir">ξcir из (Д.8)/(Д.9); NaN при перегрузке.</param>
/// <param name="Phi">Коэффициент φ.</param>
/// <param name="Mult">Предельный момент, кН·м (0 при перегрузке).</param>
/// <param name="Overloaded">Уравнение (Д.9) не имеет корня на [0; 1].</param>
public readonly record struct Sp63CircularCapacity(bool ConditionD7, double XiCir,
    double Phi, double Mult, bool Overloaded);

/// <summary>Формулы приложения Д СП 63.13330.2018 (растр HTML-копии нормы).</summary>
public static class Sp63CircularFormulas
{
    /// <summary>Допуск признака перегрузки кольца по ξcir2.</summary>
    public const double OverloadXiTolerance = 1e-12;
    const double SolverTolerance = 1e-12;
    const int SolverMaxIterations = 200;

    /// <summary>Кольцевое сечение, формулы (Д.1)–(Д.5). n — модуль сжимающей силы, кН.</summary>
    public static Sp63AnnularCapacity Annular(double n, double rb, double rs, double rsc,
        double area, double asTot, double r1, double r2, double rsRadius)
    {
        double rm = (r1 + r2) / 2.0;
        double k = rb * area * rm + rsc * asTot * rsRadius;
        double xi = (n + rs * asTot) / (rb * area + (rsc + 1.7 * rs) * asTot);

        if (xi > 0.15 && xi < 0.6)
        {
            double mult = k * Math.Sin(Math.PI * xi) / Math.PI +
                          rs * asTot * rsRadius * (1.0 - 1.7 * xi) * (0.2 + 1.3 * xi);
            return new(Sp63AnnularBranch.A, xi, double.NaN, double.NaN, mult, false);
        }

        if (xi <= 0.15)
        {
            double xi1 = (n + 0.75 * rs * asTot) / (rb * area + rsc * asTot);
            double mult = k * Math.Sin(Math.PI * xi1) / Math.PI + 0.295 * rs * asTot * rsRadius;
            return new(Sp63AnnularBranch.B, xi, xi1, double.NaN, mult, false);
        }

        double xi2 = n / (rb * area + rsc * asTot);
        // Перегрузку определяем по ξcir2, а не по знаку синуса: при ξcir2 > 2 синус снова > 0.
        if (xi2 >= 1.0 - OverloadXiTolerance)
            return new(Sp63AnnularBranch.C, xi, double.NaN, xi2, 0.0, true);
        return new(Sp63AnnularBranch.C, xi, double.NaN, xi2,
            k * Math.Sin(Math.PI * xi2) / Math.PI, false);
    }

    /// <summary>Круглое сечение, формулы (Д.6)–(Д.9). n — модуль сжимающей силы, кН.</summary>
    public static Sp63CircularCapacity Circular(double n, double rb, double rs,
        double area, double asTot, double r, double rsRadius)
    {
        bool conditionD7 = n <= 0.77 * rb * area + 0.645 * rs * asTot;
        double? root = SolveXi(n, rb, rs, area, asTot, conditionD7);
        if (root is null)
            return new(conditionD7, double.NaN, 0.0, 0.0, true);

        double xi = root.Value;
        // φ буквально по норме: ограничение сверху 1,0, снизу не ограничивается.
        double phi = conditionD7 ? Math.Min(1.0, 1.6 * (1.0 - 1.55 * xi) * xi) : 0.0;
        double s = Math.Sin(Math.PI * xi);
        double mult = 2.0 / 3.0 * rb * area * r * s * s * s / Math.PI +
                      rs * asTot * (s / Math.PI + phi) * rsRadius;
        return new(conditionD7, xi, phi, mult, false);
    }

    /// <summary>
    /// Решает (Д.8) при выполненном (Д.7) или (Д.9) иначе бисекцией на [0; 1].
    /// Невязка строго возрастает, поэтому корень единственный; null — корня нет (перегрузка).
    /// </summary>
    public static double? SolveXi(double n, double rb, double rs, double area,
        double asTot, bool conditionD7)
    {
        double k = conditionD7 ? 2.55 : 1.0;
        // В знаменателе (Д.9) стоит Rs — буквально по растру нормы.
        double c = conditionD7 ? n + rs * asTot : n;
        double Residual(double xi) => xi * (rb * area + k * rs * asTot) - c -
                                      rb * area * Math.Sin(2.0 * Math.PI * xi) / (2.0 * Math.PI);

        if (Residual(1.0) < 0.0)
            return null;

        double lo = 0.0, hi = 1.0;
        for (int i = 0; i < SolverMaxIterations && hi - lo > SolverTolerance; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (Residual(mid) < 0.0) lo = mid;
            else hi = mid;
        }
        return 0.5 * (lo + hi);
    }
}
