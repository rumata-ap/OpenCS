using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.Analysis;

/// <summary>Параметры одного пространственного monotonic анализа секции.</summary>
public sealed class SpatialSectionAnalysisRequest
{
    /// <summary>Продольная сила в Н.</summary>
    public double AxialForceN { get; init; }

    /// <summary>Направление луча в градусах: 0° соответствует +Mx.</summary>
    public double AngleDegrees { get; init; }

    /// <summary>Максимальная длина радиального луча кривизны в 1/м.</summary>
    public double MaxCurvature { get; init; } = 0.01;

    /// <summary>Количество радиальных шагов.</summary>
    public int Increments { get; init; } = 20;

    /// <summary>Соглашение координат модели.</summary>
    public OpenSeesCoordinateConvention Convention { get; init; } =
        OpenSeesCoordinateConvention.CScoreDefault;

    /// <summary>Кривизна, сопряжённая с Mx, на конце луча.</summary>
    public double CurvatureMxAtMax => MaxCurvature * Math.Cos(AngleDegrees * Math.PI / 180.0);

    /// <summary>Кривизна, сопряжённая с My, на конце луча.</summary>
    public double CurvatureMyAtMax => MaxCurvature * Math.Sin(AngleDegrees * Math.PI / 180.0);

    /// <summary>Создаёт нормализованный запрос одного луча.</summary>
    public static SpatialSectionAnalysisRequest At(
        double axialForceN,
        double angleDegrees,
        double maxCurvature,
        int increments) => new()
        {
            AxialForceN = axialForceN,
            AngleDegrees = angleDegrees,
            MaxCurvature = maxCurvature,
            Increments = increments
        };

    /// <summary>Проверяет конечность и диапазоны параметров одного луча.</summary>
    public void Validate()
    {
        if (!double.IsFinite(AxialForceN))
            throw new ArgumentException("AxialForceN must be finite.", nameof(AxialForceN));
        if (!double.IsFinite(AngleDegrees) || AngleDegrees < 0 || AngleDegrees >= 360)
            throw new ArgumentException("AngleDegrees must be in [0, 360).", nameof(AngleDegrees));
        if (!double.IsFinite(MaxCurvature) || MaxCurvature <= 0)
            throw new ArgumentException("MaxCurvature must be positive and finite.", nameof(MaxCurvature));
        if (Increments <= 0)
            throw new ArgumentException("Increments must be positive.", nameof(Increments));
    }
}
