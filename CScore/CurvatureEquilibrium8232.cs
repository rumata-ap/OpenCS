using System;

namespace CScore;

/// <summary>Результат поиска плоскости деформаций при заданной кривизне.</summary>
public readonly record struct CurvatureEquilibriumResult(
    double E0,
    Load Load,
    bool Converged,
    int Iterations,
    double Residual);

/// <summary>Скалярное решение равновесия продольной силы для заданной кривизны.</summary>
public static class CurvatureEquilibrium8232
{
    /// <summary>
    /// Находит значение ε0, при котором заданный вычислитель возвращает целевую продольную силу.
    /// Метод используется для построения диаграммы кривизна–момент при фиксированном N.
    /// </summary>
    public static CurvatureEquilibriumResult Solve(
        Func<double, Load> evaluate,
        double targetN = 0.0,
        double initialE0 = 0.0,
        double tolerance = 1e-9,
        double initialBracket = 0.02,
        int maxIterations = 100)
    {
        ArgumentNullException.ThrowIfNull(evaluate);
        if (!double.IsFinite(targetN) || !double.IsFinite(initialE0))
            throw new ArgumentOutOfRangeException(nameof(targetN));
        if (!double.IsFinite(tolerance) || tolerance <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        if (!double.IsFinite(initialBracket) || initialBracket <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(initialBracket));
        if (maxIterations < 1)
            throw new ArgumentOutOfRangeException(nameof(maxIterations));

        double halfWidth = initialBracket;
        double lo = initialE0 - halfWidth;
        double hi = initialE0 + halfWidth;
        Load loLoad = evaluate(lo);
        Load hiLoad = evaluate(hi);
        double loResidual = loLoad.N - targetN;
        double hiResidual = hiLoad.N - targetN;

        for (int expansion = 0; expansion < 20 && !OppositeSigns(loResidual, hiResidual); expansion++)
        {
            halfWidth *= 2.0;
            lo = initialE0 - halfWidth;
            hi = initialE0 + halfWidth;
            loLoad = evaluate(lo);
            hiLoad = evaluate(hi);
            loResidual = loLoad.N - targetN;
            hiResidual = hiLoad.N - targetN;
        }

        if (!OppositeSigns(loResidual, hiResidual))
            throw new InvalidOperationException("Не удалось заключить равновесное ε0 в скобки");

        // Критерий остановки только по невязке N опасен там, где dN/de0 локально мал (типично
        // сразу после трещины — часть бетонных фибр уже перешла на нисходящую ветвь
        // растяжения, вклад в жёсткость по N почти нулевой): |N-target|<=tolerance тогда
        // выполняется на широком диапазоне e0, и бисекция может остановиться в любой его
        // точке — соседние точки скана (с чуть разной κ, но близким seed-продолжением e0)
        // получают заметно разные e0/Mx, хотя обе формально "сошлись" — визуально это пила на
        // графике кривизна-момент (см. пилообразный уч. "2" в реальной задаче пользователя,
        // 2026-08-17). Фикс — не выходить раньше, чем сам интервал [lo,hi] сузится до
        // domainTolerance (доля от фактической начальной ширины скобки, с учётом возможных
        // расширений выше) — это гарантирует стабильно точный e0 независимо от локальной
        // формы N(e0), а не только "достаточно малую" невязку.
        double domainTolerance = (hi - lo) * 1e-10;
        Load lastLoad = loLoad;
        double lastE0 = lo;
        double lastResidual = Math.Abs(loResidual);
        for (int iteration = 1; iteration <= maxIterations; iteration++)
        {
            lastE0 = 0.5 * (lo + hi);
            lastLoad = evaluate(lastE0);
            lastResidual = lastLoad.N - targetN;

            if (!double.IsFinite(lastResidual))
                throw new InvalidOperationException("Вычислитель равновесия вернул недопустимую силу N");

            if (Math.Abs(lastResidual) <= tolerance && (hi - lo) <= domainTolerance)
                return new CurvatureEquilibriumResult(lastE0, lastLoad, true, iteration, Math.Abs(lastResidual));

            if (SameSign(lastResidual, loResidual))
            {
                lo = lastE0;
                loResidual = lastResidual;
            }
            else
            {
                hi = lastE0;
                hiResidual = lastResidual;
            }
        }

        return new CurvatureEquilibriumResult(
            lastE0, lastLoad, false, maxIterations, Math.Abs(lastResidual));
    }

    static bool OppositeSigns(double left, double right) =>
        double.IsFinite(left) && double.IsFinite(right) &&
        (left == 0.0 || right == 0.0 || Math.Sign(left) != Math.Sign(right));

    static bool SameSign(double left, double right) =>
        left == 0.0 || Math.Sign(left) == Math.Sign(right);
}
