using CScore;
using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63Normal;

/// <summary>Проверки бетонных (без рабочей арматуры) элементов по разделу 7 СП 63.</summary>
public sealed class Sp63ConcreteNormalCheckerTests
{
    static Sp63NormalOptions ConcreteOptions(double length, double? l0,
        Sp63NormalStabilityMode mode = Sp63NormalStabilityMode.Member, double psi = 0.0,
        Sp63NormalShapeKind shape = Sp63NormalShapeKind.Rectangular) => new(
        shape,
        Sp63NormalAxis.Mx,
        new Sp63MemberContext(length, Sp63StructuralScheme.StaticallyIndeterminate, l0,
            mode, psi),
        Sp63NormalElementType.Concrete);

    /// <summary>Панель примера 1 Пособия: b = 1 м, h = 0,15 м, бетон B15 (Eb = 24 000 МПа).</summary>
    static CrossSection HandbookPanel(double rbC, double rbCl)
    {
        var concrete = Sp63NormalFixtures.Concrete(rb: rbC);
        concrete.E = 24_000_000.0;
        concrete.C!.E = 24_000_000.0;
        concrete.CL = new MaterialChars(CalcType.CL)
        {
            Type = MatType.Concrete,
            Fc = -rbCl,
            Ft = 675.0,
            E = 24_000_000.0,
            Ec2 = -0.0035
        };
        var section = new CrossSection { Tag = "panel" };
        section.Areas.Add(Sp63NormalFixtures.ConcreteRegion(concrete,
            [(-0.5, -0.075), (0.5, -0.075), (0.5, 0.075), (-0.5, 0.075)]));
        return section;
    }

    [Fact]
    public void HandbookExample1_FullLoad_MatchesManual()
    {
        // Пособие к СП 63, пример 1: N = 700 кН, H = l0 = 2,7 м, e0 = ea = 10 мм,
        // φl = 1 + 0,93 = 1,93 (как в книге), η = 1,797, Rb·γb3 = 7,65 МПа, Rb·Ab = 872,6 кН > 700.
        var section = HandbookPanel(rbC: 8_500.0, rbCl: 7_650.0);
        var result = Sp63NormalChecker.Check(section, new LoadItem { N = -700.0 },
            CalcType.C, ConcreteOptions(2.7, 2.7, psi: 0.93));

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Equal("concrete_compression", result.Branch);
        Assert.True(result.StrengthPassed);
        Assert.Equal(0.010, result.Variables["e0"], 9);
        Assert.Equal(1578.3, result.Variables["etaNcr"], 0);
        Assert.Equal(1.797, result.Variables["eta"], 3);
        Assert.Equal(7_650.0, result.Variables["Rb"], 6);
        var detail = Assert.Single(result.StrengthDetails);
        Assert.Equal("(7.1)", detail.Formula);
        Assert.Equal(872.6, detail.Allowable, 0);
        Assert.Contains(result.InformationalMessages, m => m.Code == "concrete_gamma_b3");
    }

    [Fact]
    public void HandbookExample1_LongTermLoad_MatchesManual()
    {
        // Та же панель на Nl = 650 кН: φl = 2, η = 1,745, Rb = 6,89 МПа (γb1·γb3),
        // Rb·Ab = 793 кН > 650.
        var section = HandbookPanel(rbC: 8_500.0, rbCl: 7_650.0);
        var result = Sp63NormalChecker.Check(section, new LoadItem { N = -650.0 },
            CalcType.CL, ConcreteOptions(2.7, 2.7, psi: 1.0));

        Assert.True(result.StrengthPassed);
        // Пособие округляет Ncr до 1523,4 кН; точное значение даёт η = 1,7445.
        Assert.InRange(result.Variables["eta"], 1.743, 1.746);
        Assert.Equal(793.0, result.StrengthDetails[0].Allowable, 0);
    }

