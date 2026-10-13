namespace CScore.PlateStrip;

/// <summary>Отклик сечения полосы для формулировки Тимошенко (Срез 8b).</summary>
/// <param name="Q">Усилия [N, My, Mz, T] при деформациях [ε₀, κy, κz, θx′].</param>
/// <param name="K">Касательная 4×4 в тех же обобщённых деформациях.</param>
/// <param name="GAy">Сдвиговая жёсткость в плоскости полосы (Vy), кН.</param>
/// <param name="GAz">Сдвиговая жёсткость из плоскости (Vz), кН.</param>
public readonly record struct StripTimoshenkoSectionResponse(
    double[] Q, double[,] K, double GAy, double GAz);

/// <summary>Закон сечения полосы по элементам: источник усилий, касательной и сдвиговой жёсткости
/// для элемента Тимошенко. Реализации: линейная (постоянная матрица) и нелинейная
/// (интегрирование плитного отклика по ширине).</summary>
public interface IStripTimoshenkoSectionLaw
{
    /// <summary>Число элементов, на которое рассчитан закон.</summary>
    int ElementCount { get; }

    /// <summary>Ширина полосы, м.</summary>
    double WidthM { get; }

    /// <summary>Отклик элемента <paramref name="element"/> при заданных деформациях.</summary>
    StripTimoshenkoSectionResponse Evaluate(
        int element, double eps0, double kappaY, double kappaZ, double twist);
}

/// <summary>Коэффициент сдвига прямоугольного сечения κs = 5/6 (Тимошенко/Рейсснер).</summary>
public static class StripShear
{
    public const double RectangleCorrection = 5.0 / 6.0;
}

/// <summary>Линейный закон: постоянные матрица 3×3, GJ и сдвиговые жёсткости на всех элементах.</summary>
public sealed class LinearStripTimoshenkoSectionLaw : IStripTimoshenkoSectionLaw
{
    readonly double[,] _k4 = new double[4, 4];
    readonly double _gaY;
    readonly double _gaZ;

    public LinearStripTimoshenkoSectionLaw(
        double[,] tangent3, double gj, double gaY, double gaZ, int elementCount, double widthM = 1.0)
    {
        ArgumentNullException.ThrowIfNull(tangent3);
        if (tangent3.GetLength(0) != 3 || tangent3.GetLength(1) != 3)
            throw new ArgumentException("Матрица сечения должна быть 3×3.", nameof(tangent3));
        if (elementCount < 1)
            throw new ArgumentOutOfRangeException(nameof(elementCount));
        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
        {
            if (!double.IsFinite(tangent3[i, j]))
                throw new ArgumentException("Матрица сечения должна быть конечной.", nameof(tangent3));
            _k4[i, j] = tangent3[i, j];
        }
        if (!double.IsFinite(gj) || gj < 0.0)
            throw new ArgumentOutOfRangeException(nameof(gj), "GJ должна быть конечной и неотрицательной.");
        _k4[3, 3] = gj;
        _gaY = gaY;
        _gaZ = gaZ;
        ElementCount = elementCount;
        WidthM = widthM;
    }

    public int ElementCount { get; }
    public double WidthM { get; }

    public StripTimoshenkoSectionResponse Evaluate(
        int element, double eps0, double kappaY, double kappaZ, double twist)
    {
        double[] e = [eps0, kappaY, kappaZ, twist];
        var q = new double[4];
        for (int i = 0; i < 4; i++)
        for (int j = 0; j < 4; j++)
            q[i] += _k4[i, j] * e[j];
        return new(q, (double[,])_k4.Clone(), _gaY, _gaZ);
    }
}

/// <summary>Нелинейный закон: четырёхкомпонентное интегрирование плитного отклика по ширине
/// (<see cref="StripKinematicEmbedding.MatrixWithTwist"/>), сдвиг — GA = κs·b·∫A₃₃ по текущему
/// состоянию слоёв (секущая деградация мембранного G·h).</summary>
public sealed class NonlinearStripTimoshenkoSectionLaw(
    StripSectionSourceGrid sources, double widthM) : IStripTimoshenkoSectionLaw
{
    public int ElementCount => sources.ElementCount;
    public double WidthM { get; } = widthM;

    public StripTimoshenkoSectionResponse Evaluate(
        int element, double eps0, double kappaY, double kappaZ, double twist) =>
        Integrate(WidthM, sources[element], new BeamStrainState(eps0, kappaY, kappaZ), twist, retained: null);

    /// <summary>Интегрирование по ширине. <paramref name="retained"/> (индексы плитных
    /// деформаций) включает статическую конденсацию остальных — политика EnergyEquivalent.</summary>
    internal static StripTimoshenkoSectionResponse Integrate(
        double width, IReadOnlyList<IPlateSectionResponse> widthSources,
        BeamStrainState beam, double twist, int[]? retained)
    {
        var embedding = new StripKinematicEmbedding(width);
        var (vs, weights) = EquivalentSectionCalculator.WidthGaussPoints(width, widthSources.Count);

        var q = new double[4];
        var k = new double[4, 4];
        double gaSum = 0.0;
        for (int g = 0; g < vs.Length; g++)
        {
            var b = embedding.MatrixWithTwist(vs[g]);
            var shell = embedding.MapWithTwist(beam, twist, vs[g]);
            var tangent = widthSources[g].Tangent(shell);
            var h = PlateSectionResponseMath.BuildH(tangent.A, tangent.B, tangent.D);
            double[] forces = [tangent.Nx, tangent.Ny, tangent.Nxy, tangent.Mx, tangent.My, tangent.Mxy];
            gaSum += weights[g] * h[2, 2];

            if (retained != null)
            {
                if (!PlateSectionResponseMath.TryCondense(h, retained, out h))
                    throw new InvalidOperationException("Конденсируемый блок плитной жёсткости вырожден.");
                // усилия по удерживаемым деформациям не меняются: конденсированные усилия — нулевые
                for (int i = 0; i < 6; i++)
                    if (Array.IndexOf(retained, i) < 0) forces[i] = 0.0;
            }

            for (int a = 0; a < 4; a++)
            {
                double qv = 0.0;
                for (int i = 0; i < 6; i++) qv += b[i, a] * forces[i];
                q[a] += weights[g] * qv;
                for (int c = 0; c < 4; c++)
                {
                    double kv = 0.0;
                    for (int i = 0; i < 6; i++)
                    for (int j = 0; j < 6; j++)
                        kv += b[i, a] * h[i, j] * b[j, c];
                    k[a, c] += weights[g] * kv;
                }
            }
        }
        double ga = StripShear.RectangleCorrection * gaSum;
        return new(q, k, ga, ga);
    }
}
