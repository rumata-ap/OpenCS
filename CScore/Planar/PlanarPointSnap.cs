namespace CScore.Planar;

/// <summary>Результат привязки точки: итоговая точка и чем она получена.</summary>
/// <param name="U">Координата U (ось x региона).</param>
/// <param name="V">Координата V (ось y региона).</param>
/// <param name="Kind">Способ привязки.</param>
public readonly record struct PlanarSnapResult(double U, double V, PlanarSnapKind Kind);

/// <summary>Способ привязки точки на плане региона.</summary>
public enum PlanarSnapKind
{
    /// <summary>Без привязки: точка как указана.</summary>
    None,
    /// <summary>К вершине контура, зоны или узлу сетки.</summary>
    Vertex,
    /// <summary>К середине ребра контура.</summary>
    Midpoint,
    /// <summary>К шагу сетки привязки.</summary>
    Grid,
    /// <summary>Выравнивание с предыдущей точкой вдоль оси U или V (ортогональный режим).</summary>
    Ortho,
}

/// <summary>
/// Привязка точек, указанных мышью на плане региона (ось полосы, вершины зон армирования). Порядок
/// приоритета: вершина → середина ребра → ортогональное выравнивание с предыдущей точкой → шаг сетки.
/// Допуск задаётся в единицах модели (пиксели × масштаб вида), поэтому привязка не зависит от зума.
/// Чистая функция без UI.
/// </summary>
public static class PlanarPointSnap
{
    /// <summary>Допуск ортогонального выравнивания, радианы (≈ 3°).</summary>
    public const double OrthoToleranceRad = 3.0 * Math.PI / 180.0;

    /// <summary>
    /// Привязать точку.
    /// </summary>
    /// <param name="u">Указанная точка, U.</param>
    /// <param name="v">Указанная точка, V.</param>
    /// <param name="vertices">Вершины контуров, зон, узлы сетки.</param>
    /// <param name="edges">Контуры (замкнутые, без повтора первой точки): середины их рёбер — кандидаты привязки.</param>
    /// <param name="toleranceM">Допуск привязки к вершине/середине, в единицах модели.</param>
    /// <param name="anchor">Предыдущая точка для ортогонального выравнивания; null — не применяется.</param>
    /// <param name="gridStepM">Шаг сетки привязки; 0 — не применяется.</param>
    public static PlanarSnapResult Snap(
        double u, double v,
        IEnumerable<(double U, double V)> vertices,
        IEnumerable<IReadOnlyList<(double U, double V)>> edges,
        double toleranceM,
        (double U, double V)? anchor = null,
        double gridStepM = 0.0)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(edges);

        if (toleranceM > 0)
        {
            double best = toleranceM;
            PlanarSnapResult? found = null;
            foreach (var p in vertices)
            {
                double d = Math.Sqrt((p.U - u) * (p.U - u) + (p.V - v) * (p.V - v));
                if (d <= best) { best = d; found = new(p.U, p.V, PlanarSnapKind.Vertex); }
            }
            if (found is { } vertex) return vertex;

            foreach (var loop in edges)
                for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
                {
                    double mu = 0.5 * (loop[i].U + loop[j].U), mv = 0.5 * (loop[i].V + loop[j].V);
                    double d = Math.Sqrt((mu - u) * (mu - u) + (mv - v) * (mv - v));
                    if (d <= best) { best = d; found = new(mu, mv, PlanarSnapKind.Midpoint); }
                }
            if (found is { } mid) return mid;
        }

        if (anchor is { } a)
        {
            double du = u - a.U, dv = v - a.V;
            double len = Math.Sqrt(du * du + dv * dv);
            if (len > 1e-12)
            {
                double angle = Math.Atan2(dv, du);
                // Допуск по углу: у короткого отрезка тот же угол — слишком грубый сдвиг, поэтому ограничиваем и по длине.
                if (Math.Abs(Math.Sin(angle)) * len <= toleranceM || Math.Abs(Math.Sin(angle)) <= Math.Sin(OrthoToleranceRad))
                    return new(u, a.V, PlanarSnapKind.Ortho);
                if (Math.Abs(Math.Cos(angle)) * len <= toleranceM || Math.Abs(Math.Cos(angle)) <= Math.Sin(OrthoToleranceRad))
                    return new(a.U, v, PlanarSnapKind.Ortho);
            }
        }

        if (gridStepM > 0)
            return new(Math.Round(u / gridStepM) * gridStepM, Math.Round(v / gridStepM) * gridStepM, PlanarSnapKind.Grid);

        return new(u, v, PlanarSnapKind.None);
    }
}
