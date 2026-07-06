namespace CSfea.Torsion;

/// <summary>Геометрические метрики контура кручения, используемые для авто-подбора шага сетки.</summary>
public static class TorsionBoundaryMetrics
{
    /// <summary>
    /// Минимальная длина ребра многоугольника (по внешнему контуру и всем отверстиям).
    /// Для сечений со скруглениями (дуги аппроксимированы полигоном при построении контура)
    /// это фактически длина самой мелкой хорды дуги — естественный "пол" детализации,
    /// заданный при построении геометрии, а не размером граничного/конечного элемента.
    /// Вырожденные (нулевые/почти нулевые) рёбра игнорируются.
    /// </summary>
    public static double MinEdgeLength(TorsionBoundary boundary)
    {
        double min = double.MaxValue;
        UpdateMinLoopEdge(boundary.OuterX, boundary.OuterY, ref min);
        if (boundary.Holes != null)
            foreach (var (hx, hy) in boundary.Holes)
                UpdateMinLoopEdge(hx, hy, ref min);
        return min;
    }

    const double DegenerateEdgeTolerance = 1e-9;

    static void UpdateMinLoopEdge(double[] x, double[] y, ref double min)
    {
        int n = x.Length;
        if (n < 2) return;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            double dx = x[j] - x[i], dy = y[j] - y[i];
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len > DegenerateEdgeTolerance && len < min)
                min = len;
        }
    }
}
