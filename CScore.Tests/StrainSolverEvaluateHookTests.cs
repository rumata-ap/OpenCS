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

    [Fact]
    public void EvaluateJacobian_UsesConfiguredEvaluateAndColumnOrder()
    {
        var section = BuildThreePointSection();
        var solver = new StrainSolver(section, h: 1e-6, centralJacobian: true,
            evaluate: k => new Load
            {
                N = 2.0 * k.e0 + 3.0 * k.ky + 5.0 * k.kz,
                Mx = 7.0 * k.e0 + 11.0 * k.ky + 13.0 * k.kz,
                My = 17.0 * k.e0 + 19.0 * k.ky + 23.0 * k.kz
            });

        var jacobian = solver.EvaluateJacobian(new Kurvature { e0 = 0.3, ky = -0.2, kz = 0.1 });

        Assert.Equal(["N", "Mx", "My"], jacobian.Rows);
        Assert.Equal(["e0", "ky", "kz"], jacobian.Columns);
        Assert.Equal("central", jacobian.Scheme);
        Assert.Equal(1e-6, jacobian.Step, 12);
        Assert.Equal(2.0, jacobian[0, 0], 8);
        Assert.Equal(3.0, jacobian[0, 1], 8);
        Assert.Equal(5.0, jacobian[0, 2], 8);
        Assert.Equal(7.0, jacobian[1, 0], 8);
        Assert.Equal(11.0, jacobian[1, 1], 8);
        Assert.Equal(13.0, jacobian[1, 2], 8);
        Assert.Equal(17.0, jacobian[2, 0], 8);
        Assert.Equal(19.0, jacobian[2, 1], 8);
        Assert.Equal(23.0, jacobian[2, 2], 8);
    }
}
