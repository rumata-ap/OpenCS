using System;
using System.Collections.Generic;

namespace CScore.Import;

/// <summary>
/// Разбор единиц измерения силы/длины из текстовых меток экспортов ЛИРА/SCAD
/// в единый масштаб к базе (кН, м). Числитель — сила (или момент как произведение
/// силы и длины), знаменатель — длина (для погонных величин/напряжений).
/// </summary>
public static class UnitTokens
{
    static readonly Dictionary<string, double> LengthFactors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["мм"] = 0.001,
        ["см"] = 0.01,
        ["дм"] = 0.1,
        ["м"]  = 1.0,
    };

    /// <summary>Коэффициент перевода 1 единицы силы в кН. Null — токен не распознан.</summary>
    public static double? ForceToKn(string token, double tonFactor) => token.Trim().ToLowerInvariant() switch
    {
        "н"                 => 1e-3,
        "кг" or "кгс"       => tonFactor / 1e3,
        "т"  or "тс"        => tonFactor,
        "кн"                => 1.0,
        "мн"                => 1000.0,
        "фунт" or "lbf"     => 0.0044482216,
        "kips" or "kip"     => 4.4482216,
        _                   => null,
    };

    /// <summary>Коэффициент перевода 1 единицы длины в метры. Null — токен не распознан.</summary>
    public static double? LengthToM(string token)
        => LengthFactors.TryGetValue(token.Trim(), out var f) ? f : null;

    /// <summary>
    /// Разбирает составное выражение единиц («т/м2», «(т*м)/м», «кг/см2», «т*м*м») в масштаб
    /// к базе (кН, м): множители числителя (разделены «*») перемножаются, знаменателя (после
    /// «/», разделены «*») делят результат. Число сразу после буквенного токена — степень
    /// («м2» = м²). Null, если хоть один токен не распознан или выражение пустое.
    /// </summary>
    public static double? ParseCompoundToKnBase(string expr, double tonFactor)
    {
        if (string.IsNullOrWhiteSpace(expr)) return null;
        string cleaned = expr.Replace("(", "").Replace(")", "").Trim();
        string[] parts = cleaned.Split('/', 2);

        double? num = ParseProduct(parts[0], tonFactor);
        if (num is null) return null;
        if (parts.Length == 1) return num;

        double? den = ParseProduct(parts[1], tonFactor);
        if (den is null || den == 0) return null;
        return num / den;
    }

    static double? ParseProduct(string s, double tonFactor)
    {
        var tokens = s.Split('*', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return null;

        double result = 1.0;
        foreach (var raw in tokens)
        {
            var (unit, power) = SplitTrailingDigits(raw);
            double? factor = ForceToKn(unit, tonFactor) ?? LengthToM(unit);
            if (factor is null) return null;
            result *= Math.Pow(factor.Value, power);
        }
        return result;
    }

    static (string unit, int power) SplitTrailingDigits(string token)
    {
        int i = token.Length;
        while (i > 0 && char.IsDigit(token[i - 1])) i--;
        string unit = token[..i];
        string digits = token[i..];
        int power = digits.Length > 0 ? int.Parse(digits) : 1;
        return (unit, power);
    }
}
