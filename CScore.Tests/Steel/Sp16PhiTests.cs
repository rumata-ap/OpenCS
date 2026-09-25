using CScore.Sp16;
using Xunit;

namespace CScore.Tests.Steel;

/// <summary>φ по формулам (8), (9) СП 16 против табл. Д.1 (значения ×1000, редакция изм. № 6).</summary>
public class Sp16PhiTests
{
    // λ̄, a, b, c; объединённые ячейки: два значения — (a = b, c), одно — a = b = c.
    static readonly double[][] TableD1 =
    [
        [0.4, 1000, 1000, 984], [0.6, 994, 986, 956], [0.8, 981, 967, 929], [1.0, 968, 948, 901],
        [1.2, 953, 927, 872], [1.4, 938, 905, 842], [1.6, 920, 881, 811], [1.8, 900, 855, 778],
        [2.0, 877, 826, 744], [2.2, 851, 794, 709], [2.4, 821, 760, 672], [2.6, 786, 723, 635],
        [2.8, 747, 683, 598], [3.0, 704, 643, 562], [3.2, 660, 602, 527], [3.4, 616, 562, 493],
        [3.6, 572, 524, 460], [3.8, 526, 487, 430], [4.0, 475, 453, 402], [4.2, 431, 422, 375],
        [4.4, 393, 392, 351], [4.6, 359, 329], [4.8, 330, 308], [5.0, 304, 289], [5.2, 281, 271],
        [5.4, 261, 255], [5.6, 242, 241], [5.8, 226], [6.0, 211], [6.2, 198], [6.4, 186], [6.6, 174],
        [6.8, 164], [7.0, 155], [7.2, 147], [7.4, 139], [7.6, 132], [7.8, 125], [8.0, 119], [8.5, 105],
        [9.0, 94], [9.5, 84], [10.0, 76],
    ];

    public static IEnumerable<object[]> Rows()
    {
        foreach (var r in TableD1)
        {
            double a, b, c;
            if (r.Length == 4) { a = r[1]; b = r[2]; c = r[3]; }
            else if (r.Length == 3) { a = b = r[1]; c = r[2]; }
            else { a = b = c = r[1]; }
            yield return [r[0], SectionCurve.a, a];
            yield return [r[0], SectionCurve.b, b];
            yield return [r[0], SectionCurve.c, c];
        }
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void Phi_MatchesTableD1(double lambdaBar, SectionCurve curve, double expected1000)
    {
        double phi = Sp16Stability.Phi(lambdaBar, curve);
        Assert.InRange(phi * 1000, expected1000 - 1.0, expected1000 + 1.0);
    }

    [Fact]
    public void LambdaBar_IsLambdaTimesSqrtRyOverE()
    {
        // λ = 100, Ry = 240 МПа, E = 2,06·10⁵ МПа → λ̄ = 100·√(240/206000) = 3,413.
        Assert.Equal(3.4133, Sp16Stability.LambdaBar(100, 240000, 2.06e8), 3);
    }

    [Fact]
    public void GammaRes_C255_FollowsFormula7a()
    {
        Assert.Equal(1.0, Sp16Stability.GammaRes(2.5, 245000, out _), 6);
        Assert.Equal(1.1, Sp16Stability.GammaRes(4.0, 245000, out _), 6);
    }

    [Fact]
    public void GammaRes_C390AboveFive_IsNotAppliedBecauseOfMisprint()
    {
        Assert.Equal(1.0, Sp16Stability.GammaRes(5.5, 390000, out var note), 6);
        Assert.NotNull(note);
    }

    [Fact]
    public void GammaRes_Interpolates_BetweenC255AndC390()
    {
        // Ry = 317,5 МПа — середина; λ̄ = 4: С255 → 1,1, С390 → 1,0.
        Assert.Equal(1.05, Sp16Stability.GammaRes(4.0, 317500, out _), 6);
    }
}
