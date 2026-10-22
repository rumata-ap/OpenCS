using System.Linq;
using CScore;
using Xunit;

namespace CScore.Tests;

/// <summary>
/// σ стержня по собственной диаграмме стали при разностной диаграмме (σs − σb):
/// равновесие идёт по разностной, показ — по чистой.
/// </summary>
public class RebarOwnStressTests
{
    static CrossSection SectionWithHostedRebar(bool differential)
    {
        var section = TestSections.RectWithBottomRebar();
        var concrete = section.Areas.First(a => a.Category == AreaCategory.Region);
        var rebar = section.Areas.First(a => a.Category == AreaCategory.RebarGroup);
        concrete.Id = 1;
        rebar.Id = 2;
        rebar.HostAreaId = concrete.Id;
        section.ResolveAndBuildDiagramms(0.85, pool: null, rebarDifferentialDiagram: differential);
        return section;
    }

    [Fact]
    public void Differential_CompressedRebar_SigOwnIsPureSteelAndSigIsDifferential()
    {
        var section = SectionWithHostedRebar(differential: true);
        var concrete = section.Areas.First(a => a.Category == AreaCategory.Region);
        var rebar = section.Areas.First(a => a.Category == AreaCategory.RebarGroup);
        var dgr = rebar.Diagramms[CalcType.C];
        Assert.NotNull(dgr.Own);

        var fiber = rebar.Fibers[0];
        fiber.Eps = -0.0025;
        dgr.Sig(fiber, tenB: false);

        double sigSteel = dgr.Own!.SigValue(-0.0025);
        double sigConcrete = concrete.Diagramms[CalcType.C].SigValue(-0.0025, ten: false);
        Assert.True(sigConcrete < 0);
        Assert.Equal(sigSteel, fiber.SigOwn, 6);
        Assert.Equal(sigSteel - sigConcrete, fiber.Sig, 6);
        Assert.Equal(fiber.Sig * fiber.Area, fiber.N, 9);
    }

    [Fact]
    public void NotDifferential_SigOwnEqualsSig()
    {
        var section = SectionWithHostedRebar(differential: false);
        var rebar = section.Areas.First(a => a.Category == AreaCategory.RebarGroup);
        var dgr = rebar.Diagramms[CalcType.C];
        Assert.Null(dgr.Own);

        var fiber = rebar.Fibers[0];
        fiber.Eps = -0.0025;
        dgr.Sig(fiber, tenB: false);

        Assert.Equal(fiber.Sig, fiber.SigOwn, 9);
    }

    [Fact]
    public void PsiCorrection_UpdatesSigOwnByOwnDiagramInCrack()
    {
        var section = SectionWithHostedRebar(differential: true);
        var rebar = section.Areas.First(a => a.Category == AreaCategory.RebarGroup);
        var dgr = rebar.Diagramms[CalcType.N];
        var fiber = rebar.Fibers[0];
        fiber.Eps = 0.0008;
        dgr.Sig(fiber, tenB: true);

        var epsCrc = new System.Collections.Generic.Dictionary<Fiber, double> { [fiber] = 0.0010 };
        Curvature8232.ApplyPsiCorrection(section, new Kurvature(), new Load(), epsCrc, CalcType.N);

        double psi = Curvature8232.PsiS(0.0010, 0.0008);
        Assert.Equal(dgr.Own!.SigValue(0.0008 / psi), fiber.SigOwn, 6);
        Assert.Equal(dgr.SigValue(0.0008 / psi), fiber.Sig, 6);
    }
}
