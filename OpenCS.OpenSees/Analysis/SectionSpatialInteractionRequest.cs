using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.Analysis;

/// <summary>Запрос полной пространственной диаграммы N-Mx-My.</summary>
public sealed class SectionSpatialInteractionRequest
{
    /// <summary>Упорядоченный список продольных сил в Н.</summary>
    public IReadOnlyList<double> AxialForcesN { get; init; } = [];

    /// <summary>Количество равномерных промежуточных опорных срезов между границами N.</summary>
    public int AdditionalAxialSlices { get; init; } = 2;

    /// <summary>Точки исходного ForceSet, которые требуется проверить относительно поверхности.</summary>
    public IReadOnlyList<SpatialInteractionDemandPoint> DemandPoints { get; init; } = [];

    /// <summary>Шаг полного оборота лучей в градусах.</summary>
    public double AngleStepDegrees { get; init; } = 45;

    /// <summary>Максимальная длина каждого луча кривизны в 1/м.</summary>
    public double MaxCurvature { get; init; } = 0.01;

    /// <summary>Количество радиальных шагов каждого луча.</summary>
    public int Increments { get; init; } = 20;

    /// <summary>Соглашение координат модели.</summary>
    public OpenSeesCoordinateConvention Convention { get; init; } =
        OpenSeesCoordinateConvention.CScoreDefault;

    /// <summary>Проверяет силы и параметры полного оборота.</summary>
    public void Validate()
    {
        if (AxialForcesN.Count == 0 || AxialForcesN.Any(force => !double.IsFinite(force)))
            throw new ArgumentException("AxialForcesN must contain finite values.", nameof(AxialForcesN));
        if (AxialForcesN.Count != AxialForcesN.Distinct().Count())
            throw new ArgumentException("AxialForcesN must not contain duplicates.", nameof(AxialForcesN));
        if (AdditionalAxialSlices < 0)
            throw new ArgumentException("AdditionalAxialSlices must not be negative.", nameof(AdditionalAxialSlices));
        if (DemandPoints.Any(point =>
                !double.IsFinite(point.AxialForceN) ||
                !double.IsFinite(point.MomentMxNm) ||
                !double.IsFinite(point.MomentMyNm)))
            throw new ArgumentException("DemandPoints must contain finite values.", nameof(DemandPoints));
        if (!double.IsFinite(AngleStepDegrees) || AngleStepDegrees <= 0)
            throw new ArgumentException("AngleStepDegrees must be positive and finite.", nameof(AngleStepDegrees));
        double count = 360.0 / AngleStepDegrees;
        double roundedCount = Math.Round(count);
        if (roundedCount < 1 || Math.Abs(count - roundedCount) > 1e-9 * Math.Max(1, Math.Abs(count)))
            throw new ArgumentException("AngleStepDegrees must divide 360 degrees.", nameof(AngleStepDegrees));
        if (!double.IsFinite(MaxCurvature) || MaxCurvature <= 0)
            throw new ArgumentException("MaxCurvature must be positive and finite.", nameof(MaxCurvature));
        if (Increments <= 0)
            throw new ArgumentException("Increments must be positive.", nameof(Increments));
    }

    /// <summary>Генерирует углы от 0 до 360 градусов без дубликата 360.</summary>
    public IReadOnlyList<double> GenerateAnglesDegrees()
    {
        Validate();
        int count = checked((int)Math.Round(360.0 / AngleStepDegrees));
        return Enumerable.Range(0, count)
            .Select(index => Math.Round(index * AngleStepDegrees, 12))
            .ToArray();
    }
}
