using Xunit;
using CScore.ParametricSteel;
using CScore.Sp16;
using static CScore.Tests.ParametricSteel.ParametricSteelTestData;
using D = CScore.ParametricSteel.ParametricSteelSectionDefinition;

namespace CScore.Tests.ParametricSteel;

/// <summary>Дескриптор профиля СП 16 из параметрического определения и согласованность с распознаванием.</summary>
public class ParametricSteelProfileTests
{
    [Fact]
    public void RolledIBeamProfile()
    {
        var p = ParametricSteelSectionGenerator.ToSteelProfile(D.RolledIBeam(0.298, 0.149, 0.0055, 0.008, 0.013));
        Assert.Equal(SteelProfileKind.IBeam, p.Kind);
        Assert.Equal(SteelFabrication.Rolled, p.Fabrication);
        Assert.Equal((0.298, 0.149, 0.008, 0.0055, 0.013), (p.H, p.Bf1, p.Tf1, p.Tw, p.R));
        Assert.True(p.IsDoublySymmetricIBeam);
    }

    [Fact]
    public void WeldedMonosymmetricIBeamKeepsBottomFlange()
    {
        var p = ParametricSteelSectionGenerator.ToSteelProfile(D.WeldedIBeam(0.5, 0.3, 0.02, 0.15, 0.012, 0.008));
        Assert.Equal((0.3, 0.02, 0.15, 0.012, 0.0), (p.Bf1, p.Tf1, p.BfBottom, p.TfBottom, p.R));
        Assert.False(p.IsDoublySymmetricIBeam);
    }

    [Fact]
    public void BentRadiiFollowSp16SectionConventions()
    {
        // Гнутый швеллер и уголок — R внутренний (hef = hw − 2R), гнутый короб — наружный (hef = H − 2R).
        Assert.Equal(0.009, ParametricSteelSectionGenerator.ToSteelProfile(D.BentChannel(0.2, 0.08, 0.006, 0.009)).R, 12);
        Assert.Equal(0.009, ParametricSteelSectionGenerator.ToSteelProfile(D.BentAngle(0.1, 0.1, 0.006, 0.009)).R, 12);
        var box = ParametricSteelSectionGenerator.ToSteelProfile(D.BentBox(0.2, 0.1, 0.006, 0.006));
        Assert.Equal(0.012, box.R, 12);
        Assert.Equal((0.006, 0.006), (box.Tf1, box.Tw));
        var ch = ParametricSteelSectionGenerator.ToSteelProfile(D.BentChannel(0.2, 0.08, 0.006, 0.009));
        Assert.Equal(0.006, ch.Tf1, 12);
    }

    [Fact]
    public void SimpleKinds()
    {
        var pipe = ParametricSteelSectionGenerator.ToSteelProfile(D.Pipe(0.159, 0.006));
        Assert.Equal((SteelProfileKind.Pipe, 0.159, 0.006), (pipe.Kind, pipe.H, pipe.Tw));
        var plate = ParametricSteelSectionGenerator.ToSteelProfile(D.Plate(0.02, 0.2));
        Assert.Equal((SteelProfileKind.Rect, 0.2, 0.02), (plate.Kind, plate.H, plate.Bf1));
        var round = ParametricSteelSectionGenerator.ToSteelProfile(D.RoundBar(0.05));
        Assert.Equal((SteelProfileKind.Round, 0.05), (round.Kind, round.H));
        var angle = ParametricSteelSectionGenerator.ToSteelProfile(D.RolledAngle(0.1, 0.063, 0.008, 0.01, 0.0033) with { Flipped = true });
        Assert.Equal((0.1, 0.063, 0.008, 0.008, 0.01, true), (angle.H, angle.Bf1, angle.Tw, angle.Tf1, angle.R, angle.Flipped));
    }

    [Fact]
    public void OrientationFlagsAreCopiedOnlyWhereAllowed()
    {
        var ch = ParametricSteelSectionGenerator.ToSteelProfile(D.WeldedChannel(0.2, 0.08, 0.006, 0.01) with { Rotated90 = true, Flipped = true });
        Assert.True(ch.Rotated90);
        Assert.True(ch.Flipped);
        var ib = ParametricSteelSectionGenerator.ToSteelProfile(D.WeldedIBeam(0.3, 0.15, 0.012, 0, 0, 0.008) with { Flipped = true });
        Assert.False(ib.Flipped);
    }

