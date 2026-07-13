using Xunit;
using CScore;

namespace CScore.Tests;

// Знаковая конвенция OpenCS: Mx = ∫σ·y·dA, поэтому ПОЛОЖИТЕЛЬНЫЙ Mx растягивает
// ВЕРХНЮЮ грань (y > 0). Тестовое сечение RectWithBottomRebar армировано СНИЗУ, значит
// момент, при котором в работу включается растянутая арматура, должен быть ОТРИЦАТЕЛЬНЫМ.
// При положительном моменте растянут верх, где стержней нет, а бетон на растяжение
// в трещиноватой стадии отключён — равновесия не существует и решатель не сходится
// (это не дефект метода Ньютона, а физически неразрешимая постановка).
public class CrackWidthSolverTests
{
    [Fact]
    public void Compute_BelowCrackingMoment_NoCracks()
    {
        var section = TestSections.RectWithBottomRebar();
        var solver = new CrackWidthSolver(section);

        // Момент заведомо ниже Mcrc чистого изгиба (знак — растяжение снизу).
        var res = solver.Compute(N: 0.0, mxLong: -0.5, mxTotal: -0.5);

        Assert.False(res.Cracked);
        Assert.Equal(0.0, res.AcrcLong);
        Assert.Equal(0.0, res.AcrcShort);
        Assert.True(res.PassedLong);
        Assert.True(res.PassedShort);
    }

    [Fact]
    public void Compute_AboveCrackingMoment_AcrcGrowsWithMoment()
    {
        var section = TestSections.RectWithBottomRebar();
        // Направление трещинообразования — растяжение снизу (арматура), поэтому (0, -1, 0).
        var mcrc = new CrackingSolver(section, CalcType.CL).CrackingMoment(0, -1, 0).Mx;

        // Множители выше Mcrc, но ниже предельного момента сечения (Mult ≈ 4.4·Mcrc:
        // при 5·Mcrc арматура уже потекла и равновесия не существует — это не дефект
        // решателя, а исчерпание несущей способности).
        var solver = new CrackWidthSolver(section);
        var low = solver.Compute(N: 0.0, mxLong: mcrc * 2.0, mxTotal: mcrc * 2.0);
        var high = solver.Compute(N: 0.0, mxLong: mcrc * 4.0, mxTotal: mcrc * 4.0);

        Assert.True(low.Cracked);
        Assert.True(high.Cracked);
        Assert.True(high.AcrcLong > low.AcrcLong);
    }

    [Fact]
    public void Compute_LongEqualsTotal_ShortEqualsLong()
    {
        var section = TestSections.RectWithBottomRebar();
        var mcrc = new CrackingSolver(section, CalcType.CL).CrackingMoment(0, -1, 0).Mx;

        var solver = new CrackWidthSolver(section);
        var res = solver.Compute(N: 0.0, mxLong: mcrc * 4.0, mxTotal: mcrc * 4.0);

        Assert.True(res.Cracked);
        Assert.Equal(res.AcrcLong, res.AcrcShort, 6);
    }
}
