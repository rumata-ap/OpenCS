namespace OpenCS.OpenSees.Structural;

/// <summary>Фиксированная конвенция локальных осей стержня для geomTransf Linear.</summary>
public static class FemLocalAxis
{
    /// <summary>
    /// Порог «вертикальности» по углу от оси Z, а не по сырому машинному эпсилон на ez.
    /// cos(0.5°) ≈ 0.99996 — стержни, отклонённые от вертикали менее чем на ~0.5°
    /// (типичный шум округления координат при импорте), всё ещё считаются вертикальными.
    /// Старый порог 1-1e-6 требовал отклонения &lt; ~0.081° и был слишком хрупким: при
    /// vecxz=(0,0,1) почти параллельном оси стержня geomTransf вырождается, локальные оси
    /// y/z поворачиваются на произвольный угол, и чистый момент в одной глобальной плоскости
    /// «расщепляется» между My и Mz элемента.
    /// </summary>
    const double VerticalCosineThreshold = 0.99996;

    /// <summary>vecxz: глобальная Z для не-вертикальных стержней, глобальная X для вертикальных.</summary>
    public static (double X, double Y, double Z) Vecxz(FemLinearNode i, FemLinearNode j)
    {
        double dx = j.X - i.X, dy = j.Y - i.Y, dz = j.Z - i.Z;
        double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (!double.IsFinite(len) || len < 1e-9)
            throw new InvalidOperationException("Нулевая длина стержня — невозможно построить локальные оси.");
        double ez = dz / len;
        return Math.Abs(ez) > VerticalCosineThreshold ? (1, 0, 0) : (0, 0, 1);
    }
}