    [Fact]
    public void Bending_UsesRbtTimesElasticModulus()
    {
        // W = 0,3·0,5²/6 = 0,0125 м³; Mult = 1000·0,0125 = 12,5 кН·м.
        var section = Sp63NormalFixtures.Rectangle(0.3, 0.5);
        var passed = Sp63NormalChecker.Check(section, new LoadItem { Mx = -5.0 },
            CalcType.C, ConcreteOptions(3.0, 3.0));
        var failed = Sp63NormalChecker.Check(section, new LoadItem { Mx = 20.0 },
            CalcType.C, ConcreteOptions(3.0, 3.0));

        Assert.Equal("concrete_bending", passed.Branch);
        Assert.Equal("(7.8)", passed.StrengthDetails[0].Formula);
        Assert.Equal(12.5, passed.StrengthDetails[0].Allowable, 9);
        Assert.True(passed.StrengthPassed);
        Assert.False(failed.StrengthPassed);
    }

    [Fact]
    public void ForceOutsideSection_UsesTensileZoneFormula()
    {
        // e0 = 0,4 м > h/2: (7.5) Nult = 1000·0,3·0,5/(6·0,4/0,5 − 1) = 150/3,8.
        var section = Sp63NormalFixtures.Rectangle(0.3, 0.5);
        var result = Sp63NormalChecker.Check(section, new LoadItem { N = -100.0, Mx = 40.0 },
            CalcType.C, ConcreteOptions(3.0, null, Sp63NormalStabilityMode.SectionOnlyExplicit));

        Assert.Equal("concrete_compression_outside", result.Branch);
        var detail = Assert.Single(result.StrengthDetails);
        Assert.Equal("(7.5)", detail.Formula);
        Assert.Equal(150.0 / 3.8, detail.Allowable, 9);
        Assert.False(result.StrengthPassed);
    }

    [Fact]
    public void SmallEccentricity_AddsReferenceCheck73()
    {
        // e0 = ea = h/30, l0/h = 6 → φ = 0,92 (кратковременно); N ≤ φ·Rb·A справочно.
        var section = Sp63NormalFixtures.Rectangle(0.3, 0.5);
        var result = Sp63NormalChecker.Check(section, new LoadItem { N = -1000.0 },
            CalcType.C, ConcreteOptions(3.0, 3.0));

        var alt = Assert.Single(result.AlternativeChecks);
        Assert.Equal("(7.3)", alt.Formula);
        Assert.Equal(0.92, alt.Variables["phi"], 9);
        Assert.Equal(0.92 * 18_000.0 * 0.15, alt.Allowable, 6);
    }

    [Theory]
    [InlineData(4.0, true, 0.92)]
    [InlineData(12.5, true, 0.85)]
    [InlineData(20.0, true, 0.6)]
    [InlineData(10.0, false, 0.9)]
    [InlineData(15.0, false, 0.875)]
    [InlineData(20.0, false, 0.85)]
    public void Phi_FollowsTable71AndShortTermLine(double l0OverH, bool longTerm, double expected) =>
        Assert.Equal(expected, Sp63ConcreteNormalChecker.Phi(l0OverH, longTerm), 9);

    [Fact]
    public void Tension_IsNotApplicable()
    {
        var result = Sp63NormalChecker.Check(Sp63NormalFixtures.Rectangle(0.3, 0.5),
            new LoadItem { N = 10.0 }, CalcType.C, ConcreteOptions(3.0, 3.0));

        Assert.Equal(Sp63NormalStatus.NotApplicable, result.Status);
        Assert.Equal("concrete_tension_not_supported", result.ApplicabilityMessages[0].Code);
    }

    [Fact]
    public void CircularShape_IsNotApplicable()
    {
        var result = Sp63NormalChecker.Check(Sp63NormalFixtures.CircleSection(0.3),
            new LoadItem { N = -10.0 }, CalcType.C,
            ConcreteOptions(3.0, 3.0, shape: Sp63NormalShapeKind.Circular));

        Assert.Equal("concrete_shape_not_supported", result.ApplicabilityMessages[0].Code);
    }

    [Fact]
    public void TeeShapeOnRectangle_IsNotATee()
    {
        var result = Sp63NormalChecker.Check(Sp63NormalFixtures.Rectangle(0.3, 0.5),
            new LoadItem { N = -10.0 }, CalcType.C,
            ConcreteOptions(3.0, 3.0, shape: Sp63NormalShapeKind.Tee));

        Assert.Equal("not_a_tee_shape", result.ApplicabilityMessages[0].Code);
    }

