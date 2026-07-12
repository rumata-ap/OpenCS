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

    static Diagramm BuildConcreteDiagram()
    {
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            Type = MatType.Concrete, E = 30_000, Fc = 30, Ft = 2.0, Ry = 2.0, Ru = 30,
            Ec0 = -0.002, Ec2 = -0.0035, Ec1Red = -0.0035 * 0.6, Et2 = 0.00015, Et1Red = 0.00015 * 0.6,
        };
        var m = new Material { Id = 5, E = 30_000, Type = MatType.Concrete, Tag = "c30" };
        m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
        return m.GetDiagramms(DiagrammType.SP63)![CalcType.C];
    }

    static Diagramm BuildRebarDiagram()
    {
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            E = 200_000, Ry = 400, Ru = 400, Ft = 400, Fc = -400,
            Ec2 = -0.025, Et2 = 0.025, Type = MatType.ReSteelF,
        };
        var m = new Material { Id = 6, E = 200_000, Type = MatType.ReSteelF, Tag = "a400" };
        m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
        return m.GetDiagramms(DiagrammType.L2)![CalcType.C];
    }

    [Fact]
    public void PlateSection_TensionOverrideFalse_SuppressesTensionEvenIfFlagTrue()
    {
        var cDiag = BuildConcreteDiagram();
        var rDiag = BuildRebarDiagram();
        var plate = new PlateSection { H = 0.2, NLayers = 20, TensionConcrete = true, PlateModel = "layered" };

        // Чистый изгиб kx>0: нижние слои (z<0) растянуты, верхние (z>0) сжаты.
        // kx подобран так, чтобы деформация растянутой грани (z=H/2=0.1) оставалась
        // ниже εbt,ult=Et2=0.00015 (иначе бетон там уже треснул и tenB на него не влияет).
        var state = new ShellStrainState(0, 0, 0, 0.001, 0, 0);

        var withFlagTension = plate.Compute(state, cDiag, rDiag, computeStiffness: false);
        var withOverrideOff = plate.Compute(state, cDiag, rDiag, computeStiffness: false, tensionOverride: false);

        Assert.NotEqual(withFlagTension.Mx, withOverrideOff.Mx);
    }

    [Fact]
    public void PlateSection_TensionOverrideNull_KeepsExistingFlagBehavior()
    {
        var cDiag = BuildConcreteDiagram();
        var rDiag = BuildRebarDiagram();
        var plateOn  = new PlateSection { H = 0.2, NLayers = 20, TensionConcrete = true,  PlateModel = "layered" };
        var plateOff = new PlateSection { H = 0.2, NLayers = 20, TensionConcrete = false, PlateModel = "layered" };
        var state = new ShellStrainState(0, 0, 0, 0.001, 0, 0);

        var resOn  = plateOn.Compute(state, cDiag, rDiag, computeStiffness: false, tensionOverride: null);
        var resOff = plateOff.Compute(state, cDiag, rDiag, computeStiffness: false, tensionOverride: null);

        Assert.NotEqual(resOn.Mx, resOff.Mx);
    }
}
