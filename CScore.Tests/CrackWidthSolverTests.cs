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
        var mcrc = new CrackingSolver(section, CalcType.N).CrackingMoment(0, -1, 0).Mx;

        // Множители выше Mcrc, но ниже предельного момента сечения (на характеристиках N
        // Mult оказывается между 3·Mcrc и 3.5·Mcrc: при 3.5·Mcrc арматура уже потекла и
        // равновесия не существует — это не дефект решателя, а исчерпание несущей способности).
        var solver = new CrackWidthSolver(section);
        var low = solver.Compute(N: 0.0, mxLong: mcrc * 2.0, mxTotal: mcrc * 2.0);
        var high = solver.Compute(N: 0.0, mxLong: mcrc * 3.0, mxTotal: mcrc * 3.0);

        Assert.True(low.Cracked);
        Assert.True(high.Cracked);
        Assert.True(high.AcrcLong > low.AcrcLong);
    }

    [Fact]
    public void Compute_LongEqualsTotal_ShortEqualsLong()
    {
        var section = TestSections.RectWithBottomRebar();
        var mcrc = new CrackingSolver(section, CalcType.N).CrackingMoment(0, -1, 0).Mx;

        var solver = new CrackWidthSolver(section);
        var res = solver.Compute(N: 0.0, mxLong: mcrc * 3.0, mxTotal: mcrc * 3.0);

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
        var mcrc = new CrackingSolver(section, CalcType.N).CrackingMoment(0, -1, 0).Mx;

        var solver = new CrackWidthSolver(section);
        var res = solver.Compute(N: 0.0, mxLong: mcrc * 3.0, mxTotal: mcrc * 3.0);

        Assert.True(res.Cracked);
        Assert.True(res.AcrcLong > 0.0);
    }

    [Fact]
    public void Compute_AboveCrackingMoment_ExposesPlaneAndCrackingMomentFields()
    {
        var section = TestSections.RectWithBottomRebar();
        var mcrc = new CrackingSolver(section, CalcType.N).CrackingMoment(0, -1, 0).Mx;

        var solver = new CrackWidthSolver(section);
        var res = solver.Compute(N: 0.0, mxLong: mcrc * 3.0, mxTotal: mcrc * 3.0);

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
        var mcrc = new CrackingSolver(section, CalcType.N).CrackingMoment(0, -1, 0).Mx;

        // mxLong < mxTotal, чтобы planeTotal решался независимо от planeLong.
        var solverDefault = new CrackWidthSolver(section);
        var solverExplicitN = new CrackWidthSolver(section, calcServiceLong: CalcType.N);

        var resDefault = solverDefault.Compute(N: 0.0, mxLong: mcrc * 2.5, mxTotal: mcrc * 3.0);
        var resExplicitN = solverExplicitN.Compute(N: 0.0, mxLong: mcrc * 2.5, mxTotal: mcrc * 3.0);

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
        var mcrc = new CrackingSolver(section, CalcType.N).CrackingMoment(0, -1, 0).Mx;

        // mxLong < mxTotal, чтобы planeTotal (и, следовательно, Acrc2) решался независимо
        // от planeLong и не зависел от calcServiceLong.
        var solverN  = new CrackWidthSolver(section);
        var solverNL = new CrackWidthSolver(section, calcServiceLong: CalcType.NL);

        var resN  = solverN.Compute(N: 0.0, mxLong: mcrc * 2.5, mxTotal: mcrc * 3.0);
        var resNL = solverNL.Compute(N: 0.0, mxLong: mcrc * 2.5, mxTotal: mcrc * 3.0);

        Assert.True(resN.Cracked);
        Assert.True(resNL.Cracked);

        // Кратковременная часть не должна зависеть от переключателя длительной части.
        Assert.Equal(resN.Acrc2, resNL.Acrc2, 6);

        // Длительная часть обязана измениться — у B25 N и NL заметно различаются
        // (E: 30 000 000 vs 17 857 142.86 кПа, см. TestMaterials.Concrete).
        Assert.True(Math.Abs(resNL.AcrcLong - resN.AcrcLong) > 1e-6);
        Assert.True(Math.Abs(resNL.Acrc1 - resN.Acrc1) > 1e-6);
    }

    [Fact]
    public void Compute_ExplicitPhi3_ScalesAcrcProportionally()
    {
        var section = TestSections.RectWithBottomRebar();
        var mcrc = new CrackingSolver(section, CalcType.N).CrackingMoment(0, -1, 0).Mx;
        var solver = new CrackWidthSolver(section);

        var res10 = solver.Compute(N: 0.0, mxLong: mcrc * 2.5, mxTotal: mcrc * 2.5, phi3: 1.0);
        var res12 = solver.Compute(N: 0.0, mxLong: mcrc * 2.5, mxTotal: mcrc * 2.5, phi3: 1.2);

        Assert.True(res10.Cracked);
        Assert.True(res12.Cracked);
        Assert.Equal(res10.AcrcLong * 1.2, res12.AcrcLong, 6);
    }

    // п. 8.2.15: φ3 = 1.2 для растянутых элементов (N > 0, знак "+" = растяжение),
    // 1.0 для изгибаемых/внецентренно сжатых. При phi3 = null Compute должен выбирать
    // его автоматически по знаку N (тот же признак, что и в FemCheckRunner для плит).
    [Fact]
    public void Compute_TensileN_AutoSelectsPhi3OnePointTwo()
    {
        var section = TestSections.RectWithBottomRebar();
        var mcrc = new CrackingSolver(section, CalcType.N).CrackingMoment(0, -1, 0).Mx;
        var solver = new CrackWidthSolver(section);

        const double n = 1.0; // небольшое растяжение, знак "+"
        var resAuto = solver.Compute(N: n, mxLong: mcrc * 2.5, mxTotal: mcrc * 2.5);
        var resExplicit10 = solver.Compute(N: n, mxLong: mcrc * 2.5, mxTotal: mcrc * 2.5, phi3: 1.0);

        Assert.True(resAuto.Cracked);
        Assert.True(resExplicit10.Cracked);
        Assert.Equal(resExplicit10.AcrcLong * 1.2, resAuto.AcrcLong, 6);
    }

    [Fact]
    public void Compute_Phi2Override_ScalesAcrcProportionally()
    {
        var section = TestSections.RectWithBottomRebar();
        var mcrc = new CrackingSolver(section, CalcType.N).CrackingMoment(0, -1, 0).Mx;

        var solverDefault = new CrackWidthSolver(section);
        var solverSmooth  = new CrackWidthSolver(section, phi2: 0.8);

        var resDefault = solverDefault.Compute(N: 0.0, mxLong: mcrc * 2.5, mxTotal: mcrc * 2.5);
        var resSmooth  = solverSmooth.Compute(N: 0.0, mxLong: mcrc * 2.5, mxTotal: mcrc * 2.5);

        Assert.True(resDefault.Cracked);
        Assert.True(resSmooth.Cracked);
        Assert.Equal(resDefault.AcrcLong * (0.8 / 0.5), resSmooth.AcrcLong, 6);
    }

    // Инцидент 2026-07-15 (найден пользователем): SigmaSCrc читался прямо с ДОтрещинной
    // плоскости crcRes.StrainPlane (ten=true — CrackingSolver ищет по ней сам Mcrc), где
    // бетон ещё несёт растяжение и разгружает арматуру. По п.8.2.18 σs,crc должно быть
    // напряжением в арматуре В СЕЧЕНИИ С ТРЕЩИНОЙ, т.е. на пересчитанной (ten=false)
    // плоскости при том же M=Mcrc — там бетон резко перестаёт помогать, и напряжение в
    // арматуре ощутимо ВЫШЕ, чем на дотрещинной плоскости при том же моменте.
    [Fact]
    public void Compute_SigmaSCrc_HigherThanOnPreCrackPlane()
    {
        var section = TestSections.RectWithBottomRebar();
        var crcSolver = new CrackingSolver(section, CalcType.N);
        var crcRes = crcSolver.CrackingMoment(0, -1, 0);
        Assert.True(crcRes.StrainPlane.HasValue);

        var solver = new CrackWidthSolver(section);
        var res = solver.Compute(N: 0.0, mxLong: crcRes.Mx * 2.5, mxTotal: crcRes.Mx * 2.5);
        Assert.True(res.Cracked);

        // "Наивное" (ошибочное) значение — напряжение в арматуре на ДОтрещинной плоскости,
        // которое раньше ошибочно использовалось как σs,crc.
        var plane = crcRes.StrainPlane!.Value;
        double naiveSigmaCrcKPa = 0.0;
        foreach (var area in section.Areas)
        {
            if (area.Material?.Type is not (MatType.ReSteelF or MatType.ReSteelU)) continue;
            var dgr = area.Material.GetDiagramms(area.DiagrammType, 0.85)![CalcType.N];
            foreach (var f in area.Fibers)
            {
                if (f.TypeFiber != FiberType.point) continue;
                double eps = plane.e0 + plane.ky * f.Y + plane.kz * f.X;
                if (eps <= 0.0) continue;
                double sig = dgr.Sig(eps, out _);
                if (sig > naiveSigmaCrcKPa) naiveSigmaCrcKPa = sig;
            }
        }

        Assert.True(naiveSigmaCrcKPa > 0.0);
        Assert.True(res.SigmaSCrc > naiveSigmaCrcKPa * 1.5);
    }
}
