namespace CSfea.Core;

/// <summary>
/// Стержневое сечение с замороженной связанной секущей матрицей КЭ: (N, M_y, M_z) = S·(ε₀, κ_y, κ_z),
/// касательная — та же S, кручение — упругое GJ. Секущий расчёт (Пикар) пересобирает S между
/// итерациями через <see cref="Update"/> (обычно по средней податливости трёх сечений Лобатто,
/// <see cref="BeamElements.MeanCompliance"/>). Матрица — в осях КЭ, единицы CSfea (Н, м).
/// </summary>
public sealed class SecantBeamResponse : IBeamSectionResponse
{
    private double[,] _s;

    public SecantBeamResponse(double[,] s, double gj)
    {
        _s = Validate(s);
        GJ = gj;
    }

    /// <summary>Текущая секущая матрица 3×3.</summary>
    public double[,] Matrix => _s;

    /// <summary>Жёсткость кручения (упругая).</summary>
    public double GJ { get; }

    /// <summary>Заменить секущую матрицу.</summary>
    public void Update(double[,] s) => _s = Validate(s);

    public BeamForces Forces(double eps0, double kappaY, double kappaZ)
        => new(_s[0, 0] * eps0 + _s[0, 1] * kappaY + _s[0, 2] * kappaZ,
               _s[1, 0] * eps0 + _s[1, 1] * kappaY + _s[1, 2] * kappaZ,
               _s[2, 0] * eps0 + _s[2, 1] * kappaY + _s[2, 2] * kappaZ);

    public double[,] Tangent(double eps0, double kappaY, double kappaZ) => _s;

    public (double EA, double EIy, double EIz) Secant(double eps0, double kappaY, double kappaZ)
        => (_s[0, 0], _s[1, 1], _s[2, 2]);

    public double TorsionalStiffness(double twist = 0.0) => GJ;

    public void Commit() { }

    public void Reset() { }

    private static double[,] Validate(double[,] s)
    {
        if (s == null || s.GetLength(0) != 3 || s.GetLength(1) != 3)
            throw new ArgumentException("Секущая матрица стержня должна быть 3×3");
        return s;
    }
}
