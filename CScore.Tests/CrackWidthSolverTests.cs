using System;
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

    // Бетонная область без построенной сетки волокон (только Hull, контурный интеграл по
    // теореме Грина — см. CrossSection.Integral). SolveDamped/EvalWithTangent суммировал
    // только area.Fibers и полностью игнорировал такой бетон, из-за чего пост-трещинный
    // Ньютон "видел" только 2 стержня арматуры под всей нагрузкой N и расходился в
    // абсурдные кривизны вместо схождения — Compute тихо возвращал Cracked=false.
    [Fact]
    public void Compute_MeshlessConcreteArea_ConvergesAboveCrackingMoment()
    {
        var section = TestSections.RectWithBottomRebarNoMesh();
        var mcrc = new CrackingSolver(section, CalcType.CL).CrackingMoment(0, -1, 0).Mx;

        var solver = new CrackWidthSolver(section);
        var res = solver.Compute(N: 0.0, mxLong: mcrc * 4.0, mxTotal: mcrc * 4.0);

        Assert.True(res.Cracked);
        Assert.True(res.AcrcLong > 0.0);
    }

    [Fact]
    public void Compute_AboveCrackingMoment_ExposesPlaneAndCrackingMomentFields()
    {
        var section = TestSections.RectWithBottomRebar();
        var mcrc = new CrackingSolver(section, CalcType.CL).CrackingMoment(0, -1, 0).Mx;

        var solver = new CrackWidthSolver(section);
        var res = solver.Compute(N: 0.0, mxLong: mcrc * 4.0, mxTotal: mcrc * 4.0);

        Assert.True(res.Cracked);
        Assert.True(res.CrcConverged);
        Assert.True(res.MxCrc < 0);                        // тот же знак/направление, что и mcrc
        Assert.Equal(res.Mcrc, Math.Abs(res.MxCrc), 3);     // Mcrc — модуль вектора (MxCrc, MyCrc)
        Assert.Equal(0.0, res.MyCrc, 6);                    // одноосное направление — My=0
        Assert.True(res.EpsTensionLimit > 0.0);
        Assert.True(res.EpsMaxTension > 0.0);
        Assert.True(res.H0 > 0.0);
        Assert.True(res.PlaneLong.HasValue);
    }

    [Fact]
    public void Compute_CalcServiceLongNull_MatchesExplicitN()
    {
        var section = TestSections.RectWithBottomRebar();
        var mcrc = new CrackingSolver(section, CalcType.CL).CrackingMoment(0, -1, 0).Mx;

        // mxLong < mxTotal, чтобы planeTotal решался независимо от planeLong.
        var solverDefault = new CrackWidthSolver(section);
        var solverExplicitN = new CrackWidthSolver(section, calcServiceLong: CalcType.N);

        var resDefault = solverDefault.Compute(N: 0.0, mxLong: mcrc * 2.5, mxTotal: mcrc * 4.0);
        var resExplicitN = solverExplicitN.Compute(N: 0.0, mxLong: mcrc * 2.5, mxTotal: mcrc * 4.0);

        Assert.True(resDefault.Cracked);
        Assert.Equal(resDefault.AcrcLong, resExplicitN.AcrcLong, 6);
        Assert.Equal(resDefault.AcrcShort, resExplicitN.AcrcShort, 6);
        Assert.Equal(resDefault.Acrc1, resExplicitN.Acrc1, 6);
        Assert.Equal(resDefault.Acrc2, resExplicitN.Acrc2, 6);
        Assert.Equal(resDefault.Acrc3, resExplicitN.Acrc3, 6);
    }

    [Fact]
    public void Compute_CalcServiceLongNL_ChangesOnlyLongPart()
    {
        var section = TestSections.RectWithBottomRebar();
        var mcrc = new CrackingSolver(section, CalcType.CL).CrackingMoment(0, -1, 0).Mx;

        // mxLong < mxTotal, чтобы planeTotal (и, следовательно, Acrc2) решался независимо
        // от planeLong и не зависел от calcServiceLong.
        var solverN  = new CrackWidthSolver(section);
        var solverNL = new CrackWidthSolver(section, calcServiceLong: CalcType.NL);

        var resN  = solverN.Compute(N: 0.0, mxLong: mcrc * 2.5, mxTotal: mcrc * 4.0);
        var resNL = solverNL.Compute(N: 0.0, mxLong: mcrc * 2.5, mxTotal: mcrc * 4.0);

        Assert.True(resN.Cracked);
        Assert.True(resNL.Cracked);

        // Кратковременная часть не должна зависеть от переключателя длительной части.
        Assert.Equal(resN.Acrc2, resNL.Acrc2, 6);

        // Длительная часть обязана измениться — у B25 N и NL заметно различаются
        // (E: 30 000 000 vs 17 857 142.86 кПа, см. TestMaterials.Concrete).
        Assert.True(Math.Abs(resNL.AcrcLong - resN.AcrcLong) > 1e-6);
        Assert.True(Math.Abs(resNL.Acrc1 - resN.Acrc1) > 1e-6);
    }
}
