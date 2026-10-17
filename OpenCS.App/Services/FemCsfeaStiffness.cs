using CSfea.CScoreBridge.Structural;

namespace OpenCS.Services;

/// <summary>
/// Величина жёсткости КЭ по блоку жёсткостей полей шага CSfea (<see cref="RcSecantStepFields.HasStiffness"/>): у
/// пластин — изгибные и мембранные модули в осях выдачи усилий, у стержней — жёсткости сечения в точках ξ = 0, ½, 1.
/// </summary>
public enum FemCsfeaStiffness
{
    BendE1, BendE2, BendNu12, BendNu21, BendG,
    MembE1, MembE2, MembNu12, MembNu21, MembG,
    BarEA, BarEIy, BarEIz, BarGAy, BarGAz,
}

/// <summary>Что показывать: начальную жёсткость, секущую или их отношение.</summary>
public enum FemCsfeaStiffnessMode { Initial, Secant, Ratio }

/// <summary>
/// Жёсткости КЭ из полей шага CSfea (спека НДС сечений, п. 2.2). Пластина: изгибные E1 = 12·D11/h³, E2 = 12·D22/h³,
/// G = 12·D66/h³, ν12 = D12/D22, ν21 = D12/D11; мембранные E1 = A11/h, E2 = A22/h, G = A66/h, ν12 = A12/A22,
/// ν21 = A12/A11 — члены матрицы в осях выдачи усилий (при угле оси выдачи к арматуре — диагональ повёрнутой матрицы, а
/// не модули ортотропии). Стержень: EA, EIy, EIz — диагональ секущей матрицы сечения точки, GAy, GAz — КЭ. Модули — МПа,
/// EA и GA — МН, EI — МН·м², отношения — безразмерные.
/// </summary>
public static class FemCsfeaStiffnessValues
{
    public static bool IsShell(FemCsfeaStiffness q) => q <= FemCsfeaStiffness.MembG;
    public static bool IsBar(FemCsfeaStiffness q) => q >= FemCsfeaStiffness.BarEA;

    public static IReadOnlyList<FemCsfeaStiffness> ShellQuantities { get; } =
        Enum.GetValues<FemCsfeaStiffness>().Where(IsShell).ToArray();

    public static IReadOnlyList<FemCsfeaStiffness> BarQuantities { get; } =
        Enum.GetValues<FemCsfeaStiffness>().Where(IsBar).ToArray();

    /// <summary>Значение у пластины с индексом <paramref name="index"/> полей; null — нет блока, толщины или деления на ноль.</summary>
    public static double? Shell(RcSecantStepFields f, int index, FemCsfeaStiffness q, FemCsfeaStiffnessMode mode)
    {
        if (!f.HasStiffness || !IsShell(q)) return null;
        int o = index * RcSecantStepFields.ShellStiffnessComponents;
        if (o + RcSecantStepFields.ShellStiffnessComponents > f.ShellStiffness.Length) return null;
        double h = f.ShellStiffness[o + 16];
        double? Of(int offset) => ShellTerm(f.ShellStiffness, o + offset, q, h);
        return Combine(Of(0), Of(8), mode, q is FemCsfeaStiffness.BendNu12 or FemCsfeaStiffness.BendNu21
            or FemCsfeaStiffness.MembNu12 or FemCsfeaStiffness.MembNu21 ? 1.0 : 1e-6);
    }

    /// <summary>
    /// Значения у стержня в точках ξ = 0, ½, 1; null — нет блока или величина не определена (GA без сдвиговых
    /// деформаций — бесконечна).
    /// </summary>
    public static (double I, double M, double J)? Bar(RcSecantStepFields f, int index, FemCsfeaStiffness q, FemCsfeaStiffnessMode mode)
    {
        if (!f.HasStiffness || !IsBar(q)) return null;
        int o = index * RcSecantStepFields.BeamStiffnessComponents;
        if (o + RcSecantStepFields.BeamStiffnessComponents > f.BeamStiffness.Length) return null;
        int c = q - FemCsfeaStiffness.BarEA;
        var k = f.BeamStiffness;
        double? At(int p) => Combine(Finite(k[o + 5 * p + c]), Finite(k[o + 15 + c]), mode, 1e-6);
        return At(0) is double i && At(1) is double m && At(2) is double j ? (i, m, j) : null;
    }