    // Тавр для ручного счёта: стенка 0,2×0,4, полка 0,6×0,1 сверху, h = 0,5 м.
    // От нижней грани: A = 0,08 + 0,06 = 0,14; S = 0,08·0,2 + 0,06·0,45 = 0,043;
    // yb = 0,043/0,14; I = Σ(b·t³/12 + A·(c − yb)²).
    const double TeeA = 0.14;
    static readonly double TeeYb = 0.043 / 0.14;
    static readonly double TeeI =
        0.2 * Math.Pow(0.4, 3) / 12.0 + 0.08 * Math.Pow(0.2 - TeeYb, 2) +
        0.6 * Math.Pow(0.1, 3) / 12.0 + 0.06 * Math.Pow(0.45 - TeeYb, 2);

    static CrossSection HandTee(bool rotateForMy = false) =>
        Sp63NormalFixtures.PlainTee(0.2, 0.5, 0.6, 0.1, flangeOnTop: true, rotateForMy);

    static Sp63NormalOptions TeeOptions(bool cracksNotAllowed = false,
        Sp63NormalAxis axis = Sp63NormalAxis.Mx) => new(
        Sp63NormalShapeKind.Tee,
        axis,
        new Sp63MemberContext(3.0, Sp63StructuralScheme.StaticallyIndeterminate, null,
            Sp63NormalStabilityMode.SectionOnlyExplicit, 0.0),
        Sp63NormalElementType.Concrete,
        cracksNotAllowed);

    [Theory]
    [InlineData(5.0, false)]   // Mx > 0 растягивает верх (полку): yt = h − yb.
    [InlineData(-5.0, true)]   // Mx < 0 растягивает низ (стенку): yt = yb.
    public void TeeBending_UsesElasticModulusToTensionFiber(double mx, bool tensionAtBottom)
    {
        double yt = tensionAtBottom ? TeeYb : 0.5 - TeeYb;
        var result = Sp63NormalChecker.Check(HandTee(), new LoadItem { Mx = mx },
            CalcType.C, TeeOptions());

        Assert.Equal("concrete_bending", result.Branch);
        var detail = Assert.Single(result.StrengthDetails);
        Assert.Equal(1_000.0 * TeeI / yt, detail.Allowable, 9);
        Assert.Equal(yt, result.Variables["yt"], 12);
        // Без поперечной силы условия п. 7.1.4 не выводятся.
        Assert.DoesNotContain(result.InformationalMessages,
            m => m.Code == "concrete_elastic_stresses");
    }

    [Fact]
    public void TeeBending_RotatedForMy_MatchesMx()
    {
        var mx = Sp63NormalChecker.Check(HandTee(), new LoadItem { Mx = 5.0 },
            CalcType.C, TeeOptions());
        var my = Sp63NormalChecker.Check(HandTee(rotateForMy: true), new LoadItem { My = 5.0 },
            CalcType.C, TeeOptions(axis: Sp63NormalAxis.My));

        Assert.Equal(mx.StrengthDetails[0].Allowable, my.StrengthDetails[0].Allowable, 9);
    }

    [Fact]
    public void TeeCompression_FlangeCompressed_AbCentroidAtForce()
    {
        // Mx < 0 сжимает верх (полку). e0 = 50/1000 = 0,05 м; yc от сжатой грани = h − yb;
        // d = yc − e0. Зона заходит в стенку: A(x) = 0,06 + 0,2·(x − 0,1),
        // S(x) = 0,003 + 0,1·(x² − 0,01); S = d·A сводится к x² − 2d·x + 0,02 − 0,4d = 0
        // → x = d + √(d² − 0,02 + 0,4d); Ab = A(x).
        double d = 0.5 - TeeYb - 0.05;
        double x = d + Math.Sqrt(d * d - 0.02 + 0.4 * d);
        double ab = 0.06 + 0.2 * (x - 0.1);

        var result = Sp63NormalChecker.Check(HandTee(), new LoadItem { N = -1000.0, Mx = -50.0 },
            CalcType.C, TeeOptions());

        Assert.Equal("concrete_compression", result.Branch);
        var detail = Assert.Single(result.StrengthDetails);
        Assert.Equal("(7.1)", detail.Formula);
        Assert.Equal(x, result.Variables["xZone"], 9);
        Assert.Equal(18_000.0 * ab, detail.Allowable, 6);
        Assert.True(result.StrengthPassed);
        Assert.Empty(result.AlternativeChecks);
    }

