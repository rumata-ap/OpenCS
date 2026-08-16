using CScore;
using Xunit;

namespace CScore.Tests;

public class StrainSolverEvaluateHookTests
{
    static CrossSection BuildThreePointSection()
    {
        var material = new Material
        {
            Id = 1,
            Type = MatType.ReSteelF,
            E = 200_000_000.0,
            MaterialChars =
            [
                RebarChars(CalcType.C),
                RebarChars(CalcType.CL),
                RebarChars(CalcType.N),
                RebarChars(CalcType.NL)
            ]
        };
        var area = new MaterialArea
        {
            Id = 1,
            Category = AreaCategory.RebarGroup,
            Material = material,
            MaterialId = material.Id,
            DiagrammType = DiagrammType.L2
        };
        foreach (var (x, y) in new[] { (-0.10, -0.08), (0.10, -0.08), (0.0, 0.12) })
        {
            var bar = Fiber.CreatePoint(0.02, x, y);
            bar.Area = 0.001;
            area.Fibers.Add(bar);
        }

        var section = new CrossSection { Id = 1, Areas = [area] };
        section.ResolveAndBuildDiagramms(rebarDifferentialDiagram: false);
        return section;
    }

    static MaterialChars RebarChars(CalcType calc) => new()
    {
        Type = MatType.ReSteelF,
        TypeCalc = calc,
        Fc = -400_000,
        Ft = 400_000,
        E = 200_000_000,
        Ec2 = -0.0035,
        Et2 = 0.025
    };

    [Fact]
    public void Solve_WithoutEvaluate_MatchesSectionIntegral()
    {
        var section = BuildThreePointSection();

        var solverDefault = new StrainSolver(section, CalcType.C, tol: 0.01, maxIter: 40);
        var planeDefault = solverDefault.Solve(nTarget: 10.0, mxTarget: 0.0, myTarget: 0.0);

        var solverWithIdentityEvaluate = new StrainSolver(section, CalcType.C, tol: 0.01, maxIter: 40,
            evaluate: k => section.Integral(k, CalcType.C, true, true));
        var planeWithEvaluate = solverWithIdentityEvaluate.Solve(nTarget: 10.0, mxTarget: 0.0, myTarget: 0.0);

        Assert.True(solverDefault.Converged);
        Assert.True(solverWithIdentityEvaluate.Converged);
        Assert.Equal(planeDefault.e0, planeWithEvaluate.e0, 8);
        Assert.Equal(planeDefault.ky, planeWithEvaluate.ky, 8);
        Assert.Equal(planeDefault.kz, planeWithEvaluate.kz, 8);
    }

    [Fact]
    public void Solve_WithCustomEvaluate_UsesEvaluateInsteadOfIntegral()
    {
        var section = BuildThreePointSection();
        int evaluateCalls = 0;

        var solver = new StrainSolver(section, CalcType.C, tol: 0.01, maxIter: 40,
            evaluate: k =>
            {
                evaluateCalls++;
                var load = section.Integral(k, CalcType.C, true, true);
                load.N *= 2.0;
                return load;
            });
        var plane = solver.Solve(nTarget: 10.0, mxTarget: 0.0, myTarget: 0.0);
        var realLoad = section.Integral(plane, CalcType.C, true, true);

        Assert.True(solver.Converged);
        Assert.True(evaluateCalls > 0);
        Assert.Equal(5.0, realLoad.N, 1);
    }
}
