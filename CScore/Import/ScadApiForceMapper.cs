namespace CScore.Import;

/// <summary>
/// Усилия SCAD из SCADAPIX.dll (по кодам TypeUs) → строки наборов OpenCS. Оси, знаки и единицы — как у
/// xls-импорта (<see cref="ScadXlsForceMapper"/>); результаты DLL — в «т, м» (ApiInitResult), поэтому
/// LengthM = 1.
/// </summary>
public static class ScadApiForceMapper
{
    /// <summary>Коды TypeUs стержня.</summary>
    public const byte BarN = 0, BarMk = 1, BarMy = 4, BarQz = 5, BarMz = 6, BarQy = 7;

    /// <summary>Коды TypeUs пластины/оболочки (NX/NY/TXY — напряжения).</summary>
    public const byte ShellNx = 8, ShellNy = 9, ShellTxy = 11, ShellMx = 14, ShellMy = 15, ShellMxy = 16,
        ShellQx = 17, ShellQy = 18;

    /// <summary>Строка стержня; кода нет среди <paramref name="types"/> — значение 0.</summary>
    public static LoadItem MapBar(ReadOnlySpan<byte> types, ReadOnlySpan<double> values, ScadXlsImportOptions options) =>
        ScadXlsForceMapper.MapBar(
            Get(types, values, BarN), Get(types, values, BarMk), Get(types, values, BarMy),
            Get(types, values, BarQz), Get(types, values, BarMz), Get(types, values, BarQy), WithMeters(options));

    /// <summary>Строка пластины; кода нет среди <paramref name="types"/> — значение 0.</summary>
    public static ShellLoadItem MapShell(ReadOnlySpan<byte> types, ReadOnlySpan<double> values, ScadXlsImportOptions options) =>
        ScadXlsForceMapper.MapShell(
            Get(types, values, ShellNx), Get(types, values, ShellNy), Get(types, values, ShellTxy),
            Get(types, values, ShellMx), Get(types, values, ShellMy), Get(types, values, ShellMxy),
            Get(types, values, ShellQx), Get(types, values, ShellQy), WithMeters(options));

    /// <summary>Все усилия строки, которые переносятся в OpenCS, равны нулю (КЭ без результатов).</summary>
    public static bool IsZero(LoadItem i) => i.N == 0 && i.T == 0 && i.Mx == 0 && i.My == 0 && i.Vx == 0 && i.Vy == 0;

    /// <inheritdoc cref="IsZero(LoadItem)"/>
    public static bool IsZero(ShellLoadItem i) =>
        i.SigmaX == 0 && i.SigmaY == 0 && i.TauXY == 0 && i.Mx == 0 && i.My == 0 && i.Mxy == 0 && i.Qx == 0 && i.Qy == 0;

    static double Get(ReadOnlySpan<byte> types, ReadOnlySpan<double> values, byte code)
    {
        int i = types.IndexOf(code);
        return i >= 0 && i < values.Length ? values[i] : 0;
    }

    static ScadXlsImportOptions WithMeters(ScadXlsImportOptions o) =>
        o.LengthM == 1.0 ? o : o.WithUnits(o.TonToKnFactor, 1.0);
}
