using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.Analysis;

/// <summary>Параметры одноосного анализа moment–curvature.</summary>
public sealed class SectionAnalysisRequest
{
    /// <summary>Продольная сила в Н.</summary>
    public double AxialForceN { get; init; }

    /// <summary>Максимальная кривизна в 1/м.</summary>
    public double MaxCurvature { get; init; }

    /// <summary>Количество шагов кривизны.</summary>
    public int Increments { get; init; } = 20;

    /// <summary>Выбранное направление изгиба в системе CScore.</summary>
    public SectionBendingAxis Axis { get; init; } = SectionBendingAxis.Mx;

    /// <summary>Соглашение координат модели.</summary>
    public OpenSeesCoordinateConvention Convention { get; init; } =
        OpenSeesCoordinateConvention.CScoreDefault;

    /// <summary>Проверяет положительность параметров пошагового анализа.</summary>
    public void Validate()
    {
        if (!double.IsFinite(AxialForceN) || !double.IsFinite(MaxCurvature) || MaxCurvature <= 0)
            throw new ArgumentException("MaxCurvature должно быть положительным и конечным.", nameof(MaxCurvature));
        if (Increments <= 0)
            throw new ArgumentException("Increments должно быть положительным.", nameof(Increments));
    }
}

/// <summary>Изгибающая компонента CScore, которую анализирует backend.</summary>
public enum SectionBendingAxis
{
    Mx,
    My
}
