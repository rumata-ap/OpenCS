namespace CScore.PlateStrip;

/// <summary>
/// Отклик плитного сечения, умноженный на долю материала в ячейке квадратуры по ширине полосы
/// (<see cref="StripMaterialProfile.CellFractions"/>): ячейка, частично перекрытая отверстием, даёт
/// пропорциональную часть усилий и жёсткостей (включая сдвиг). Доля 0 — ячейка целиком в отверстии.
/// </summary>
public sealed class ScaledPlateSectionResponse(IPlateSectionResponse inner, double factor) : IPlateSectionResponse
{
    /// <summary>Исходный отклик.</summary>
    public IPlateSectionResponse Inner => inner;

    /// <summary>Доля материала, 0…1.</summary>
    public double Factor => factor;

    /// <summary>Исходный отклик, если доля полная; иначе обёртка.</summary>
    public static IPlateSectionResponse Apply(IPlateSectionResponse source, double factor) =>
        factor >= 1.0 - 1e-12 ? source : new ScaledPlateSectionResponse(source, Math.Max(0.0, factor));

    /// <inheritdoc/>
    public EquivalentSectionSourceKind SourceKind => inner.SourceKind;

    /// <inheritdoc/>
    public string Fingerprint { get; } =
        inner.Fingerprint + "|material:" + factor.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public PlateResultants Forces(ShellStrainState state)
    {
        var f = inner.Forces(state);
        return new(f.Nx * factor, f.Ny * factor, f.Nxy * factor, f.Mx * factor, f.My * factor, f.Mxy * factor);
    }

    /// <inheritdoc/>
    public PlateShellTangentResult Tangent(ShellStrainState state)
    {
        var t = inner.Tangent(state);
        return new PlateShellTangentResult
        {
            Nx = t.Nx * factor, Ny = t.Ny * factor, Nxy = t.Nxy * factor,
            Mx = t.Mx * factor, My = t.My * factor, Mxy = t.Mxy * factor,
            A = Scale(t.A), B = Scale(t.B), D = Scale(t.D), As = Scale(t.As),
        };
    }

    double[,] Scale(double[,] m)
    {
        var r = new double[m.GetLength(0), m.GetLength(1)];
        for (int i = 0; i < r.GetLength(0); i++)
        for (int j = 0; j < r.GetLength(1); j++)
            r[i, j] = m[i, j] * factor;
        return r;
    }
}