    public static TheoryData<string, D> Recognizable() => new()
    {
        { "двутавр прокат", D.RolledIBeam(0.298, 0.149, 0.0055, 0.008, 0.013) },
        { "двутавр прокат повёрнут", D.RolledIBeam(0.298, 0.149, 0.0055, 0.008, 0.013) with { Rotated90 = true } },
        { "двутавр сварной несимм.", D.WeldedIBeam(0.5, 0.3, 0.02, 0.2, 0.014, 0.008) },
        { "двутавр сварной повёрнут", D.WeldedIBeam(0.4, 0.2, 0.014, 0, 0, 0.008) with { Rotated90 = true } },
        { "швеллер прокат", D.RolledChannel(0.2, 0.076, 0.0052, 0.009, 0.0095, 0.0055) },
        { "швеллер прокат зеркально", D.RolledChannel(0.2, 0.076, 0.0052, 0.009, 0.0095, 0.0055) with { Flipped = true } },
        { "швеллер прокат повёрнут", D.RolledChannel(0.2, 0.076, 0.0052, 0.009, 0.0095, 0.0055) with { Rotated90 = true } },
        { "швеллер повёрнут и зеркально", D.RolledChannel(0.2, 0.076, 0.0052, 0.009, 0.0095, 0.0055) with { Rotated90 = true, Flipped = true } },
        { "тавр", D.WeldedTee(0.2, 0.15, 0.01, 0.014) },
        { "тавр зеркально", D.WeldedTee(0.2, 0.15, 0.01, 0.014) with { Flipped = true } },
        { "тавр повёрнут", D.WeldedTee(0.2, 0.15, 0.01, 0.014) with { Rotated90 = true } },
        { "тавр повёрнут и зеркально", D.WeldedTee(0.2, 0.15, 0.01, 0.014) with { Rotated90 = true, Flipped = true } },
        { "уголок прокат", D.RolledAngle(0.1, 0.1, 0.008, 0.012, 0.004) },
        { "уголок зеркально", D.RolledAngle(0.1, 0.063, 0.008, 0.01, 0.0033) with { Flipped = true } },
        { "короб сварной", D.WeldedBox(0.4, 0.3, 0.01, 0.016) },
        { "короб гнутый", D.BentBox(0.2, 0.1, 0.006, 0.006) },
        { "труба", D.Pipe(0.159, 0.006) },
        { "лист", D.Plate(0.2, 0.02) },
        { "круг", D.RoundBar(0.05) },
    };

    [Theory]
    [MemberData(nameof(Recognizable))]
    public void RecognizerAgreesWithExplicitProfile(string name, D definition)
    {
        var r = Generate(definition);
        var poly = Polygon(r.Section);
        var explicitProfile = r.Profile!;
        var recognized = SteelProfileRecognizer.Recognize(poly);
        Assert.True(explicitProfile.Kind == recognized.Kind, $"{name}: {recognized.Kind}");
        Assert.True(explicitProfile.Rotated90 == recognized.Rotated90, $"{name}: Rotated90");
        if (explicitProfile.Kind is SteelProfileKind.Channel or SteelProfileKind.Tee)
            Assert.True(explicitProfile.Flipped == recognized.Flipped, $"{name}: Flipped");
        Near(explicitProfile.H, recognized.H, 0.02, name + ": H");
        Near(explicitProfile.Bf1, recognized.Bf1, 0.02, name + ": Bf1");
        Near(explicitProfile.Tw, recognized.Tw, 0.02, name + ": Tw");

        // Геометрия СП 16 с явным и распознанным профилем совпадает.
        var se = Sp16Section.FromContour(poly, explicitProfile, C245);
        var sr = Sp16Section.FromContour(poly, recognized, C245);
        Assert.Equal(sr.Ix, se.Ix, 12);
        Assert.Equal(sr.Iy, se.Iy, 12);
        Assert.True(se.Ix >= se.Iy * (1 - 1e-9) || explicitProfile.Kind is SteelProfileKind.Angle or SteelProfileKind.Rect, $"{name}: канонически x — ось наибольшей жёсткости");
        Near(se.Hef, sr.Hef, 0.05, name + ": hef");
        Near(se.BefTop, sr.BefTop, 0.05, name + ": bef");
    }

    [Fact]
    public void BentChannelWithExplicitProfileGetsTable7Curve()
    {
        var r = Generate(D.BentChannel(0.2, 0.08, 0.006, 0.009));
        var poly = Polygon(r.Section);
        Assert.Equal(SteelProfileKind.Generic, SteelProfileRecognizer.Recognize(poly).Kind);
        var s = Sp16Section.FromContour(poly, r.Profile, C245);
        Assert.Equal(SteelProfileKind.Channel, s.Kind);
        Assert.Equal(SectionCurve.c, s.CurveFor(true));
        Assert.True(s.Hef > 0 && s.Hef < 0.2 - 2 * 0.006);
    }

    static void Near(double expected, double actual, double rel, string what)
    {
        double tol = rel * Math.Max(Math.Abs(expected), 1e-9);
        Assert.True(Math.Abs(expected - actual) <= tol, $"{what}: ожидалось {expected}, получено {actual}");
    }
}
