using CScore;
using CScore.Fire;

namespace CSfea.Tests;

/// <summary>Разрешение группы класса арматуры по таблице 5.6 СП 468.</summary>
public static class FireRebarClassResolverTests
{
    public static void RunAll()
    {
        TestHarness.Section("FireRebarClassResolver: группа класса арматуры");
        Explicit_WinsOverEverything();
        Tag_RecognizesWireAndRope();
        Tag_CyrillicAndLatinAreEquivalent();
        Class_MapsNumericGrades();
        Unknown_FallsBackWithFlag();
    }

    static Material Rebar(string tag, double numericClass) => new()
    {
        Type = MatType.ReSteelF,
        Tag = tag,
        MaterialChars = [Chars(numericClass), Chars(numericClass), Chars(numericClass), Chars(numericClass)]
    };

    static MaterialChars Chars(double numericClass) => new() { Class = numericClass };

    static void Explicit_WinsOverEverything()
    {
        var m = Rebar("A400 (6-40 мм)", 400);
        m.FireRebarClass = "a500c_25g2s";

        var r = FireRebarClassResolver.Resolve(m);

        TestHarness.Check("FireRebarClass_ExplicitWins",
            r.Group == FireRebarClass.A500C25G2S && r.Source == "explicit" && !r.IsFallback,
            $"group={r.Group}, source={r.Source}");
    }

    static void Tag_RecognizesWireAndRope()
    {
        var bp = FireRebarClassResolver.Resolve(Rebar("Вр1400", 0));
        var rope = FireRebarClassResolver.Resolve(Rebar("К1500", 0));

        TestHarness.Check("FireRebarClass_TagWire",
            bp.Group == FireRebarClass.WireRope && bp.Source == "tag");
        TestHarness.Check("FireRebarClass_TagRope",
            rope.Group == FireRebarClass.WireRope && rope.Source == "tag");
    }

    static void Tag_CyrillicAndLatinAreEquivalent()
    {
        var cyrillic = FireRebarClassResolver.Resolve(Rebar("В500", 0));
        var latin = FireRebarClassResolver.Resolve(Rebar("B500", 0));

        TestHarness.Check("FireRebarClass_HomoglyphsEqual",
            cyrillic.Group == FireRebarClass.WireRope && latin.Group == FireRebarClass.WireRope,
            $"cyr={cyrillic.Group}, lat={latin.Group}");
    }

    static void Class_MapsNumericGrades()
    {
        var low = FireRebarClassResolver.Resolve(Rebar("Своя марка", 400));
        var high = FireRebarClassResolver.Resolve(Rebar("Своя марка", 800));

        TestHarness.Check("FireRebarClass_Class400",
            low.Group == FireRebarClass.A240A500 && low.Source == "class");
        TestHarness.Check("FireRebarClass_Class800",
            high.Group == FireRebarClass.A600A1000 && high.Source == "class");
    }

    static void Unknown_FallsBackWithFlag()
    {
        var r = FireRebarClassResolver.Resolve(Rebar("Нечто", 0));

        TestHarness.Check("FireRebarClass_Fallback",
            r.Group == FireRebarClass.A240A500 && r.IsFallback && r.RawValue == "Нечто",
            $"group={r.Group}, fallback={r.IsFallback}, raw={r.RawValue}");
    }
}
