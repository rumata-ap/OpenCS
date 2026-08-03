using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.Analysis;

/// <summary>Параметры последовательного расчёта одноосной диаграммы N-M.</summary>
public sealed class SectionInteractionRequest
{
    /// <summary>Упорядоченный список продольных сил в Н.</summary>
    public IReadOnlyList<double> AxialForcesN { get; init; } = [];

    /// <summary>Приращение кривизны каждой внутренней задачи на одном шаге в 1/м.</summary>
    public double CurvatureStep { get; init; } = 0.0005;

    /// <summary>Максимальное число шагов каждой внутренней задачи.</summary>
    public int MaxSteps { get; init; } = 200;

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
        if (!double.IsFinite(CurvatureStep) || CurvatureStep <= 0)
            throw new ArgumentException("CurvatureStep must be positive and finite.", nameof(CurvatureStep));
        if (MaxSteps <= 0)
            throw new ArgumentException("MaxSteps must be positive.", nameof(MaxSteps));
    }
}
