using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.Analysis;

/// <summary>Параметры одного пространственного monotonic анализа секции.</summary>
public sealed class SpatialSectionAnalysisRequest
{
    /// <summary>Продольная сила в Н.</summary>
    public double AxialForceN { get; init; }

    /// <summary>Направление луча в градусах: 0° соответствует +Mx.</summary>
    public double AngleDegrees { get; init; }

    /// <summary>Приращение кривизны радиального луча на одном шаге в 1/м.</summary>
    public double CurvatureStep { get; init; } = 0.0005;

    /// <summary>Максимальное число радиальных шагов.</summary>
    public int MaxSteps { get; init; } = 200;

    /// <summary>Соглашение координат модели.</summary>
    public OpenSeesCoordinateConvention Convention { get; init; } =
        OpenSeesCoordinateConvention.CScoreDefault;

    /// <summary>Кривизна Mx на одном шаге луча.</summary>
    public double CurvatureMxStep => CurvatureStep * Math.Cos(AngleDegrees * Math.PI / 180.0);

    /// <summary>Кривизна My на одном шаге луча.</summary>
    public double CurvatureMyStep => CurvatureStep * Math.Sin(AngleDegrees * Math.PI / 180.0);

    /// <summary>Создаёт нормализованный запрос одного луча.</summary>
    public static SpatialSectionAnalysisRequest At(
        double axialForceN,
        double angleDegrees,
        double curvatureStep,
        int maxSteps) => new()
        {
            AxialForceN = axialForceN,
            AngleDegrees = angleDegrees,
            CurvatureStep = curvatureStep,
            MaxSteps = maxSteps
        };

    /// <summary>Проверяет конечность и диапазоны параметров одного луча.</summary>
    public void Validate()
    {
        if (!double.IsFinite(AxialForceN))
            throw new ArgumentException("AxialForceN must be finite.", nameof(AxialForceN));
        if (!double.IsFinite(AngleDegrees) || AngleDegrees < 0 || AngleDegrees >= 360)
            throw new ArgumentException("AngleDegrees must be in [0, 360).", nameof(AngleDegrees));
        if (!double.IsFinite(CurvatureStep) || CurvatureStep <= 0)
            throw new ArgumentException("CurvatureStep must be positive and finite.", nameof(CurvatureStep));
        if (MaxSteps <= 0)
            throw new ArgumentException("MaxSteps must be positive.", nameof(MaxSteps));
    }
}
