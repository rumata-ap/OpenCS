namespace CScore.PlateStrip;

/// <summary>
/// Отображение трёх обобщённых деформаций полосы в шесть деформаций плитного сечения.
/// Поперечная координата v отсчитывается от средней линии вдоль StripFrame.LocalY.
/// </summary>
public sealed class StripKinematicEmbedding
{
    /// <summary>Номинальная ширина полосы, м.</summary>
    public double WidthM { get; }

    public StripKinematicEmbedding(double widthM)
    {
        if (!(widthM > 0.0) || !double.IsFinite(widthM))
            throw new ArgumentOutOfRangeException(nameof(widthM), "Ширина полосы должна быть конечной и положительной.");
        WidthM = widthM;
    }

    /// <summary>
    /// Отобразить [eps0, kappaY, kappaZ] в [eps0x, eps0y, gamma0xy, Kx, Ky, Kxy].
    /// </summary>
    public ShellStrainState Map(BeamStrainState beam, double v)
    {
        ValidateCoordinate(v);
        return new ShellStrainState(
            beam.Eps0 - beam.KappaZ * v,
            0.0,
            0.0,
            beam.KappaY,
            0.0,
            0.0);
    }

    /// <summary>Матрица B(v) размера 6x3 для виртуальной работы.</summary>
    public double[,] Matrix(double v)
    {
        ValidateCoordinate(v);
        var b = new double[6, 3];
        b[0, 0] = 1.0;
        b[0, 2] = -v;
        b[3, 1] = 1.0;
        return b;
    }

    void ValidateCoordinate(double v)
    {
        if (!double.IsFinite(v))
            throw new ArgumentOutOfRangeException(nameof(v), "Поперечная координата должна быть конечной.");
    }
}
