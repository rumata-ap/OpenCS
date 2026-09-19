using CScore.Sp63.Normal;
using CScore.ParametricRc;
using CScore.Tests.Sp63Normal;
using Xunit;

namespace CScore.Tests.ParametricRc;

public sealed class ParametricRcNdmIntegrationTests
{
    [Fact]
    public void GeneratedPhysicalRectangleMatchesManualAnalogInNdmResponse()
    {
        var definition = ParametricRcSectionDefinition.Rectangle(0.30, 0.50) with
        {
            LowerRebar = ParametricLongitudinalLayer.Physical(2, 0.016, -0.21),
            UpperRebar = ParametricLongitudinalLayer.Physical(2, 0.016, 0.21)
        };
        var generated = Materialized(definition);
        var manual = ManualAnalog(generated);
        var reference = new Kurvature { e0 = -0.00015, ky = 0.0012, kz = 0.0004 };
        var target = generated.Integral(reference, CalcType.C, ten: true);

        var generatedSolver = new StrainSolver(generated, CalcType.C,
            ten: true, tol: 1e-8, maxIter: 80);
        var manualSolver = new StrainSolver(manual, CalcType.C,
            ten: true, tol: 1e-8, maxIter: 80);
        var generatedPlane = generatedSolver.Solve(target.N, target.Mx, target.My, reference);
        var manualPlane = manualSolver.Solve(target.N, target.Mx, target.My, reference);

        Assert.True(generatedSolver.Converged, $"generated residual={generatedSolver.Residual}");
        Assert.True(manualSolver.Converged, $"manual residual={manualSolver.Residual}");
        Assert.Equal(generatedPlane.e0, manualPlane.e0, 6);
        Assert.Equal(generatedPlane.ky, manualPlane.ky, 6);
        Assert.Equal(generatedPlane.kz, manualPlane.kz, 6);
        var manualResponse = manual.Integral(manualPlane, CalcType.C, ten: true);
        Assert.Equal(target.N, manualResponse.N, 6);
        Assert.Equal(target.Mx, manualResponse.Mx, 6);
        Assert.Equal(target.My, manualResponse.My, 6);
    }

    [Fact]
    public void GeneratedPhysicalRectangleMatchesManualAnalogInSp63NormalCheck()
    {
        var definition = ParametricRcSectionDefinition.Rectangle(0.30, 0.50) with
        {
            LowerRebar = ParametricLongitudinalLayer.Physical(2, 0.016, -0.21),
            UpperRebar = ParametricLongitudinalLayer.Physical(2, 0.016, 0.21)
        };
        var generated = Materialized(definition);
        var manual = ManualNormalAnalog(generated);
        var load = new LoadItem { N = 0.0, Mx = -40.0, My = 0.0 };
        var options = Sp63NormalFixtures.MemberOptions();

        var generatedResult = Sp63NormalChecker.Check(
            generated, load, CalcType.C, options);
        var manualResult = Sp63NormalChecker.Check(
            manual, load, CalcType.C, options);

        Assert.Equal(manualResult.Status, generatedResult.Status);
        Assert.Equal(manualResult.StrengthPassed, generatedResult.StrengthPassed);
        Assert.Equal(manualResult.Branch, generatedResult.Branch);
        Assert.Equal(
            manualResult.StrengthDetails.Select(d => d.Applied),
            generatedResult.StrengthDetails.Select(d => d.Applied));
    }

    static CrossSection Materialized(ParametricRcSectionDefinition definition)
    {
        var section = ParametricRcSectionGenerator.Generate(definition).Section;
        var concrete = SectionCutFixtures.BuildConcreteMaterial();
        var steel = SectionCutFixtures.BuildSteelMaterial();
        foreach (var area in section.Areas)
        {
            if (area.Category == AreaCategory.Region)
                area.SetMaterial(concrete, DiagrammType.L2);
            else if (area.Category == AreaCategory.RebarGroup)
                area.SetMaterial(steel, DiagrammType.L2);
        }
        section.ResolveAndBuildDiagramms(rebarDifferentialDiagram: false);
        return section;
    }

    static CrossSection ManualAnalog(CrossSection generated)
    {
        var concrete = generated.Areas.First(a => a.Category == AreaCategory.Region).Clone();
        var rebars = generated.Areas.Where(a => a.Category == AreaCategory.RebarGroup)
            .Select(a =>
            {
                var rebar = a.Clone();
                rebar.HostArea = concrete;
                rebar.HostAreaId = concrete.Id;
                return rebar;
            }).ToList();
        var manual = new CrossSection { Areas = [concrete, .. rebars] };
        manual.ResolveAndBuildDiagramms(rebarDifferentialDiagram: false);
        return manual;
    }

    static CrossSection ManualNormalAnalog(CrossSection generated)
    {
        var generatedConcrete = generated.Areas.Single(a => a.Category == AreaCategory.Region);
        var manual = new CrossSection
        {
            Areas = [generatedConcrete.Clone()]
        };
        var steel = generated.Areas.First(a => a.Category == AreaCategory.RebarGroup).Material!;
        var manualRebar = new MaterialArea
        {
            Category = AreaCategory.RebarGroup,
            Material = steel,
            MaterialId = steel.Id,
            Fibers = generated.Areas
                .Where(a => a.Category == AreaCategory.RebarGroup)
                .SelectMany(a => a.Fibers)
                .Select(f => new Fiber(f.X, f.Y)
                {
                    TypeFiber = FiberType.point,
                    Area = f.Area,
                    Diameter = f.Diameter
                }).ToList()
        };
        manual.Areas.Add(manualRebar);
        manual.ResolveAndBuildDiagramms(rebarDifferentialDiagram: false);
        return manual;
    }
}
