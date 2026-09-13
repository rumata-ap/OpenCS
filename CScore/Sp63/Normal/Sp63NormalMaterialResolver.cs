using CScore.Sp63;

namespace CScore.Sp63.Normal;

/// <summary>Разрешает расчётные характеристики бетона и арматуры для формульных проверок.</summary>
internal static class Sp63NormalMaterialResolver
{
    /// <summary>Расчётные характеристики материалов формульной проверки.</summary>
    public readonly record struct MaterialValues(double Rb, double Rs, double Rsc,
        double Es, double EpsilonB2);

    /// <summary>
    /// Пытается получить Rb/Es/εb2 бетона и Rs/Rsc арматуры по виду расчёта.
    /// Сопротивления арматуры передаются вызывающим кодом (они уже определены профилем).
    /// </summary>
    public static bool TryResolve(CrossSection section, CalcType calc,
        double tensionRs, double compressionRsc,
        out MaterialValues values, out Sp63NormalMessage? message)
    {
        var concrete = section.Areas.FirstOrDefault(area =>
            area.Category == AreaCategory.Region &&
            area.Material?.Type == MatType.Concrete);
        var rebar = section.Areas.FirstOrDefault(area =>
            area.Category == AreaCategory.RebarGroup && area.Material != null);
        var concreteChars = concrete?.Material?.GetChars(calc);
        var rebarChars = rebar?.Material?.GetChars(calc);
        double rb = Math.Abs(concreteChars?.Fc ?? 0.0);
        double es = rebarChars?.E ?? 0.0;
        double epsilonB2 = Math.Abs(concreteChars?.Ec2 ?? 0.0);
        if (!IsPositiveFinite(rb))
            return Failure("missing_concrete_resistance",
                "Sp63Normal_MissingConcreteResistance", "8.1.8", out values, out message);
        if (!IsPositiveFinite(es))
            return Failure("missing_rebar_modulus",
                "Sp63Normal_MissingRebarModulus", "8.1.8", out values, out message);
        if (!IsPositiveFinite(epsilonB2))
            return Failure("missing_concrete_strain",
                "Sp63Normal_MissingConcreteStrain", "8.1.8", out values, out message);

        values = new MaterialValues(rb, tensionRs, compressionRsc, es, epsilonB2);
        message = null;
        return true;
    }

    static bool Failure(string code, string text, string reference,
        out MaterialValues values, out Sp63NormalMessage? message)
    {
        values = default;
        message = new Sp63NormalMessage(code, Sp63NormalMessageKind.Applicability,
            reference, text);
        return false;
    }

    static bool IsPositiveFinite(double value) => double.IsFinite(value) && value > 0;
}