    [Fact]
    public void TeeCompression_ZoneWithinFlange_IsFlangeRectangle()
    {
        // Mx < 0, e0 = 0,17 м: d = yc − e0 < 0,05 — зона внутри полки, Ab = 0,6·2d.
        double d = 0.5 - TeeYb - 0.17;
        var result = Sp63NormalChecker.Check(HandTee(), new LoadItem { N = -100.0, Mx = -17.0 },
            CalcType.C, TeeOptions());

        Assert.Equal(18_000.0 * 0.6 * 2.0 * d, result.StrengthDetails[0].Allowable, 6);
    }

    [Fact]
    public void TeeCompression_ForceOutsideSection_UsesFormula74()
    {
        // Mx > 0 сжимает низ (стенку): yc = yb, e0 = 0,5 м > yc; растянут верх, yt = h − yb.
        double yt = 0.5 - TeeYb;
        double expected = 1_000.0 * TeeA / (TeeA / TeeI * 0.5 * yt - 1.0);

        var result = Sp63NormalChecker.Check(HandTee(), new LoadItem { N = -200.0, Mx = 100.0 },
            CalcType.C, TeeOptions());

        Assert.Equal("concrete_compression_outside", result.Branch);
        var detail = Assert.Single(result.StrengthDetails);
        Assert.Equal("(7.4)", detail.Formula);
        Assert.Equal(expected, detail.Allowable, 9);
    }

    [Fact]
    public void TeeCompression_CracksNotAllowed_AddsFormula74ToVerdict()
    {
        // Mx < 0, e0 = 0,12 м — сила в пределах сечения, (7.1) выполняется; растянут низ,
        // yt = yb: Nult,crc = Rbt·A/(A/I·e0·yt − 1) ≈ 240 кН < 1000 кН.
        double expected = 1_000.0 * TeeA / (TeeA / TeeI * 0.12 * TeeYb - 1.0);
        var load = new LoadItem { N = -1000.0, Mx = -120.0 };

        var plain = Sp63NormalChecker.Check(HandTee(), load, CalcType.C, TeeOptions());
        var crackFree = Sp63NormalChecker.Check(HandTee(), load, CalcType.C,
            TeeOptions(cracksNotAllowed: true));

        Assert.True(plain.StrengthPassed);
        Assert.Single(plain.StrengthDetails);
        Assert.Equal(2, crackFree.StrengthDetails.Count);
        Assert.Equal("(7.4)", crackFree.StrengthDetails[1].Formula);
        Assert.Equal(expected, crackFree.StrengthDetails[1].Allowable, 9);
        Assert.False(crackFree.StrengthPassed);
    }

    [Fact]
    public void TeeCompression_ZeroMoment_ChecksBothFacesAndTakesWorst()
    {
        // e0 = ea = max(3/600; 0,5/30; 0,01) = 0,5/30. Худшая грань — сжатая стенка
        // (меньшая сжатая зона), результат не хуже ни одного из явных направлений.
        var zero = Sp63NormalChecker.Check(HandTee(), new LoadItem { N = -1000.0 },
            CalcType.C, TeeOptions());
        double ea = 0.5 / 30.0;
        var towardWeb = Sp63NormalChecker.Check(HandTee(),
            new LoadItem { N = -1000.0, Mx = 1000.0 * ea }, CalcType.C, TeeOptions());
        var towardFlange = Sp63NormalChecker.Check(HandTee(),
            new LoadItem { N = -1000.0, Mx = -1000.0 * ea }, CalcType.C, TeeOptions());

        double worst = Math.Min(towardWeb.StrengthDetails[0].Allowable,
            towardFlange.StrengthDetails[0].Allowable);
        Assert.Equal(worst, zero.StrengthDetails[0].Allowable, 6);
        Assert.Contains(zero.InformationalMessages, m => m.Code == "concrete_tee_both_faces");
    }

