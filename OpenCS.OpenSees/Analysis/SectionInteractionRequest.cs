using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.Analysis;

/// <summary>Параметры последовательного расчёта одноосной диаграммы N-M.</summary>
public sealed class SectionInteractionRequest
{
    /// <summary>Упорядоченный список продольных сил в Н.</summary>
    public IReadOnlyList<double> AxialForcesN { get; init; } = [];

    /// <summary>Максимальная кривизна каждой внутренней moment-curvature задачи в 1/м.</summary>
    public double MaxCurvature { get; init; } = 0.01;

    /// <summary>Количество шагов кривизны каждой внутренней задачи.</summary>
    public int Increments { get; init; } = 20;

    /// <summary>Выбранная изгибающая компонента CScore.</summary>
    public SectionBendingAxis Axis { get; init; } = SectionBendingAxis.Mx;

    /// <summary>Соглашение координат модели.</summary>
    public OpenSeesCoordinateConvention Convention { get; init; } =
        OpenSeesCoordinateConvention.CScoreDefault;

    /// <summary>Проверяет список сил и общие параметры внутренних расчётов.</summary>
    public void Validate()
    {
        if (AxialForcesN.Count == 0 || AxialForcesN.Any(force => !double.IsFinite(force)))
            throw new ArgumentException("AxialForcesN must contain finite values.", nameof(AxialForcesN));
        if (AxialForcesN.Count != AxialForcesN.Distinct().Count())
            throw new ArgumentException("AxialForcesN must not contain duplicates.", nameof(AxialForcesN));
        if (!double.IsFinite(MaxCurvature) || MaxCurvature <= 0)
            throw new ArgumentException("MaxCurvature must be positive and finite.", nameof(MaxCurvature));
        if (Increments <= 0)
            throw new ArgumentException("Increments must be positive.", nameof(Increments));
    }
}