    /// <summary>Значение КЭ стержня для раскраски: наименьшее по точкам или среднее по длине (Симпсон).</summary>
    public static double Aggregate((double I, double M, double J) v, bool mean) =>
        mean ? (v.I + 4 * v.M + v.J) / 6 : Math.Min(v.I, Math.Min(v.M, v.J));

    /// <summary>Ключ строки единицы величины в режиме (МПа, МН, МН·м²); у отношений и коэффициентов Пуассона — пусто.</summary>
    public static string Unit(FemCsfeaStiffness q, FemCsfeaStiffnessMode mode)
    {
        if (mode == FemCsfeaStiffnessMode.Ratio) return "";
        return q switch
        {
            FemCsfeaStiffness.BendNu12 or FemCsfeaStiffness.BendNu21 or FemCsfeaStiffness.MembNu12 or FemCsfeaStiffness.MembNu21 => "",
            FemCsfeaStiffness.BarEA or FemCsfeaStiffness.BarGAy or FemCsfeaStiffness.BarGAz => "UnitMN",
            FemCsfeaStiffness.BarEIy or FemCsfeaStiffness.BarEIz => "UnitMNm2",
            _ => "Unit_MPa",
        };
    }

    /// <summary>Обозначение величины: «E1», «ν12», «EIy»; с индексом 0 у начальной, «/…₀» у отношения.</summary>
    public static string Symbol(FemCsfeaStiffness q, FemCsfeaStiffnessMode mode)
    {
        string s = q switch
        {
            FemCsfeaStiffness.BendE1 or FemCsfeaStiffness.MembE1 => "E1",
            FemCsfeaStiffness.BendE2 or FemCsfeaStiffness.MembE2 => "E2",
            FemCsfeaStiffness.BendNu12 or FemCsfeaStiffness.MembNu12 => "ν12",
            FemCsfeaStiffness.BendNu21 or FemCsfeaStiffness.MembNu21 => "ν21",
            FemCsfeaStiffness.BendG or FemCsfeaStiffness.MembG => "G",
            _ => q.ToString()[3..],
        };
        return mode switch
        {
            FemCsfeaStiffnessMode.Initial => s + "₀",
            FemCsfeaStiffnessMode.Ratio => $"{s}/{s}₀",
            _ => s,
        };
    }

    static double? Finite(double v) => double.IsFinite(v) ? v : null;

    static double? Combine(double? secant, double? initial, FemCsfeaStiffnessMode mode, double scale) => mode switch
    {
        FemCsfeaStiffnessMode.Initial => initial * scale,
        FemCsfeaStiffnessMode.Secant => secant * scale,
        _ => secant is double s && initial is double i && i != 0 ? s / i : null,
    };

    /// <summary>Модуль или коэффициент Пуассона по 8 членам A11, A22, A12, A66, D11, D22, D12, D66 с <paramref name="o"/>.</summary>
    static double? ShellTerm(double[] k, int o, FemCsfeaStiffness q, double h)
    {
        double a11 = k[o], a22 = k[o + 1], a12 = k[o + 2], a66 = k[o + 3], d11 = k[o + 4], d22 = k[o + 5], d12 = k[o + 6], d66 = k[o + 7];
        double? Div(double a, double b) => b != 0 && double.IsFinite(a / b) ? a / b : null;
        double bend = h > 0 ? 12 / (h * h * h) : double.NaN, memb = h > 0 ? 1 / h : double.NaN;
        double? v = q switch
        {
            FemCsfeaStiffness.BendE1 => d11 * bend,
            FemCsfeaStiffness.BendE2 => d22 * bend,
            FemCsfeaStiffness.BendG => d66 * bend,
            FemCsfeaStiffness.BendNu12 => Div(d12, d22),
            FemCsfeaStiffness.BendNu21 => Div(d12, d11),
            FemCsfeaStiffness.MembE1 => a11 * memb,
            FemCsfeaStiffness.MembE2 => a22 * memb,
            FemCsfeaStiffness.MembG => a66 * memb,
            FemCsfeaStiffness.MembNu12 => Div(a12, a22),
            FemCsfeaStiffness.MembNu21 => Div(a12, a11),
            _ => null,
        };
        return v is double d && double.IsFinite(d) ? d : null;
    }
}
