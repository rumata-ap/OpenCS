namespace CScore.ShellSimpl;

/// <summary>Оптимум ортогональной сетки: площади по x и y, м²/м, и управляющие фасетки.</summary>
/// <param name="ActiveThetas">Углы фасеток, ограничения которых активны в оптимуме, градусы.</param>
public sealed record OrthogonalMeshSolution(double Asx, double Asy, IReadOnlyList<double> ActiveThetas);

/// <summary>
/// Линейная задача подбора ортогональной сетки по требованиям фасеток (спека, раздел 6, п. 4):
/// min Asx + Asy при Asx·cos²θ + Asy·sin²θ ≥ r(θ) для всех θ, Asx, Asy ≥ 0.
/// Две переменные — решается точно перебором вершин допустимой области: пересечения пар
/// ограничений, ограничений с осями и начало координат, с проверкой допустимости.
/// </summary>
public static class OrthogonalMeshLp
{
    /// <summary>Относительный допуск активности и допустимости.</summary>
    const double RelTol = 1e-9;

    public static OrthogonalMeshSolution Solve(IReadOnlyList<(double ThetaDeg, double R)> demands)
    {
        // Требования r ≤ 0 выполняются при любой неотрицательной сетке.
        var cons = new List<(double Theta, double C2, double S2, double R)>(demands.Count);
        double scale = 0.0;
        foreach (var (theta, r) in demands)
        {
            if (!(r > 0.0)) continue;
            double a = theta * Math.PI / 180.0;
            double c = Math.Cos(a), s = Math.Sin(a);
            cons.Add((theta, c * c, s * s, r));
            scale = Math.Max(scale, r);
        }
        if (cons.Count == 0) return new OrthogonalMeshSolution(0.0, 0.0, []);
        double tol = RelTol * scale;

        bool Feasible(double x, double y)
        {
            if (x < -tol || y < -tol) return false;
            foreach (var k in cons)
                if (k.C2 * x + k.S2 * y < k.R - tol) return false;
            return true;
        }

        double bestX = double.NaN, bestY = double.NaN, bestSum = double.PositiveInfinity;
        void Consider(double x, double y)
        {
            if (!double.IsFinite(x) || !double.IsFinite(y)) return;
            x = Math.Max(0.0, x); y = Math.Max(0.0, y);
            double sum = x + y;
            // При равной сумме (ребро оптимума параллельно целевой — фасетка 45°) — более
            // равномерная сетка.
            if (sum > bestSum + tol) return;
            if (sum > bestSum - tol && Math.Abs(x - y) >= Math.Abs(bestX - bestY)) return;
            if (!Feasible(x, y)) return;
            bestX = x; bestY = y; bestSum = sum;
        }

        // Равномерная сетка Asx = Asy = max r всегда допустима; при ребре оптимума, параллельном
        // целевой, это его внутренняя точка.
        Consider(scale, scale);
        // Ограничение с осями: x = r/c² при y = 0, y = r/s² при x = 0.
        foreach (var k in cons)
        {
            if (k.C2 > 1e-12) Consider(k.R / k.C2, 0.0);
            if (k.S2 > 1e-12) Consider(0.0, k.R / k.S2);
        }
        // Пары ограничений.
        for (int i = 0; i < cons.Count; i++)
        for (int j = i + 1; j < cons.Count; j++)
        {
            var a = cons[i]; var b = cons[j];
            double det = a.C2 * b.S2 - a.S2 * b.C2;
            if (Math.Abs(det) < 1e-12) continue;
            Consider((a.R * b.S2 - a.S2 * b.R) / det, (a.C2 * b.R - a.R * b.C2) / det);
        }

        if (double.IsNaN(bestX))
            throw new InvalidOperationException("Ортогональная сетка не найдена: допустимая область пуста");

        var active = new List<double>();
        foreach (var k in cons)
            if (k.C2 * bestX + k.S2 * bestY <= k.R + 1e-6 * scale) active.Add(k.Theta);
        return new OrthogonalMeshSolution(bestX, bestY, active);
    }
}
