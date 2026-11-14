using Xunit;

namespace CScore.Tests;

/// <summary>
/// Бетонный прямоугольник сеткой nx = 1 (как у параметрического сечения): все волокна на x = 0,
/// My не зависит от kz, якобиан Ньютона вырожден. Раньше решатели плоскости деформаций уходили в
/// запасной шаг 1e-6·невязка и находили равновесие только для крошечных моментов (бетон 300×500, B25:
/// «Предельное усилие» с растяжением бетона давало Mx_lim = 0,37 кН·м). Теперь Ньютон идёт по осям с
/// жёсткостью. Ориентир — п. 7.1.12 СП 63: M_ult = Rbt·b·h²/6 = 1050·0,3·0,5²/6 = 13,1 кН·м; НДМ с
/// двухлинейной диаграммой растяжения даёт больше (≈ 18 кН·м, пластика растянутого бетона).
/// </summary>
public sealed class PlainConcreteDegenerateJacobianTests
{
    static CrossSection Section()
    {
        var area = new MaterialArea
        {
            Id = 1, Tag = "concrete", Category = AreaCategory.Region,
            Hull = new Contour([-0.15, 0.15, 0.15, -0.15, -0.15], [-0.25, -0.25, 0.25, 0.25, -0.25], "outer"),
        };
        area.SetWKT();
        area.SliceXY(nx: 1, ny: 50);
        var section = new CrossSection { Id = 1, Tag = "300×500", Areas = [area] };
        var concrete = new Material
        {
            Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000,
            MaterialChars = [.. new[] { CalcType.C, CalcType.CL, CalcType.N, CalcType.NL }.Select(ct => new MaterialChars(ct)
            {
                Type = MatType.Concrete, E = 30_000_000, Fc = -14_500, Ft = 1_050,
                Ec1Red = -0.0015, Ec2 = -0.0035, Et1Red = 8e-5, Et2 = 0.00015,
            })]
        };
        area.SetMaterial(concrete, DiagrammType.L2);
        section.ResolveAndBuildDiagramms(rebarDifferentialDiagram: false);
        return section;
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(10.0)]
    [InlineData(15.0)]
    public void StrainSolver_PlainConcreteBending_Converges(double mx)
    {
        var section = Section();
        var solver = new StrainSolver(section, CalcType.C, ten: true, tol: 0.1, maxIter: 25);

        var k = solver.Solve(0.0, mx, 0.0);

        Assert.True(solver.Converged, $"невязка {solver.Residual}, итераций {solver.Iterations}");
        var load = section.Integral(k, CalcType.C, true);
        // Невязка в пределах допуска решателя tol = 0,1.
        Assert.InRange(load.Mx, mx - 0.1, mx + 0.1);
        Assert.InRange(load.N, -0.1, 0.1);
    }

    [Fact]
    public void LimitForceSolver_PlainConcreteBending_AboveRbtW()
    {
        var solver = LimitForceSolver.ForCrossSection(Section(), CalcType.C, solverTol: 0.1, ten: true);

        var res = solver.AllFactor(0.0, 10.0, 0.0);

        Assert.True(res.Converged);
        Assert.InRange(res.MxLimit, 13.1, 20.0);
    }
}