    [Fact]
    public void RectangleCompression_CracksNotAllowed_UsesFormula75()
    {
        // 0,3×0,5, e0 = 0,1 м: (7.1) Ab = 0,15·(1 − 0,4) → 1620 кН; (7.5) Rbt·b·h/(6e0/h − 1) = 750 кН.
        var load = new LoadItem { N = -1000.0, Mx = 100.0 };
        var options = ConcreteOptions(3.0, null, Sp63NormalStabilityMode.SectionOnlyExplicit)
            with { CracksNotAllowed = true };

        var result = Sp63NormalChecker.Check(Sp63NormalFixtures.Rectangle(0.3, 0.5), load,
            CalcType.C, options);

        Assert.Equal(2, result.StrengthDetails.Count);
        Assert.Equal(1_620.0, result.StrengthDetails[0].Allowable, 6);
        Assert.Equal("(7.5)", result.StrengthDetails[1].Formula);
        Assert.Equal(750.0, result.StrengthDetails[1].Allowable, 6);
        Assert.False(result.StrengthPassed);
    }

    [Fact]
    public void RectangleCompression_CracksNotAllowed_ForceInKernel_NoTensionNote()
    {
        // e0 = 0,04 м < h/6: растянутой зоны нет, (7.5) не требуется.
        var options = ConcreteOptions(3.0, null, Sp63NormalStabilityMode.SectionOnlyExplicit)
            with { CracksNotAllowed = true };
        var result = Sp63NormalChecker.Check(Sp63NormalFixtures.Rectangle(0.3, 0.5),
            new LoadItem { N = -1000.0, Mx = 40.0 }, CalcType.C, options);

        Assert.Single(result.StrengthDetails);
        Assert.Contains(result.InformationalMessages, m => m.Code == "concrete_no_tension_zone");
    }

    [Fact]
    public void BandProfile_RectangleZone_IsTwiceDepth()
    {
        var profile = Sp63ConcreteBandProfile.Rectangle(0.3, 0.5);
        double ab = profile.CompressedZoneArea(0.1, out double x);

        Assert.Equal(0.2, x, 12);
        Assert.Equal(0.06, ab, 12);
        Assert.Equal(0.3 * Math.Pow(0.5, 3) / 12.0, profile.Inertia, 12);
    }

    [Theory]
    [InlineData(0.02)]   // зона в сжатой полке
    [InlineData(0.15)]   // зона заходит в стенку
    [InlineData(0.31)]   // зона захватывает часть растянутой полки (yc ≈ 0,323)
    public void BandProfile_IBeamZone_MatchesSlicedIntegration(double depth)
    {
        // Двутавр: полки 0,5×0,12 и 0,4×0,1, стенка 0,15×0,48; h = 0,7 м.
        var geometry = new Sp63TeeGeometry(0.15, 0.7, 0.0, 0.7, 0.5, 0.12, 0.4, 0.1);
        var profile = Sp63ConcreteBandProfile.Tee(geometry, compressedAtMin: false);
        double ab = profile.CompressedZoneArea(depth, out double x);

        // Независимо: центр тяжести участка [0; x] тонкими слоями, x — бисекцией.
        double Width(double s) => s < 0.12 ? 0.5 : s < 0.6 ? 0.15 : 0.4;
        (double Area, double Moment) Integrate(double top)
        {
            const int slices = 200_000;
            double step = top / slices, area = 0, moment = 0;
            for (int k = 0; k < slices; k++)
            {
                double s = (k + 0.5) * step;
                area += Width(s) * step;
                moment += Width(s) * step * s;
            }
            return (area, moment);
        }
        double low = 1e-6, high = 0.7;
        for (int iteration = 0; iteration < 60; iteration++)
        {
            double mid = (low + high) / 2;
            var (a, m) = Integrate(mid);
            if (m / a < depth) low = mid; else high = mid;
        }
        double expectedX = (low + high) / 2;

        Assert.Equal(expectedX, x, 4);
        Assert.Equal(Integrate(expectedX).Area, ab, 4);
    }

    // --- Поперечная сила, п. 7.1.4 (Пособие (3.12)/(3.12а), (3.14)). Rb·γb3 = 18 МПа, Rbt = 1 МПа.

    /// <summary>(3.12): σmt/Rbt + σmc/Rb, σmt,mc = ∓σx/2 + √((σx/2)² + τ²), σx — сжатие.</summary>
    static double PrincipalRatio(double sigmaX, double tau)
    {
        double root = Math.Sqrt(sigmaX * sigmaX / 4.0 + tau * tau);
        return (root - sigmaX / 2.0) / 1_000.0 + (root + sigmaX / 2.0) / 18_000.0;
    }

