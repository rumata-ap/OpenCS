using System.Globalization;
using CScore.Combinations;

namespace CScore.Fem.Combinations;

/// <summary>Типы комбинаций загружений схемы — группы <c>CalcType</c> проверок и особое сочетание.</summary>
public static class FemCombinationTypes
{
    /// <summary>ПС1, полные нагрузки.</summary>
    public const string C = "C";
    /// <summary>ПС1, длительные нагрузки.</summary>
    public const string CL = "CL";
    /// <summary>ПС2, полные нагрузки.</summary>
    public const string N = "N";
    /// <summary>ПС2, длительные нагрузки.</summary>
    public const string NL = "NL";
    /// <summary>Особое сочетание (ПС1).</summary>
    public const string A = "A";

    public static IReadOnlyList<string> All { get; } = [C, CL, N, NL, A];

    /// <summary>Тип по сохранённому значению: прежние "fundamental"/"accidental" и пустое — C/A/C.</summary>
    public static string Normalize(string? type)
    {
        string? t = type?.Trim();
        return t switch
        {
            CL or N or NL or A => t,
            "accidental" => A,
            _ => C
        };
    }
}

/// <summary>«Авто»-коэффициент слагаемого: значение и множители подстановки (знак '×' или '÷' и число).</summary>
public sealed record FemAutoCoefficient(double Value, IReadOnlyList<(char Op, double Factor)> Factors)
{
    /// <summary>Подстановка вида «1,4·0,9 = 1,26» или «÷1,4·0,9 = 0,643»; без множителей — «1».</summary>
    public string Format(IFormatProvider culture)
    {
        string N(double v) => v.ToString("0.###", culture);
        if (Factors.Count == 0) return N(Value);
        var text = string.Concat(Factors.Select((f, i) => (f.Op == '÷' ? "÷" : i == 0 ? "" : "·") + N(f.Factor)));
        return Factors.Count == 1 && Factors[0].Op == '×' ? text : $"{text} = {N(Value)}";
    }
}

/// <summary>
/// Коэффициенты слагаемых комбинации по роли (спека 2026-10-14, п. 5.3–5.4): γf загружения и ψ из
/// <see cref="CombRules.Psi"/> с учётом группы C/CL/N/NL/A, вида величин нагрузок и доли длительной части.
/// </summary>
public static class FemCombinationCoefficients
{
    /// <summary>Вид нагрузки СП 20 загружения.</summary>
    public static NormLoadType LoadType(FemLoadCase loadCase) => loadCase.Sp20Type.ToLowerInvariant() switch
    {
        "permanent" => NormLoadType.Permanent,
        "long_term" => NormLoadType.LongTerm,
        "accidental" => NormLoadType.Accidental,
        _ => NormLoadType.ShortTerm
    };

    /// <summary>γf загружения; не задан — по умолчанию из настроек.</summary>
    public static double GammaF(FemLoadCase loadCase, Sp20GammaDefaults defaults) => loadCase.GammaFUnfav ?? LoadType(loadCase) switch
    {
        NormLoadType.Permanent => defaults.PermanentUnfav,
        NormLoadType.LongTerm => defaults.LongTermUnfav,
        NormLoadType.Accidental => defaults.AccidentalUnfav,
        _ => defaults.ShortTermUnfav
    };

    /// <summary>γf постоянного загружения при уменьшении веса (п. 7.3 СП 20); не задан — из настроек (0,9).</summary>
    public static double ReducedGammaF(FemLoadCase loadCase, Sp20GammaDefaults defaults) =>
        loadCase.GammaFFav ?? defaults.PermanentFav;

    /// <summary>Роль, допустимая для вида нагрузки: G — постоянная (или п. 7.3), A — особая, L/Q — по рангу.</summary>
    public static Sp20TermRole NormalizeRole(NormLoadType type, Sp20TermRole role) => type switch
    {
        NormLoadType.Permanent => role == Sp20TermRole.PermanentReduced ? role : Sp20TermRole.Permanent,
        NormLoadType.Accidental => Sp20TermRole.Accidental,
        _ => role is Sp20TermRole.Leading or Sp20TermRole.Second or Sp20TermRole.Other ? role : Sp20TermRole.Leading
    };

