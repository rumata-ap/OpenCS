using CScore;
using Xunit;

namespace CScore.Tests;

/// <summary>Проверяет направление момента при переводе строки ForceSet в Load.</summary>
public class ForceSetSignConventionTests
{
    [Fact]
    public void LoadItemToLoad_PositiveMxProducesTensionAtPositiveY()
    {
        var section = SectionCutFixtures.BuildReinforcedRectangle(0.3, 0.6);
        section.ResolveAndBuildDiagramms(0.85, pool: null, rebarDifferentialDiagram: false);
        var reference = new Kurvature { e0 = 0, ky = 0.0001, kz = 0 };
        var response = section.Integral(reference, CalcType.C, ten: true);
        Assert.True(response.Mx > 0);

        var item = new LoadItem { N = response.N, Mx = response.Mx, My = response.My };
        var target = item.ToLoad();
        var solver = new StrainSolver(section, CalcType.C, ten: true, tol: 1e-3, maxIter: 60);
        var solved = solver.Solve(target.N, target.Mx, target.My, initialGuess: reference);

        Assert.True(solver.Converged, $"Residual={solver.Residual}");
        section.SetEps(solved, CalcType.C, ten: true);
        var fibers = section.Areas.SelectMany(area => area.Fibers).ToList();
        double upper = fibers.Where(fiber => fiber.Y > 0.2).Average(fiber => fiber.Eps);
        double lower = fibers.Where(fiber => fiber.Y < -0.2).Average(fiber => fiber.Eps);
        Assert.True(upper > 0);
        Assert.True(lower < 0);
        Assert.True(upper > lower);
    }
}
