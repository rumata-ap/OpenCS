using Xunit;
using CScore;

namespace CScore.Tests;

public class CrackingSolverTests
{
    [Fact]
    public void CrackingMoment_PureBending_Converges()
    {
        var section = TestSections.RectWithBottomRebar();
        var solver = new CrackingSolver(section, CalcType.N);

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

        var mcrcSmall = new CrackingSolver(small, CalcType.N).CrackingMoment(0, 1, 0).Mx;
        var mcrcLarge = new CrackingSolver(large, CalcType.N).CrackingMoment(0, 1, 0).Mx;

        Assert.True(mcrcLarge > mcrcSmall);
    }

    [Fact]
    public void TensionLimit_ReturnsPositiveConcreteTensionStrain()
    {
        var section = TestSections.RectWithBottomRebar();
        var solver = new CrackingSolver(section, CalcType.N);

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
        var solver = new CrackingSolver(section, CalcType.N);

        // Единичное направление (как делает CrackingHandler) против "сырого" момента большой
        // величины в ту же сторону (как раньше делал CrackWidthSolver.Compute()).
        var resUnit = solver.CrackingMoment(N: 0.0, Mx: -1.0, My: 0.0);
        var resRaw  = solver.CrackingMoment(N: 0.0, Mx: -50.0, My: 0.0);

        Assert.True(resUnit.Converged);
        Assert.True(resRaw.Converged);
        Assert.Equal(resUnit.Mx, resRaw.Mx, 6);
    }

    [Fact]
    public void CrackingCurvature_PureBending_ConvergesAlongGivenRay()
    {
        var section = TestSections.RectWithBottomRebar();
        var solver = new CrackingSolver(section, CalcType.N);

        var res = solver.CrackingCurvature(N: 0.0, kyDir: 1.0, kzDir: 0.0);

        Assert.True(res.Converged);
        Assert.True(res.Ky > 0.0);
        Assert.Equal(0.0, res.Kz, 10);
        Assert.True(res.Mx > 0.0);
    }

    [Fact]
    public void CrackingCurvature_InvariantToDirectionVectorMagnitude()
    {
        var section = TestSections.RectWithBottomRebar();
        var solver = new CrackingSolver(section, CalcType.N);

        var resUnit = solver.CrackingCurvature(N: 0.0, kyDir: 1.0, kzDir: 0.0);
        var resScaled = solver.CrackingCurvature(N: 0.0, kyDir: 25.0, kzDir: 0.0);

        Assert.True(resUnit.Converged);
        Assert.True(resScaled.Converged);
        Assert.Equal(resUnit.Ky, resScaled.Ky, 6);
        Assert.Equal(resUnit.Mx, resScaled.Mx, 4);
    }

    // Регрессия на блокер ревью 2026-08-16: для сечения без EIxy=0 упругая кривизна
    // НЕ параллельна направлению момента, поэтому CrackingMoment (масштабирует момент)
    // и CrackingCurvature (масштабирует кривизну вдоль луча) в общем случае обязаны
    // расходиться. Если этот тест начнёт падать (Mx/My совпали) на текущей фикстуре —
    // либо фикстура перестала быть асимметричной, либо перекрёстный член жёсткости
    // сечения случайно занулился; проверить перед тем как менять допуски.
    [Fact]
    public void CrackingCurvature_DiffersFromCrackingMoment_ForAsymmetricSection()
    {
        var section = TestSections.RectWithCornerClusterRebar();
        var solver = new CrackingSolver(section, CalcType.N);

        var byMoment = solver.CrackingMoment(N: 0.0, Mx: 1.0, My: 1.0);
        var byCurvature = solver.CrackingCurvature(N: 0.0, kyDir: 1.0, kzDir: 1.0);

        Assert.True(byMoment.Converged);
        Assert.True(byCurvature.Converged);
        Assert.NotEqual(byMoment.Mx, byCurvature.Mx, 3);
        Assert.NotEqual(byMoment.My, byCurvature.My, 3);
    }

    // Ключевая инвариантность CrackingCurvature: контрольная точка обязана лежать
    // РОВНО на луче (uky,ukz), которым сканирует BiaxialCurvatureCurveSolver — иначе
    // именно это и есть блокер ревью.
    [Fact]
    public void CrackingCurvature_ResultLiesExactlyOnScanRay_ForAsymmetricSection()
    {
        var section = TestSections.RectWithCornerClusterRebar();
        var solver = new CrackingSolver(section, CalcType.N);
        double uky = 1.0 / Math.Sqrt(2), ukz = 1.0 / Math.Sqrt(2);

        var res = solver.CrackingCurvature(N: 0.0, kyDir: uky, kzDir: ukz);

        Assert.True(res.Converged);
        double mag = Math.Sqrt(res.Ky * res.Ky + res.Kz * res.Kz);
        Assert.True(mag > 1e-9);
        Assert.Equal(uky, res.Ky / mag, 6);
        Assert.Equal(ukz, res.Kz / mag, 6);
    }

    [Fact]
    public void CrackingLoadFactor_PureBending_Converges()
    {
        var section = TestSections.RectWithBottomRebar();
        var solver = new CrackingSolver(section, CalcType.N);

        var res = solver.CrackingLoadFactor(N0: 0.0, Mx0: 1.0, My0: 0.0);

        Assert.True(res.Converged);
        Assert.True(res.Lambda > 0.0);
        Assert.True(res.Mx > 0.0);
        Assert.Equal(0.0, res.My, 6);
    }

    [Fact]
    public void CrackingLoadFactor_WithAxialCompression_ShiftsCrackingMoment()
    {
        var section = TestSections.RectWithBottomRebar();
        var solver = new CrackingSolver(section, CalcType.N);

        var zeroN = solver.CrackingLoadFactor(N0: 0.0, Mx0: 1.0, My0: 0.0);
        var withCompression = solver.CrackingLoadFactor(N0: -300.0, Mx0: 1.0, My0: 0.0);

        Assert.True(zeroN.Converged);
        Assert.True(withCompression.Converged);
        Assert.NotEqual(zeroN.Mx, withCompression.Mx, 3);
    }

    [Fact]
    public void CrackingLoadFactor_ScalesAllThreeComponentsByLambda()
    {
        var section = TestSections.RectWithBottomRebar();
        var solver = new CrackingSolver(section, CalcType.N);

        var res = solver.CrackingLoadFactor(N0: -50.0, Mx0: 1.0, My0: 0.0);

        Assert.True(res.Converged);
        Assert.Equal(res.Lambda * -50.0, res.N, 6);
        Assert.Equal(res.Lambda * 1.0, res.Mx, 6);
    }
}