    /// <summary>
    /// Роль загружения, добавляемого в комбинацию: G — постоянная, A — особая, L/Q — по числу загружений того же
    /// вида, уже входящих в комбинацию (первое — основное, второе — второе, далее — остальные).
    /// </summary>
    public static Sp20TermRole DefaultRole(FemLoadCase loadCase, IEnumerable<FemLoadCase> alreadyInCombination)
    {
        var type = LoadType(loadCase);
        return type switch
        {
            NormLoadType.Permanent => Sp20TermRole.Permanent,
            NormLoadType.Accidental => Sp20TermRole.Accidental,
            _ => CombRules.RoleOfRank(alreadyInCombination.Count(lc => !ReferenceEquals(lc, loadCase) && LoadType(lc) == type))
        };
    }

    /// <summary>
    /// «Авто»-коэффициент: C/CL — ×γf (нормативные) или ×1 (расчётные); N/NL — ×1 или ÷γf; A — γf = 1, у
    /// кратковременных 0,5 (п. 6.5), у расчётных ÷γf; затем ×ψ по роли; в CL/NL кратковременные — × доля длительной
    /// части, особые не входят (0). В основных сочетаниях особые не входят (0).
    /// </summary>
    public static FemAutoCoefficient Auto(FemLoadCase loadCase, Sp20TermRole role, string? combinationType,
        Sp20LoadValueKind kind, Sp20GammaDefaults defaults)
    {
        var type = LoadType(loadCase);
        role = NormalizeRole(type, role);
        string group = FemCombinationTypes.Normalize(combinationType);
        bool accidentalComb = group == FemCombinationTypes.A;
        bool longTermGroup = group is FemCombinationTypes.CL or FemCombinationTypes.NL;
        var factors = new List<(char, double)>();

        if (type == NormLoadType.Accidental && !accidentalComb)
            return new FemAutoCoefficient(0, [('×', 0)]);

        double gammaF = GammaF(loadCase, defaults);
        if (accidentalComb)
        {
            if (type == NormLoadType.ShortTerm) factors.Add(('×', CombRules.AccidentalShortTermGamma));
            if (kind == Sp20LoadValueKind.Design) factors.Add(('÷', gammaF));
        }
        else if (group is FemCombinationTypes.C or FemCombinationTypes.CL)
        {
            bool reduced = role == Sp20TermRole.PermanentReduced;
            if (kind == Sp20LoadValueKind.Normative) factors.Add(('×', reduced ? ReducedGammaF(loadCase, defaults) : gammaF));
            else if (reduced) { factors.Add(('×', ReducedGammaF(loadCase, defaults))); factors.Add(('÷', gammaF)); }
        }
        else if (kind == Sp20LoadValueKind.Design)
            factors.Add(('÷', gammaF));

        double psi = CombRules.Psi(type, role, accidentalComb ? CombType.Accidental : CombType.Fundamental);
        if (psi != 1) factors.Add(('×', psi));
        if (longTermGroup && type == NormLoadType.ShortTerm) factors.Add(('×', loadCase.LongTermShare ?? 0));

        double value = 1;
        foreach (var (op, f) in factors) value = op == '÷' ? value / f : value * f;
        return new FemAutoCoefficient(value, factors);
    }

    /// <summary>
    /// Пересчитывает «авто»-слагаемые формулы под текущие параметры загружений, тип комбинации и вид величин;
    /// заданные числа и слагаемые без загружения не трогает.
    /// </summary>
    public static FemLoadExpression Recompute(FemLoadExpression expression, IReadOnlyList<FemLoadCase> loadCases,
        string? combinationType, Sp20LoadValueKind kind, Sp20GammaDefaults defaults)
    {
        var byId = loadCases.ToDictionary(lc => lc.Id);
        return new FemLoadExpression
        {
            Mode = expression.Mode,
            LoadCaseIds = expression.LoadCaseIds,
            CombinationType = expression.CombinationType,
            Terms = expression.Terms.Select(t => t.IsAuto && byId.TryGetValue(t.LoadCaseId, out var lc)
                    && FemLoadTermRoles.Parse(t.Role) is { } role
                ? new FemLoadTerm
                {
                    LoadCaseId = t.LoadCaseId, Role = t.Role, Mode = t.Mode,
                    Coefficient = Auto(lc, role, combinationType, kind, defaults).Value,
                }
                : t).ToList(),
        };
    }

    /// <summary>Подстановка «авто»-слагаемого в культуре интерфейса; null — слагаемое задано числом.</summary>
    public static string? Substitution(FemLoadTerm term, FemLoadCase? loadCase, string? combinationType,
        Sp20LoadValueKind kind, Sp20GammaDefaults defaults, CultureInfo culture) =>
        term.IsAuto && loadCase != null && FemLoadTermRoles.Parse(term.Role) is { } role
            ? Auto(loadCase, role, combinationType, kind, defaults).Format(culture)
            : null;
}
