using CScore.Planar;

namespace CScore.Fem.Fragment;

/// <summary>Вертикальная грань призмы: внешняя нормаль в плане и смещение; внутри — n·p ≤ <see cref="Offset"/>.</summary>
public readonly record struct FragmentPrismFace(PlanarVector3 Normal, double Offset)
{
    /// <summary>Отстояние точки от грани со знаком: больше нуля — снаружи.</summary>
    public double Eval(PlanarVector3 p) => Normal.Dot(p) - Offset;
}

/// <summary>
/// Призма фрагмента вокруг стен и колонн (спека <c>2026-10-30-fem-element-fragment-prism-design.md</c>): прямоугольник в
/// плане с центром (<see cref="CenterX"/>, <see cref="CenterY"/>), единичной осью (<see cref="AxisX"/>, <see cref="AxisY"/>)
/// и полуразмерами вдоль оси и поперёк; по высоте — полоса z₁…z₂ запроса.
/// </summary>
public sealed record FragmentPrism(double CenterX, double CenterY, double AxisX, double AxisY, double HalfLength, double HalfWidth)
{
    /// <summary>Призма с нормированной осью; нулевая ось — глобальная X.</summary>
    public static FragmentPrism Create(double centerX, double centerY, double axisX, double axisY, double halfLength, double halfWidth)
    {
        double len = Math.Sqrt(axisX * axisX + axisY * axisY);
        return len < 1e-12
            ? new(centerX, centerY, 1, 0, halfLength, halfWidth)
            : new(centerX, centerY, axisX / len, axisY / len, halfLength, halfWidth);
    }

    /// <summary>Грани +ось, −ось, +поперёк, −поперёк (поперёк = ось, повёрнутая на 90° против часовой).</summary>
    public IReadOnlyList<FragmentPrismFace> Faces
    {
        get
        {
            double c = CenterX * AxisX + CenterY * AxisY, p = -CenterX * AxisY + CenterY * AxisX;
            return
            [
                new(new PlanarVector3(AxisX, AxisY, 0), c + HalfLength),
                new(new PlanarVector3(-AxisX, -AxisY, 0), -c + HalfLength),
                new(new PlanarVector3(-AxisY, AxisX, 0), p + HalfWidth),
                new(new PlanarVector3(AxisY, -AxisX, 0), -p + HalfWidth),
            ];
        }
    }

    /// <summary>Точка плана внутри прямоугольника (или на границе в допуске).</summary>
    public bool Contains(double x, double y, double tol) => Faces.All(f => f.Eval(new PlanarVector3(x, y, 0)) <= tol);

    /// <summary>Углы прямоугольника против часовой стрелки.</summary>
    public IReadOnlyList<PlanarPoint2D> Corners()
    {
        PlanarPoint2D At(double s, double t) =>
            new(CenterX + AxisX * s - AxisY * t, CenterY + AxisY * s + AxisX * t);
        return [At(-HalfLength, -HalfWidth), At(HalfLength, -HalfWidth), At(HalfLength, HalfWidth), At(-HalfLength, HalfWidth)];
    }
}
