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

    /// <summary>
    /// Растянутый стержень: из стали вычитается ровно то, что даёт бетонная сетка в той же точке —
    /// до трещины σbt, после εbt,ult и при выключенном растяжении бетона — ноль.
    /// </summary>
    [Theory]
    [InlineData(CalcType.C, 0.0001, true)]
    [InlineData(CalcType.C, 0.00005, false)]
    [InlineData(CalcType.C, 0.0005, true)]
    [InlineData(CalcType.C, 0.0005, false)]
    [InlineData(CalcType.C, 0.01, true)]
    [InlineData(CalcType.N, 0.0001, true)]
    [InlineData(CalcType.N, 0.002, true)]
    [InlineData(CalcType.N, 0.002, false)]
    public void Differential_TensionedRebar_SubtractsWhatConcreteMeshCarries(CalcType ct, double eps, bool ten)
    {
        var section = SectionWithHostedRebar(differential: true);
        var concrete = section.Areas.First(a => a.Category == AreaCategory.Region);
        var dgr = section.Areas.First(a => a.Category == AreaCategory.RebarGroup).Diagramms[ct];

        double steel = dgr.Own!.SigValue(eps, ten);
        double mesh = concrete.Diagramms[ct].SigValue(eps, ten);
        Assert.Equal(steel - mesh, dgr.SigValue(eps, ten), 6);

        dgr.Sig(eps, out double e2Diff, ten);
        dgr.Own.Sig(eps, out double e2Steel, ten);
        concrete.Diagramms[ct].Sig(eps, out double e2Concrete, ten);
        Assert.Equal(e2Steel - e2Concrete, e2Diff, 3);
    }

    [Fact]
    public void Differential_CompressedRebar_UnchangedAndComprAOffGivesZero()
    {
        var section = SectionWithHostedRebar(differential: true);
        var concrete = section.Areas.First(a => a.Category == AreaCategory.Region);
        var dgr = section.Areas.First(a => a.Category == AreaCategory.RebarGroup).Diagramms[CalcType.C];

        Assert.Equal(dgr.Own!.SigValue(-0.003) - concrete.Diagramms[CalcType.C].SigValue(-0.003),
            dgr.SigValue(-0.003), 6);
        Assert.Equal(0.0, dgr.SigValue(-0.003, ten: true, ca: false), 9);
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
