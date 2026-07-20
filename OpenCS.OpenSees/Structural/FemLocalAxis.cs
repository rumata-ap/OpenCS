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

    /// <summary>
    /// vecxz: глобальная Z для не-вертикальных стержней, глобальная X для вертикальных;
    /// затем поворачивается вокруг локальной оси X стержня (направление I→J) на rotationDeg
    /// градусов по правилу правой руки — «β-угол» сечения (см.
    /// docs/superpowers/specs/2026-07-21-fem-member-local-axis-rotation-design.md).
    /// Компонента базового вектора вдоль самой оси X не влияет на итоговый vecxz (обнуляется
    /// векторным произведением внутри geomTransf), поэтому поворачивать можно исходный
    /// вектор целиком, без предварительной проекции на перпендикулярную плоскость.
    /// </summary>
    public static (double X, double Y, double Z) Vecxz(FemLinearNode i, FemLinearNode j, double rotationDeg = 0)
    {
        double dx = j.X - i.X, dy = j.Y - i.Y, dz = j.Z - i.Z;
        double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (!double.IsFinite(len) || len < 1e-9)
            throw new InvalidOperationException("Нулевая длина стержня — невозможно построить локальные оси.");
        double ez = dz / len;
        (double X, double Y, double Z) baseVec = Math.Abs(ez) > VerticalCosineThreshold ? (1, 0, 0) : (0, 0, 1);
        if (rotationDeg == 0) return baseVec;

        var axis = (X: dx / len, Y: dy / len, Z: dz / len);
        return RotateAroundAxis(baseVec, axis, rotationDeg * Math.PI / 180.0);
    }

    /// <summary>Поворачивает вектор v вокруг единичной оси axis на angleRad (формула Родригеса):
    /// v_rot = v·cosθ + (axis×v)·sinθ + axis·(axis·v)·(1−cosθ).</summary>
    static (double X, double Y, double Z) RotateAroundAxis(
        (double X, double Y, double Z) v, (double X, double Y, double Z) axis, double angleRad)
    {
        double cos = Math.Cos(angleRad), sin = Math.Sin(angleRad);
        double axisDotV = axis.X * v.X + axis.Y * v.Y + axis.Z * v.Z;
        double crossX = axis.Y * v.Z - axis.Z * v.Y;
        double crossY = axis.Z * v.X - axis.X * v.Z;
        double crossZ = axis.X * v.Y - axis.Y * v.X;
        return (
            v.X * cos + crossX * sin + axis.X * axisDotV * (1 - cos),
            v.Y * cos + crossY * sin + axis.Y * axisDotV * (1 - cos),
            v.Z * cos + crossZ * sin + axis.Z * axisDotV * (1 - cos)
        );
    }
}
