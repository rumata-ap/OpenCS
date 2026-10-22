using CScore;
using Xunit;

namespace CScore.Tests;

/// <summary>
/// Сжатая ветвь диаграмм арматуры L2/L3 в НДМ: по умолчанию — ветвь растяжения на Rs, срезанная по Rsc
/// (п. 6.2.13: диаграммы с учётом сопротивлений растяжению и сжатию); запасной режим — симметрично по Rs.
/// Числа — справочник OpenCS.App/DataSource/Арматура стальная_*.csv, кПа.
/// </summary>
public class RebarCompressionRscTests
{
    const double E = 200_000_000;

    static MaterialChars A500(double rsc, double rs) => new()
    {
        Type = MatType.ReSteelF, Fc = -rsc, Rsc = rsc, Ft = rs, E = E,
        Ec2 = -0.0035, Et2 = 0.025,
    };

    static MaterialChars A600(double rsc, double rs) => new()
    {
        Type = MatType.ReSteelU, Fc = -rsc, Rsc = rsc, Ft = rs, E = E,
        Ec2 = -0.0035, Et2 = 0.015,
    };

    [Fact]
    public void L2_A500_C_CompressionPlateauAtRsc()
    {
        var d = A500(400_000, 435_000).D2L();
        Assert.Equal(-400_000, d.SigValue(-0.0025), 6);
        Assert.Equal(-400_000, d.SigValue(-0.0020), 6);
        Assert.Equal(-300_000, d.SigValue(-0.0015), 6);   // упругий участок E·ε
        Assert.Equal(435_000, d.SigValue(0.0030), 6);     // растяжение не меняется
    }

    [Fact]
    public void L2_A500_C_LegacyByRs_Symmetric()
    {
        var d = A500(400_000, 435_000).D2L(compressionByRs: true);
        Assert.Equal(-435_000, d.SigValue(-0.0025), 6);
    }

    [Fact]
    public void L2_A500_CL_RscEqualsRs_Unchanged()
    {
        var clipped = A500(435_000, 435_000).D2L();
        var legacy = A500(435_000, 435_000).D2L(compressionByRs: true);
        foreach (double eps in new[] { -0.0035, -0.0025, -0.002175, -0.001, 0.0 })
            Assert.Equal(legacy.SigValue(eps), clipped.SigValue(eps), 6);
    }

    [Fact]
    public void L3_A600_C_BecomesBilinearAtRsc()
    {
        // 0,9·Rs = 468 > Rsc = 400 → срезка на первом участке, ветвь двухлинейная.
        var d = A600(400_000, 520_000).D3L();
        Assert.Equal(-200_000, d.SigValue(-0.001), 6);
        Assert.Equal(-400_000, d.SigValue(-0.002), 6);
        Assert.Equal(-400_000, d.SigValue(-0.0035), 6);
        Assert.Equal(0.9 * 520_000, d.SigValue(0.9 * 520_000 / E), 6);
    }

    [Fact]
    public void L3_A600_CL_ClippedOnSecondSegment()
    {
        // 0,9·Rs = 468 < Rsc = 470 < 1,1·Rs = 572 → срезка на втором (наклонном) участке.
        var d = A600(470_000, 520_000).D3L();
        var legacy = A600(470_000, 520_000).D3L(compressionByRs: true);
        double e1 = 0.9 * 520_000 / E;
        Assert.Equal(legacy.SigValue(-e1), d.SigValue(-e1), 6);
        Assert.Equal(-470_000, d.SigValue(-0.0035), 6);
        Assert.Equal(-572_000, legacy.SigValue(-0.0080), 6);
        // Точка срезки лежит на прежнем наклонном участке.
        double eps = -e1 - 0.00005;   // до точки срезки (≈ e1 + 0,000087)
        Assert.Equal(legacy.SigValue(eps), d.SigValue(eps), 6);
    }

    [Fact]
    public void LegacyMaterialWithoutRsc_NoClipping()
    {
        var m = new MaterialChars { Type = MatType.ReSteelF, Ft = 435_000, E = E, Ec2 = -0.0035, Et2 = 0.025 };
        Assert.Equal(-435_000, m.D2L().SigValue(-0.0030), 6);
    }
}
