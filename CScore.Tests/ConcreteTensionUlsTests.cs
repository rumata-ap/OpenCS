using CScore;
using Xunit;

namespace CScore.Tests;

public class ConcreteTensionUlsTests
{
    [Fact]
    public void Integral_TenFalse_SuppressesConcreteTensionUnderPureBending()
    {
        var section = SectionCutFixtures.BuildReinforcedRectangle(0.3, 0.6);
        // Чистый изгиб относительно оси Y: растяжение при y>0, сжатие при y<0.
        // ky подобран так, чтобы деформация растянутой грани (y=0.3) оставалась
        // ниже εbt,ult=Et2=0.00015 (иначе бетон там уже треснул и tenB на него не влияет).
        var k = new Kurvature { e0 = 0, ky = 0.0002, kz = 0 };

        var withTension    = section.Integral(k, CalcType.C, ten: true);
        var withoutTension = section.Integral(k, CalcType.C, ten: false);

        // При отключённой работе бетона на растяжение суммарная осевая сила N
        // смещается в сторону сжатия (растянутая зона бетона перестаёт вносить
        // положительный вклад в N; арматура работает в растяжении независимо от ten).
        Assert.True(withoutTension.N < withTension.N,
            $"withoutTension.N={withoutTension.N} должно быть меньше withTension.N={withTension.N}");
    }

    [Fact]
    public void Integral_TenFalse_DoesNotAffectPureCompression()
    {
        var section = SectionCutFixtures.BuildReinforcedRectangle(0.3, 0.6);
        // Чистое сжатие: вся эпюра деформаций отрицательна (eps<0) — бетон везде в сжатии
        var k = new Kurvature { e0 = -0.0005, ky = 0, kz = 0 };

        var withTension    = section.Integral(k, CalcType.C, ten: true);
        var withoutTension = section.Integral(k, CalcType.C, ten: false);

        Assert.Equal(withTension.N, withoutTension.N, precision: 6);
    }
}
