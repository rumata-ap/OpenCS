using CScore.Combinations;
using Xunit;

namespace CScore.Tests;

/// <summary>Огибающая наборов усилий по группе C/CL/N/NL/A и виду величин нагрузок (спека 2026-10-14, п. 5.4).</summary>
public sealed class SP20GroupCombinationTests
{
    static ForceSet Shell(string tag, double mx) =>
        new() { Tag = tag, Kind = "shell", ShellItems = [new ShellLoadItem { Label = "Э1", Mx = mx }] };

    // G 10 (γf 1,1), L 4 (γf 1,2), Q1 5 и Q2 3 (γf 1,4), A 20.
    static readonly ForceSet[] Sets =
        [Shell("G: Вес", 10), Shell("L: Оборудование", 4), Shell("Q: Полезная", 5), Shell("Q: Снег", 3), Shell("A: Удар", 20)];

    static Dictionary<string, double> MaxMx(string group, Sp20LoadValueKind kind, out List<string> warnings)
    {
        var (_, cases, _, w) = SP20Combinations.SP20EnvelopeAndCasesForGroup(Sets, group, kind);
        warnings = w;
        return cases.Single(c => c.Component == "Mx" && c.Sign > 0 && c.Section == 0).Active;
    }

    static void Factors(Dictionary<string, double> active, params (string Name, double K)[] expected)
    {
        Assert.Equal(expected.Select(e => e.Name).Order(), active.Keys.Order());
        foreach (var (name, k) in expected) Assert.Equal(k, active[name], 9);
    }

    [Fact]
    public void C_Normative_SameAsFundamental_Design_WithoutGamma()
    {
        Factors(MaxMx("C", Sp20LoadValueKind.Normative, out _),
            ("Вес", 1.1), ("Оборудование", 1.2), ("Полезная", 1.4), ("Снег", 1.4 * 0.9));
        Factors(MaxMx("C", Sp20LoadValueKind.Design, out _),
            ("Вес", 1), ("Оборудование", 1), ("Полезная", 1), ("Снег", 0.9));
    }

    [Fact]
    public void N_Normative_WithoutGamma_Design_DividedByGamma()
    {
        Factors(MaxMx("N", Sp20LoadValueKind.Normative, out _),
            ("Вес", 1), ("Оборудование", 1), ("Полезная", 1), ("Снег", 0.9));
        Factors(MaxMx("N", Sp20LoadValueKind.Design, out _),
            ("Вес", 1 / 1.1), ("Оборудование", 1 / 1.2), ("Полезная", 1 / 1.4), ("Снег", 0.9 / 1.4));
    }

    [Fact]
    public void LongTermGroups_SkipShortTermAndAccidental_WithWarning()
    {
        Factors(MaxMx("NL", Sp20LoadValueKind.Normative, out var warnings), ("Вес", 1), ("Оборудование", 1));
        Assert.Contains(warnings, w => w.Contains("кратковременные") && w.Contains("(2)"));
        Factors(MaxMx("CL", Sp20LoadValueKind.Normative, out _), ("Вес", 1.1), ("Оборудование", 1.2));
    }

    [Fact]
    public void A_Design_DividedByGamma_FactorsToSetValues()
    {
        // Особое: γf = 1, кратковременные 0,5·0,8 (п. 6.5); у расчётных — ещё ÷γf.
        Factors(MaxMx("A", Sp20LoadValueKind.Normative, out _),
            ("Вес", 1), ("Оборудование", 1), ("Полезная", 0.4), ("Снег", 0.4), ("Удар", 1));
        var (env, cases, _, _) = SP20Combinations.SP20EnvelopeAndCasesForGroup(Sets, "A", Sp20LoadValueKind.Design);
        var top = cases.Single(c => c.Component == "Mx" && c.Sign > 0);
        Factors(top.Active, ("Вес", 1 / 1.1), ("Оборудование", 1 / 1.2), ("Полезная", 0.4 / 1.4), ("Снег", 0.4 / 1.4), ("Удар", 1));
        // Сумма усилий сходится с коэффициентами к величинам наборов.
        double expected = 10 / 1.1 + 4 / 1.2 + (5 + 3) * 0.4 / 1.4 + 20;
        Assert.Equal(expected, top.Forces[Array.IndexOf(SP20Combinations.ComponentKeysFor("shell"), "Mx")], 9);
    }
}
