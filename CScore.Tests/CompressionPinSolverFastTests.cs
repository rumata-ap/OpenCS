using CScore;
using Xunit;

namespace CScore.Tests;

public class CompressionPinSolverFastTests
{
    [Fact]
    public void Solve_ConvergesAndMatchesEquilibrium()
    {
        var section = TestSections.Example47();
        var solver = new CompressionPinSolverFast(section, CalcType.N, ten: true);

        var result = solver.Solve(epsPin: -0.0008, n: 0.0, mx: -60.0, my: 0.0, dNdk: 0.0, seed: null);

        Assert.True(result.Converged);
        // Сжатый пин: минимальная (наиболее отрицательная) деформация по контуру бетона
        // действительно равна целевой epsPin.
        double minStrain = section.Areas
            .Where(a => a.Material?.Type == MatType.Concrete)
            .Where(a => a.Hull != null)
            .SelectMany(a => a.Hull!.X.Zip(a.Hull!.Y, (x, y) => result.Plane.e0 + result.Plane.ky * y + result.Plane.kz * x))
            .Min();
        Assert.Equal(-0.0008, minStrain, precision: 6);
    }
}