    [Fact]
    public void RectangleBending_WithShear_ChecksPrincipalStressesAtCentroid()
    {
        // 0,3×0,5, Vy = 60 кН: τ = 1,5·Q/(b·h) = 600 кПа, σx = 0 на уровне ц.т.
        // → 600/1000 + 600/18000. Vx при изгибе Mx не учитывается.
        var result = Sp63NormalChecker.Check(Sp63NormalFixtures.Rectangle(0.3, 0.5),
            new LoadItem { Mx = -5.0, Vy = 60.0, Vx = 1_000.0 }, CalcType.C,
            ConcreteOptions(3.0, 3.0));

        Assert.Equal(2, result.StrengthDetails.Count);
        var shear = result.StrengthDetails[1];
        Assert.Equal("(3.12)", shear.Formula);
        Assert.Equal("7.1.4", shear.NormReference);
        Assert.Equal(600.0, result.Variables["tau"], 9);
        Assert.Equal(0.0, result.Variables["sigmaX"], 9);
        Assert.Equal(0.6 + 600.0 / 18_000.0, shear.Applied, 12);
        Assert.Equal(1.0, shear.Allowable);
        Assert.True(result.StrengthPassed);
        Assert.Contains(result.InformationalMessages, m => m.Code == "concrete_elastic_stresses");
    }

    [Fact]
    public void RectangleBending_LargeShear_FailsVerdict()
    {
        // τ = 1,5·100/0,15 = 1000 кПа: 1 + 1000/18000 > 1 при выполненном (7.8).
        var result = Sp63NormalChecker.Check(Sp63NormalFixtures.Rectangle(0.3, 0.5),
            new LoadItem { Mx = -5.0, Vy = 100.0 }, CalcType.C, ConcreteOptions(3.0, 3.0));

        Assert.True(result.StrengthDetails[0].Passed);
        Assert.False(result.StrengthDetails[1].Passed);
        Assert.False(result.StrengthPassed);
    }

    [Fact]
    public void RectangleCompression_WithShear_UsesAxialStressAtCentroid()
    {
        // σx = N/A = 1000/0,15, τ = 600 кПа; момент на уровне ц.т. прямоугольника не влияет.
        var options = ConcreteOptions(3.0, null, Sp63NormalStabilityMode.SectionOnlyExplicit);
        var result = Sp63NormalChecker.Check(Sp63NormalFixtures.Rectangle(0.3, 0.5),
            new LoadItem { N = -1000.0, Mx = 40.0, Vy = -60.0 }, CalcType.C, options);

        Assert.Equal("concrete_compression", result.Branch);
        var shear = Assert.Single(result.StrengthDetails, d => d.Formula == "(3.12)");
        Assert.Equal(1000.0 / 0.15, result.Variables["sigmaX"], 9);
        Assert.Equal(PrincipalRatio(1000.0 / 0.15, 600.0), shear.Applied, 12);
    }

    [Fact]
    public void RectangleCompressionOutside_WithShear_AddsPrincipalCheck()
    {
        var options = ConcreteOptions(3.0, null, Sp63NormalStabilityMode.SectionOnlyExplicit);
        var result = Sp63NormalChecker.Check(Sp63NormalFixtures.Rectangle(0.3, 0.5),
            new LoadItem { N = -100.0, Mx = 40.0, Vy = 30.0 }, CalcType.C, options);

        Assert.Equal("concrete_compression_outside", result.Branch);
        Assert.Equal(new[] { "(7.5)", "(3.12)" }, result.StrengthDetails.Select(d => d.Formula));
        Assert.Equal(PrincipalRatio(100.0 / 0.15, 300.0), result.StrengthDetails[1].Applied, 12);
    }

