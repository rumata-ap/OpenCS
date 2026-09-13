using CScore;
using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63Normal;

/// <summary>
/// Предметные проверки таврового сечения по примерам пособий к СП 63.
/// В обоих примерах задана только растянутая арматура: отсутствие сжатой
/// арматуры должно быть штатным случаем, а не требовать фиктивного стержня.
/// </summary>
public sealed class Sp63TeeHandbookVerificationTests
{
    const double SpanLength = 6.3;

    /// <summary>
    /// Пример 9 Пособия к СП 63.13330.2018: Rb=14,5 МПа, Rs=340 МПа,
    /// b'f=400, h'f=100, b=200, h=600 мм, a=70 мм, As=1964 мм².
    /// </summary>
    [Fact]
    public void Sp63_2018_Example9_TeeCaseB_PassesWithRecomputedCapacity()
    {
        var section = TeeSection(
            flangeThickness: 0.100,
            concreteResistance: 14_500.0,
            rebarResistance: 340_000.0,
            tensionCoordinate: 0.300 - 0.070,
            tensionArea: 0.001964,
            rebarDiameter: 0.025);

        var result = Check(section, moment: 300.0);

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.True(result.StrengthPassed);
        var detail = Assert.Single(result.StrengthDetails);
        Assert.Equal("(8.8)", detail.Formula);
        Assert.Equal("8.1.11", detail.NormReference);

        // Ф. (3.28)-(3.29): Aov=.02 м², x=.130262 м, Mult=314.8089 кН·м.
        Assert.Equal(0.13026206896551723, result.Variables["x"], precision: 12);
        Assert.Equal(314.80890041379314, detail.Allowable, precision: 9);
        Assert.Equal(0.400, result.Variables["compressionFlangeEffectiveWidth"], precision: 12);
        Assert.Equal(0.0, result.Variables["AsPrime"], precision: 12);
        Assert.Equal(0.0, result.Variables["neutralAxisInFlange"]);
    }

    /// <summary>
    /// Пример IV.Б.8.1 Пособия Краковского к СП 63.13330.2012:
    /// Rb=7,65 МПа после γb1=0,9, Rs=350 МПа, b'f=400, h'f=120,
    /// b=200, h=600 мм, четыре Ø25 и M=270 кН·м.
    /// Два ряда растянутой арматуры заменены результирующим эффективным слоем
    /// в центре тяжести: a=(40+80)/2=60 мм.
    /// </summary>
    [Fact]
    public void Krakovsky_IVB8_1_TeeCaseB_MatchesHandbookCapacity()
    {
        double tensionArea = 4.0 * Math.PI * 0.025 * 0.025 / 4.0;
        var section = TeeSection(
            flangeThickness: 0.120,
            concreteResistance: 7_650.0,
            rebarResistance: 350_000.0,
            tensionCoordinate: 0.300 - 0.060,
            tensionArea: tensionArea,
            rebarDiameter: 0.025);

        var result = Check(section, moment: 270.0);

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.True(result.StrengthPassed);
        var detail = Assert.Single(result.StrengthDetails);
        Assert.Equal("(8.8)", detail.Formula);

        // Ф. (8.7)-(8.8): Aov=.024 м², x=.329165616 м, Mult=277.1969 кН·м.
        Assert.Equal(0.32916561632207, result.Variables["x"], precision: 11);
        Assert.Equal(277.196879934248, detail.Allowable, precision: 9);
        Assert.Equal(0.400, result.Variables["compressionFlangeEffectiveWidth"], precision: 12);
        Assert.Equal(0.0, result.Variables["AsPrime"], precision: 12);
        Assert.Equal(270.0, detail.Applied, precision: 12);
    }

    static Sp63NormalResult Check(CrossSection section, double moment) =>
        Sp63NormalChecker.Check(section,
            new LoadItem { N = 0.0, Mx = moment, My = 0.0 },
            CalcType.C,
            Sp63NormalFixtures.MemberOptions() with
            {
                ShapeKind = Sp63NormalShapeKind.Tee,
                Axis = Sp63NormalAxis.Mx
            },
            SpanLength);

    static CrossSection TeeSection(double flangeThickness,
        double concreteResistance, double rebarResistance,
        double tensionCoordinate, double tensionArea, double rebarDiameter)
    {
        const double flangeWidth = 0.400;
        const double webWidth = 0.200;
        const double height = 0.600;
        double y0 = -height / 2.0;
        double y1 = height / 2.0;
        double yf = y0 + flangeThickness;

        var concrete = Sp63NormalFixtures.ConcreteRegion(Concrete(concreteResistance),
        [
            (-flangeWidth / 2.0, y0),
            ( flangeWidth / 2.0, y0),
            ( flangeWidth / 2.0, yf),
            ( webWidth / 2.0, yf),
            ( webWidth / 2.0, y1),
            (-webWidth / 2.0, y1),
            (-webWidth / 2.0, yf),
            (-flangeWidth / 2.0, yf)
        ]);
        var section = new CrossSection { Tag = "handbook_tee" };
        section.Areas.Add(concrete);
        Sp63NormalFixtures.AddBar(section, 0.0, tensionCoordinate, tensionArea,
            Rebar(rebarResistance), diameter: rebarDiameter);
        return section;
    }

    static Material Concrete(double resistance) => new()
    {
        Id = 310,
        Tag = "handbook-concrete",
        Type = MatType.Concrete,
        E = 30_000_000.0,
        C = new MaterialChars(CalcType.C)
        {
            Type = MatType.Concrete,
            Fc = -resistance,
            Ft = 1_000.0,
            E = 30_000_000.0,
            Ec1Red = -0.0015,
            Ec2 = -0.0035,
            Et1Red = 0.00008,
            Et2 = 0.00015
        }
    };

    static Material Rebar(double resistance) => new()
    {
        Id = 311,
        Tag = "handbook-rebar",
        Type = MatType.ReSteelF,
        E = 200_000_000.0,
        C = new MaterialChars(CalcType.C)
        {
            Type = MatType.ReSteelF,
            Fc = -resistance,
            Ft = resistance,
            E = 200_000_000.0,
            Ec2 = -0.025,
            Et2 = 0.025
        }
    };
}
