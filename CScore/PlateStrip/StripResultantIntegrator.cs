namespace CScore.PlateStrip;

/// <summary>Прямое интегрирование плитных усилий IPlateSectionResponse.Forces по ширине
/// полосы в [N, My, Mz] — та же кинематика/квадратура (StripKinematicEmbedding,
/// EquivalentSectionCalculator.WidthGaussPoints), что EquivalentSectionCalculator использует
/// для сборки KBeam, применённая к усилиям, а не к касательной. widthSources[i] обязан
/// соответствовать i-й точке WidthGaussPoints (v по возрастанию от -width/2 к +width/2).
/// Внутренний расчётный примитив: некорректные входы (неположительная/нечисловая ширина,
/// null/пустой widthSources, нечисловое beamState) приводят к исключению, а не к диагностике —
/// вызывающая сторона (EquivalentSectionControlCheck) обязана провалидировать входы заранее.</summary>
public static class StripResultantIntegrator
{
    public static double[] Integrate(
        double width,
        IReadOnlyList<IPlateSectionResponse> widthSources,
        BeamStrainState beamState)
    {
        if (!(width > 0.0) || !double.IsFinite(width))
            throw new ArgumentOutOfRangeException(nameof(width),
                "Ширина полосы должна быть конечной и положительной.");
        ArgumentNullException.ThrowIfNull(widthSources);
        if (widthSources.Count < 1)
            throw new ArgumentException("Список источников по ширине не должен быть пустым.", nameof(widthSources));
        if (!double.IsFinite(beamState.Eps0) || !double.IsFinite(beamState.KappaY) || !double.IsFinite(beamState.KappaZ))
            throw new ArgumentException("Состояние деформации балки должно быть конечным.", nameof(beamState));

        var embedding = new StripKinematicEmbedding(width);
        var (vs, weights) = EquivalentSectionCalculator.WidthGaussPoints(width, widthSources.Count);
        var result = new double[3];
        for (int g = 0; g < vs.Length; g++)
        {
            double v = vs[g];
            double weight = weights[g];
            var b = embedding.Matrix(v);
            var shellState = embedding.Map(beamState, v);
            var forces = widthSources[g].Forces(shellState).ToArray();
            for (int a = 0; a < 3; a++)
            {
                double value = 0.0;
                for (int i = 0; i < 6; i++)
                    value += b[i, a] * forces[i];
                result[a] += weight * value;
            }
        }
        return result;
    }
}
