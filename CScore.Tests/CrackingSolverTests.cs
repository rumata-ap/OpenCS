using Xunit;
using CScore;

namespace CScore.Tests;

public class CrackingSolverTests
{
    [Fact]
    public void CrackingMoment_PureBending_Converges()
    {
        var section = TestSections.RectWithBottomRebar();
        var solver = new CrackingSolver(section, CalcType.CL);

        var res = solver.CrackingMoment(N: 0.0, Mx: 1.0, My: 0.0);

        Assert.True(res.Converged);
        Assert.True(res.Mx > 0);
        Assert.Equal(0.0, res.My, 6);
    }

    [Fact]
    public void CrackingMoment_GrowsWithSectionHeight()
    {
        var small = TestSections.RectWithBottomRebar(h: 0.3);
        var large = TestSections.RectWithBottomRebar(h: 0.6);

        var mcrcSmall = new CrackingSolver(small, CalcType.CL).CrackingMoment(0, 1, 0).Mx;
        var mcrcLarge = new CrackingSolver(large, CalcType.CL).CrackingMoment(0, 1, 0).Mx;

        Assert.True(mcrcLarge > mcrcSmall);
    }

    [Fact]
    public void TensionLimit_ReturnsPositiveConcreteTensionStrain()
    {
        var section = TestSections.RectWithBottomRebar();
        var solver = new CrackingSolver(section, CalcType.CL);

        double limit = solver.TensionLimit();

        Assert.True(limit > 0.0);
    }

    // Инцидент 2026-07-15: CrackWidthSolver.Compute() передавал в CrackingMoment "сырой"
    // (немасштабированный к единичному вектору) момент как направление, а отдельная задача
    // "Момент трещинообразования" нормирует направление к единичному вектору перед вызовом.
    // CrackingMoment должен давать физически одинаковый Mcrc независимо от того, какой
    // магнитудой представлено направление — бисекция ищет масштаб вдоль направления, поэтому
    // результат обязан быть инвариантен к масштабу входного вектора.
    [Fact]
    public void CrackingMoment_InvariantToDirectionVectorMagnitude()
    {
        var section = TestSections.RectWithBottomRebar();
        var solver = new CrackingSolver(section, CalcType.CL);

        // Единичное направление (как делает CrackingHandler) против "сырого" момента большой
        // величины в ту же сторону (как раньше делал CrackWidthSolver.Compute()).
        var resUnit = solver.CrackingMoment(N: 0.0, Mx: -1.0, My: 0.0);
        var resRaw  = solver.CrackingMoment(N: 0.0, Mx: -50.0, My: 0.0);

        Assert.True(resUnit.Converged);
        Assert.True(resRaw.Converged);
        Assert.Equal(resUnit.Mx, resRaw.Mx, 6);
    }
}
