using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.Analysis;

/// <summary>Параметры одноосного анализа moment–curvature.</summary>
public sealed class SectionAnalysisRequest
{
    /// <summary>Продольная сила в Н.</summary>
    public double AxialForceN { get; init; }

    /// <summary>Приращение кривизны на одном шаге в 1/м.</summary>
    public double CurvatureStep { get; init; } = 0.0005;

    /// <summary>Максимальное число шагов до принудительной остановки.</summary>
    public int MaxSteps { get; init; } = 200;

    /// <summary>Выбранное направление изгиба в системе CScore.</summary>
    public SectionBendingAxis Axis { get; init; } = SectionBendingAxis.Mx;

    /// <summary>Соглашение координат модели.</summary>
    public OpenSeesCoordinateConvention Convention { get; init; } =
        OpenSeesCoordinateConvention.CScoreDefault;

    /// <summary>Проверяет положительность параметров пошагового анализа.</summary>
    public void Validate()
    {
        if (!double.IsFinite(AxialForceN) || !double.IsFinite(CurvatureStep) || CurvatureStep <= 0)
            throw new ArgumentException("CurvatureStep должно быть положительным и конечным.", nameof(CurvatureStep));
        if (MaxSteps <= 0)
            throw new ArgumentException("MaxSteps должно быть положительным.", nameof(MaxSteps));
    }
}

/// <summary>Изгибающая компонента CScore, которую анализирует backend.</summary>
public enum SectionBendingAxis
{
    Mx,
    My
}
