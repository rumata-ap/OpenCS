namespace CSfea.Torsion;

/// <summary>
/// Интегралы граничных элементов для логарифмического потенциала Лапласа.
/// Порт rlintc/slintc/dalpha из GreenSectionPy (_torsion_bem_core.py).
/// </summary>
public static class BemKernels
{
    /// <summary>
    /// Внедиагональный элемент матрицы G: интеграл по элементу [x1,y1]→[x2,y2]
    /// от (1/2π)·ln|r|, где r — расстояние от точки коллокации (px,py) до точки Гаусса.
    /// halfLength — полудлина элемента.
    /// </summary>
    public static double Rlintc(double px, double py, double x1, double y1, double x2, double y2,
        double halfLength)
    {
        double dx = (x2 - x1) * 0.5, dy = (y2 - y1) * 0.5;
        double bx = (x2 + x1) * 0.5, by_ = (y2 + y1) * 0.5;
        double sum = 0.0;
        for (int q = 0; q < GaussLegendre.Xi.Length; q++)
        {
            double xi = GaussLegendre.Xi[q];
            double gx = dx * xi + bx;
            double gy = dy * xi + by_;
            double r = Math.Sqrt((gx - px) * (gx - px) + (gy - py) * (gy - py));
            if (r <= 0.0) continue;
            sum += GaussLegendre.W[q] * Math.Log(r);
        }
        // halfLength = sl/2; оригинал: sum · sl/(4π) = sum · 2·halfLength/(4π) = sum·halfLength/(2π)
        return sum * halfLength / (2.0 * Math.PI);
    }

    /// <summary>
    /// Диагональный элемент матрицы G (аналитически): G_ii = (l/2)·(ln(l/2) − 1)/π,
    /// где halfLength = l/2.
    /// </summary>
    public static double Slintc(double halfLength)
        => halfLength * (Math.Log(halfLength) - 1.0) / Math.PI;

    /// <summary>
    /// Внедиагональный элемент матрицы H (телесный угол): (1/2π)·atan2(dy2r, dx2r),
    /// где (dx2r,dy2r) — вектор к концу2 элемента, повёрнутый так, что вектор к концу1
    /// сонаправлен с осью X. Точное повторение _dalpha из GreenSectionPy.
    /// </summary>
    public static double Dalpha(double px, double py, double x1, double y1, double x2, double y2)
    {
        double dy1 = y1 - py, dx1 = x1 - px;
        double dy2 = y2 - py, dx2 = x2 - px;
        double dl1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);
        double cos1 = dx1 / dl1, sin1 = dy1 / dl1;
        // Поворот вектора (dx2, dy2) на −angle1 (чтобы вектор к концу1 стал вдоль +X)
        double dx2r = dx2 * cos1 + dy2 * sin1;
        double dy2r = -dx2 * sin1 + dy2 * cos1;
        return Math.Atan2(dy2r, dx2r) / (2.0 * Math.PI);
    }
}