    [Fact]
    public void TeeBending_FlangeCompressed_ChecksJunctionAndCentroid()
    {
        // Mx < 0 сжимает верх (полку). От сжатой грани yc = h − yb; уровень (3.12) — низ
        // полки (0,1 м), S = 0,06·(yc − 0,05), b = 0,2; σx = M·(yc − 0,1)/I.
        // (3.14): S(yc) = S + 0,2·(yc − 0,1)²/2.
        const double q = 80.0, m = 5.0;
        double yc = 0.5 - TeeYb;
        double sJunction = 0.06 * (yc - 0.05);
        double tau = q * sJunction / (TeeI * 0.2);
        double sigmaX = m * (yc - 0.1) / TeeI;
        double sCentroid = sJunction + 0.2 * Math.Pow(yc - 0.1, 2) / 2.0;

        var result = Sp63NormalChecker.Check(HandTee(), new LoadItem { Mx = -m, Vy = q },
            CalcType.C, TeeOptions());

        Assert.Equal(new[] { "(7.8)", "(3.12)", "(3.14)" }, result.StrengthDetails.Select(d => d.Formula));
        Assert.Equal(0.1, result.Variables["yTau"], 12);
        Assert.Equal(tau, result.Variables["tau"], 9);
        Assert.Equal(PrincipalRatio(sigmaX, tau), result.StrengthDetails[1].Applied, 12);
        Assert.Equal(q * sCentroid / (TeeI * 0.2), result.StrengthDetails[2].Applied, 9);
        Assert.Equal(1_000.0, result.StrengthDetails[2].Allowable, 12);
        Assert.DoesNotContain(result.InformationalMessages,
            msg => msg.Code == "concrete_principal_stress_at_centroid");
    }

    [Fact]
    public void TeeBending_WebCompressed_ChecksPrincipalStressesAtCentroid()
    {
        // Mx > 0 сжимает низ (стенку), полки у сжатой грани нет → (3.12) на уровне ц.т.:
        // σx = 0, S = 0,2·yb²/2, τ совпадает с (3.14).
        const double q = 80.0;
        double tau = q * (0.2 * TeeYb * TeeYb / 2.0) / (TeeI * 0.2);

        var result = Sp63NormalChecker.Check(HandTee(), new LoadItem { Mx = 5.0, Vy = q },
            CalcType.C, TeeOptions());

        Assert.Equal(PrincipalRatio(0.0, tau), result.StrengthDetails[1].Applied, 12);
        Assert.Equal(tau, result.StrengthDetails[2].Applied, 9);
        Assert.Contains(result.InformationalMessages,
            msg => msg.Code == "concrete_principal_stress_at_centroid");
    }

    [Fact]
    public void TeeBending_RotatedForMy_UsesVx()
    {
        var mx = Sp63NormalChecker.Check(HandTee(), new LoadItem { Mx = -5.0, Vy = 80.0 },
            CalcType.C, TeeOptions());
        var my = Sp63NormalChecker.Check(HandTee(rotateForMy: true),
            new LoadItem { My = -5.0, Vx = 80.0, Vy = 500.0 }, CalcType.C,
            TeeOptions(axis: Sp63NormalAxis.My));

        Assert.Equal(mx.StrengthDetails.Count, my.StrengthDetails.Count);
        for (int i = 0; i < mx.StrengthDetails.Count; i++)
            Assert.Equal(mx.StrengthDetails[i].Applied, my.StrengthDetails[i].Applied, 9);
    }

    [Fact]
    public void TeeCompression_WithShear_UsesAmplifiedMomentAtJunction()
    {
        // Mx < 0 сжимает полку, e0 = 0,05 м (сечение, η = 1): σx = N/A + N·e0·(yc − 0,1)/I.
        const double n = 1000.0, q = 150.0;
        double yc = 0.5 - TeeYb;
        double sigmaX = n / TeeA + n * 0.05 * (yc - 0.1) / TeeI;
        double tau = q * 0.06 * (yc - 0.05) / (TeeI * 0.2);

        var result = Sp63NormalChecker.Check(HandTee(),
            new LoadItem { N = -n, Mx = -50.0, Vy = q }, CalcType.C, TeeOptions());

        Assert.Equal(new[] { "(7.1)", "(3.12)" }, result.StrengthDetails.Select(d => d.Formula));
        Assert.Equal(sigmaX, result.Variables["sigmaX"], 9);
        Assert.Equal(PrincipalRatio(sigmaX, tau), result.StrengthDetails[1].Applied, 12);
    }

