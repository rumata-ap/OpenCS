namespace CScore.PlateStrip;

/// <summary>Отклик полосы, редуцированной в стержневое сечение, на заданном состоянии:
/// внутренние усилия Q = ∫Bᵀσ dA и касательная K = ∫BᵀHB dA.</summary>
/// <param name="Q">Внутренние усилия [N, My, Mz].</param>
/// <param name="K">Касательная матрица 3×3 в тех же обобщённых деформациях.</param>
public readonly record struct StripSectionResponse(double[] Q, double[,] K);

/// <summary>
/// Состояние-зависимая редукция плитного отклика в стержневое сечение — ядро нелинейной
/// политики ConstitutiveIntegration (Срез 7). До Среза 7 та же арифметика выполнялась только в
/// нулевом состоянии внутри EquivalentSectionCalculator; здесь состояние стало параметром, а
/// линейная сборка Среза 2 вызывает то же тело с BeamStrainState.Zero — поэтому её результат
/// не изменился ни на бит.
///
/// Q и K берутся из <b>одного</b> вызова IPlateSectionResponse.Tangent на точку ширины:
/// PlateShellTangentResult несёт и блоки A/B/D, и усилия Nx..Mxy, так что отдельный вызов
/// Forces (и тем более отдельный проход StripResultantIntegrator) в цикле Ньютона не нужен.
/// Тождественность Q эталонной формуле Среза 3a закреплена тестом.
///
/// Внутренний расчётный примитив: некорректные входы приводят к исключению, а не к диагностике.
/// </summary>
public static class NonlinearStripSection
{
    /// <summary>Вычислить усилия и касательную полосы в заданном состоянии балки.
    /// widthSources[i] обязан соответствовать i-й точке
    /// EquivalentSectionCalculator.WidthGaussPoints (v по возрастанию от -width/2).</summary>
    public static StripSectionResponse Evaluate(
        double width,
        IReadOnlyList<IPlateSectionResponse> widthSources,
        BeamStrainState beamState)
    {
        ArgumentNullException.ThrowIfNull(widthSources);
        if (widthSources.Count < 1)
            throw new ArgumentException("Список источников по ширине не должен быть пустым.", nameof(widthSources));
        Validate(width, beamState);

        var embedding = new StripKinematicEmbedding(width);
        var (vs, weights) = EquivalentSectionCalculator.WidthGaussPoints(width, widthSources.Count);

        var q = new double[3];
        var k = new double[3, 3];
        for (int g = 0; g < vs.Length; g++)
        {
            double v = vs[g];
            double weight = weights[g];
            var b = embedding.Matrix(v);
            var shellState = embedding.Map(beamState, v);
            var tangent = widthSources[g].Tangent(shellState);
            var h = PlateSectionResponseMath.BuildH(tangent.A, tangent.B, tangent.D);
            double[] forces = [tangent.Nx, tangent.Ny, tangent.Nxy, tangent.Mx, tangent.My, tangent.Mxy];

            for (int a = 0; a < 3; a++)
            {
                double qValue = 0.0;
                for (int i = 0; i < 6; i++)
                    qValue += b[i, a] * forces[i];
                q[a] += weight * qValue;

                for (int c = 0; c < 3; c++)
                {
                    double kValue = 0.0;
                    for (int i = 0; i < 6; i++)
                    for (int j = 0; j < 6; j++)
                        kValue += b[i, a] * h[i, j] * b[j, c];
                    k[a, c] += weight * kValue;
                }
            }
        }
        return new(q, k);
    }

    /// <summary>Только касательная — тело, вынесенное из EquivalentSectionCalculator.Integrate
    /// в Срезе 7. Линейная сборка Среза 2 вызывает его с нулевым состоянием, поэтому
    /// существующая арифметика сохранена дословно.</summary>
    internal static double[,] IntegrateTangent(
        IReadOnlyList<IPlateSectionResponse> sources,
        double width,
        int pointCount,
        BeamStrainState beamState)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count != pointCount)
            throw new ArgumentException(
                "Число источников по ширине должно совпадать с числом точек квадратуры.", nameof(sources));
        Validate(width, beamState);

        var embedding = new StripKinematicEmbedding(width);
        var result = new double[3, 3];
        var (vs, weights) = EquivalentSectionCalculator.WidthGaussPoints(width, pointCount);
        for (int g = 0; g < vs.Length; g++)
        {
            double v = vs[g];
            double weight = weights[g];
            var b = embedding.Matrix(v);
            var shellState = embedding.Map(beamState, v);
            var tangent = sources[g].Tangent(shellState);
            var h = PlateSectionResponseMath.BuildH(tangent.A, tangent.B, tangent.D);
            for (int a = 0; a < 3; a++)
            for (int c = 0; c < 3; c++)
            {
                double value = 0.0;
                for (int i = 0; i < 6; i++)
                for (int j = 0; j < 6; j++)
                    value += b[i, a] * h[i, j] * b[j, c];
                result[a, c] += weight * value;
            }
        }
        return result;
    }

    static void Validate(double width, BeamStrainState beamState)
    {
        if (!(width > 0.0) || !double.IsFinite(width))
            throw new ArgumentOutOfRangeException(nameof(width),
                "Ширина полосы должна быть конечной и положительной.");
        if (!double.IsFinite(beamState.Eps0) || !double.IsFinite(beamState.KappaY) ||
            !double.IsFinite(beamState.KappaZ))
            throw new ArgumentException("Состояние деформации балки должно быть конечным.", nameof(beamState));
    }
}
