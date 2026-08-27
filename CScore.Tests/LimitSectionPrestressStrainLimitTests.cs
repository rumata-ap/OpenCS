using CScore;
using Xunit;

namespace CScore.Tests;

/// <summary>
/// Предел деформаций арматуры в предельном состоянии.
///
/// 1. Проверять нужно ПОЛНУЮ деформацию ε = ε_плоскости + ε_p: именно её видит диаграмма
///    материала (<c>Diagramm.cs</c>, σ по <c>Eps + Eps_p</c>). Проверка по одной плоскости
///    завышает запас ровно на величину преднапряжения.
/// 2. Предельная растяжимость РАЗНАЯ у арматуры с физическим (ReSteelF, ε_su = 0.025) и с
///    условным (ReSteelU, ε_su = 0.015) пределом текучести. Поэтому ни ε_su, ни ε_s,max
///    нельзя брать как независимые максимумы по всем стержням — сравнивать надо стержень
///    сам с собой, а в отчёт отдавать пару от самого нагруженного (по ε/ε_su).
///
/// Тестовое сечение <see cref="TestSections.RectWithEccentricPrestressedRebar"/> содержит оба
/// типа: ненапрягаемую A500 (0.025) и напрягаемую A1000 (0.015, σ_sp = 900 → ε_p = 0.0045).
/// </summary>
public class LimitSectionPrestressStrainLimitTests
{
    const double NewtonTol = 0.1;
    const int NewtonMaxIter = 25;
    const double EpsPExpected = 900.0 * 1000.0 / 200_000_000.0;   // 0.0045

    [Fact]
    public void Adapter_ExposesPrestrainOfRebarPoints()
    {
        var section = TestSections.RectWithEccentricPrestressedRebar();
        var adapter = new CrossSectionLimitAdapter(section, CalcType.C);

        var points = adapter.RebarPoints.ToList();
        var prestressed = points.Where(p => p.EpsP != 0).ToList();

        Assert.Equal(2, prestressed.Count);
        Assert.All(prestressed, p => Assert.Equal(EpsPExpected, p.EpsP, 12));
        Assert.All(prestressed, p => Assert.Equal(0.015, p.EpsSu, 12));   // условный предел
        Assert.All(points.Where(p => p.EpsP == 0), p => Assert.Equal(0.025, p.EpsSu, 12));
    }

    /// <summary>Сечение, у которого предел наступает по напрягаемой арматуре, а не по бетону.</summary>
    static CrossSection SectionWithRebarGoverning(double epsSu)
        => TestSections.RectWithEccentricPrestressedRebar(strandEpsSu: epsSu);

    /// <summary>Максимум полной деформации по волокнам одной группы и её ε_p.</summary>
    static (double Full, double EpsP) WorstIn(CrossSection section, string tag, Kurvature sp)
    {
        double worst = double.MinValue, epsP = 0;
        foreach (var f in section.Areas.First(a => a.Tag == tag).Fibers
                     .Where(f => f.TypeFiber == FiberType.point))
        {
            double full = sp.e0 + sp.ky * f.Y + sp.kz * f.X + f.Eps_p;
            if (full > worst) { worst = full; epsP = f.Eps_p; }
        }
        return (worst, epsP);
    }

    [Theory]
    [InlineData("fast")]
    [InlineData("bisection")]
    public void MomentFactor_PrestressedRebarStaysWithinEpsSu(string solver)
    {
        const double EpsSu = 0.008;
        var section = SectionWithRebarGoverning(EpsSu);

        ILimitForceSolver s = solver == "fast"
            ? new LimitForceSolverFast(section, CalcType.C,
                newtonTol: NewtonTol, newtonMaxIter: NewtonMaxIter, ten: false)
            : LimitForceSolver.ForCrossSection(section, CalcType.C,
                solverTol: NewtonTol, solverMaxIter: NewtonMaxIter, ten: false);

        var res = s.MomentFactor(0.0, -100.0, 0.0);

        Assert.True(res.Converged);
        var sp = res.StrainPlane!.Value;
        var (full, epsP) = WorstIn(section, "strands", sp);

        Assert.Equal(EpsPExpected, epsP, 12);
        Assert.True(full <= EpsSu + 1e-4,
            $"Полная деформация напрягаемой арматуры {full:G6} превысила ε_su = {EpsSu} " +
            $"(ε_p = {epsP:G6}): предел проверен без учёта преднапряжения.");
    }

    /// <summary>
    /// Отчётная пара (ε_s,max; ε_su) должна относиться к ОДНОМУ — самому нагруженному —
    /// стержню. Здесь это напрягаемая A1000 с ε_su = 0.015, а не A500 с 0.025.
    /// </summary>
    [Fact]
    public void MomentFactor_ReportsMatchingPairFromGoverningBar()
    {
        var section = SectionWithRebarGoverning(0.008);

        var res = new LimitForceSolverFast(section, CalcType.C,
            newtonTol: NewtonTol, newtonMaxIter: NewtonMaxIter, ten: false)
            .MomentFactor(0.0, -100.0, 0.0);

        var sp = res.StrainPlane!.Value;
        var (full, _) = WorstIn(section, "strands", sp);

        Assert.NotNull(res.EpsRebarMax);
        Assert.NotNull(res.EpsSu);
        Assert.Equal(0.008, res.EpsSu!.Value, 6);          // предел критического стержня
        Assert.Equal(full, res.EpsRebarMax!.Value, 6);     // его же полная деформация
    }

    /// <summary>
    /// Штатное сечение (A1000 с ε_su = 0.015): отчёт обязан отдать 0.015, а не 0.025 от A500 —
    /// иначе запас напрягаемой арматуры показан почти вдвое больше настоящего.
    /// </summary>
    [Fact]
    public void MomentFactor_DoesNotBorrowEpsSuFromOtherRebarType()
    {
        var section = TestSections.RectWithEccentricPrestressedRebar();

        var res = new LimitForceSolverFast(section, CalcType.C,
            newtonTol: NewtonTol, newtonMaxIter: NewtonMaxIter, ten: false)
            .MomentFactor(0.0, -150.0, 0.0);

        var sp = res.StrainPlane!.Value;
        var (fullStrand, _) = WorstIn(section, "strands", sp);
        var (fullRebar, _) = WorstIn(section, "rebar", sp);

        // Критичен тот, у кого больше использование ε/ε_su.
        bool strandGoverns = fullStrand / 0.015 >= fullRebar / 0.025;
        Assert.True(strandGoverns, "Ожидалось, что критична напрягаемая группа.");

        Assert.Equal(0.015, res.EpsSu!.Value, 6);
        Assert.Equal(fullStrand, res.EpsRebarMax!.Value, 6);
    }

    /// <summary>Контроль: без преднапряжения ε_s,max — это чистая деформация плоскости.</summary>
    [Fact]
    public void MomentFactor_WithoutPrestress_RebarStrainUnchanged()
    {
        var section = TestSections.RectWithEccentricPrestressedRebar(sigSp: 0.0);

        var res = new LimitForceSolverFast(section, CalcType.C,
            newtonTol: NewtonTol, newtonMaxIter: NewtonMaxIter, ten: false)
            .MomentFactor(-500.0, -100.0, 25.0);

        Assert.True(res.Converged);
        var sp = res.StrainPlane!.Value;
        var (full, epsP) = WorstIn(section, "strands", sp);

        Assert.Equal(0.0, epsP);
        Assert.Equal(full, res.EpsRebarMax!.Value, 6);
    }
}