    [Fact]
    public void BandProfile_StaticMoment_IsZeroOverFullHeightAndMaxAtCentroid()
    {
        var geometry = new Sp63TeeGeometry(0.15, 0.7, 0.0, 0.7, 0.5, 0.12, 0.4, 0.1);
        var profile = Sp63ConcreteBandProfile.Tee(geometry, compressedAtMin: false);

        Assert.Equal(0.0, profile.StaticMoment(profile.Height), 12);
        double atCentroid = profile.StaticMoment(profile.Centroid);
        Assert.True(atCentroid > profile.StaticMoment(profile.Centroid - 0.01));
        Assert.True(atCentroid > profile.StaticMoment(profile.Centroid + 0.01));
        Assert.Equal(0.15, profile.WidthAt(profile.PrincipalStressLevel), 12);
    }

    [Fact]
    public void TaskParams_CracksNotAllowed_RoundTripsAndDefaultsToFalse()
    {
        var legacy = Sp63NormalTaskParams.Parse("{\"shapeKind\":\"tee\",\"elementType\":\"concrete\"}");
        Assert.True(legacy.TryToOptions(out var legacyOptions, out _));
        Assert.False(legacyOptions.CracksNotAllowed);

        var parameters = new Sp63NormalTaskParams { ElementType = "concrete", CracksNotAllowed = true };
        var restored = Sp63NormalTaskParams.Parse(parameters.ToJson());
        Assert.True(restored.TryToOptions(out var options, out _));
        Assert.True(options.CracksNotAllowed);
    }

    [Fact]
    public void RebarInSection_IsIgnoredWithNote()
    {
        var plain = Sp63NormalFixtures.Rectangle(0.3, 0.5);
        var withRebar = Sp63NormalFixtures.Rectangle(0.3, 0.5);
        Sp63NormalFixtures.AddBar(withRebar, 0.0, -0.2, 0.000201, Sp63NormalFixtures.Rebar(2));
        var load = new LoadItem { N = -1000.0, Mx = 30.0 };
        var options = ConcreteOptions(3.0, 3.0, Sp63NormalStabilityMode.SectionOnlyExplicit);

        var a = Sp63NormalChecker.Check(plain, load, CalcType.C, options);
        var b = Sp63NormalChecker.Check(withRebar, load, CalcType.C, options);

        Assert.Equal(a.StrengthDetails[0].Allowable, b.StrengthDetails[0].Allowable, 9);
        Assert.Contains(b.InformationalMessages, m => m.Code == "concrete_rebar_ignored");
        Assert.DoesNotContain(a.InformationalMessages, m => m.Code == "concrete_rebar_ignored");
    }

    [Fact]
    public void ReinforcedMode_WithoutRebar_StaysNotApplicable()
    {
        var result = Sp63NormalChecker.Check(Sp63NormalFixtures.Rectangle(0.3, 0.5),
            new LoadItem { N = -1000.0 }, CalcType.C, Sp63NormalFixtures.MemberOptions());

        Assert.Equal(Sp63NormalStatus.NotApplicable, result.Status);
    }

    [Theory]
    [InlineData(null, Sp63NormalElementType.Reinforced)]
    [InlineData("", Sp63NormalElementType.Reinforced)]
    [InlineData("reinforced", Sp63NormalElementType.Reinforced)]
    [InlineData("concrete", Sp63NormalElementType.Concrete)]
    public void TaskParams_ParseElementType(string? value, Sp63NormalElementType expected)
    {
        var parameters = new Sp63NormalTaskParams { ElementType = value! };
        Assert.True(parameters.TryToOptions(out var options, out _));
        Assert.Equal(expected, options.ElementType);
    }

    [Fact]
    public void TaskParams_UnknownElementType_IsInvalid()
    {
        var parameters = new Sp63NormalTaskParams { ElementType = "plain" };
        Assert.False(parameters.TryToOptions(out _, out var code));
        Assert.Equal("invalid_element_type", code);
    }

    [Fact]
    public void TaskParams_LegacyJsonWithoutField_IsReinforced()
    {
        var parameters = Sp63NormalTaskParams.Parse("{\"shapeKind\":\"rectangular\",\"axis\":\"Mx\"}");
        Assert.True(parameters.TryToOptions(out var options, out _));
        Assert.Equal(Sp63NormalElementType.Reinforced, options.ElementType);
    }
}
