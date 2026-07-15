using System;
using System.Linq;
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

    // п. 8.2.32 (формула деформационной модели, по отношению деформаций) vs п. 8.2.18
    // (формула 8.138, по отношению напряжений) — при упругой арматуре (σ = E·ε) оба
    // используют одно и то же отношение r = σs,crc/σs = εs,crc/εs, но по разным формулам:
    // ψs,strain = 1/(1+0.8r) ≥ ψs,stress = 1-0.8r для любого r∈[0,1] (равенство только
    // при r=0). Значит при том же нагружении Strain8232 должен давать ψs (и, как
    // следствие, acrc) не меньше, чем Stress8138 — с строгим неравенством вблизи
    // трещинообразования, где r заметно больше нуля.
    [Fact]
    public void Compute_Strain8232Method_GivesLargerPsiSThanStress8138NearCracking()
    {
        var section = TestSections.RectWithBottomRebar();
        var mcrc = new CrackingSolver(section, CalcType.N).CrackingMoment(0, -1, 0).Mx;

        var solverStress = new CrackWidthSolver(section, psiSMethod: PsiSMethod.Stress8138);
        var solverStrain = new CrackWidthSolver(section, psiSMethod: PsiSMethod.Strain8232);

        var resStress = solverStress.Compute(N: 0.0, mxLong: mcrc * 2.0, mxTotal: mcrc * 2.0);
        var resStrain = solverStrain.Compute(N: 0.0, mxLong: mcrc * 2.0, mxTotal: mcrc * 2.0);

        Assert.True(resStress.Cracked);
        Assert.True(resStrain.Cracked);
        Assert.True(resStrain.PsiS > resStress.PsiS);
        Assert.True(resStrain.AcrcLong > resStress.AcrcLong);
    }

    // Явно переданный Stress8138 (== значение по умолчанию) не должен ничего менять —
    // регрессионная защита от случайной смены поведения по умолчанию.
    [Fact]
    public void Compute_ExplicitStress8138_MatchesDefault()
    {
        var section = TestSections.RectWithBottomRebar();
        var mcrc = new CrackingSolver(section, CalcType.N).CrackingMoment(0, -1, 0).Mx;

        var solverDefault = new CrackWidthSolver(section);
        var solverExplicit = new CrackWidthSolver(section, psiSMethod: PsiSMethod.Stress8138);

        var resDefault = solverDefault.Compute(N: 0.0, mxLong: mcrc * 2.5, mxTotal: mcrc * 2.5);
        var resExplicit = solverExplicit.Compute(N: 0.0, mxLong: mcrc * 2.5, mxTotal: mcrc * 2.5);

        Assert.True(resDefault.Cracked);
        Assert.Equal(resDefault.PsiS, resExplicit.PsiS, 9);
        Assert.Equal(resDefault.AcrcLong, resExplicit.AcrcLong, 9);
    }

    // Формула 8.2.32 клипуется сверху в 1.0 (не может "усиливать" сечение относительно
    // случая без трещин) — проверяем на умеренной перегрузке, где ψs,strain ещё не должен
    // прижаться к 1 (иначе тест ничего не проверял бы).
    [Fact]
    public void Compute_Strain8232Method_PsiSWithinUnitRange()
    {
        var section = TestSections.RectWithBottomRebar();
        var mcrc = new CrackingSolver(section, CalcType.N).CrackingMoment(0, -1, 0).Mx;

        var solver = new CrackWidthSolver(section, psiSMethod: PsiSMethod.Strain8232);
        var res = solver.Compute(N: 0.0, mxLong: mcrc * 2.0, mxTotal: mcrc * 2.0);

        Assert.True(res.Cracked);
        Assert.True(res.PsiS > 0.0);
        Assert.True(res.PsiS <= 1.0);
    }

    // Инженерное уточнение сверх буквы нормы (актуально при косом изгибе, где стержни в
    // растянутой зоне напряжены существенно неравномерно): As_tens/ds_eq считаются не
    // "в лоб" (полная площадь любого стержня с eps>0), а с весом σi/σ_max — вклад слабо
    // растянутого стержня (у нейтральной оси) в As_tens и ls уменьшается пропорционально
    // тому, насколько он менее напряжён, чем самый растянутый стержень. Слой 2 (y0+0.20,
    // ближе к нейтральной оси) в растянутой зоне, но заметно слабее слоя 1 (y0+0.04) —
    // взвешенная As_tens должна оказаться строго между "только слой 1" и "оба слоя в лоб".
    [Fact]
    public void Compute_WeaklyStressedSecondLayer_AsTensWeightedBetweenLayer1AndNaiveSum()
    {
        const double diam = 0.016;
        var section = TestSections.RectWithTwoBottomRebarLayers(diam: diam);
        var mcrc = new CrackingSolver(section, CalcType.N).CrackingMoment(0, -1, 0).Mx;

        var solver = new CrackWidthSolver(section);
        var res = solver.Compute(N: 0.0, mxLong: mcrc * 2.5, mxTotal: mcrc * 2.5);

        Assert.True(res.Cracked);

        double areaOneBar = Math.PI * diam * diam / 4.0;
        double layer1Area = 2.0 * areaOneBar;
        double naiveTotalArea = 4.0 * areaOneBar;

        Assert.True(res.AsTens > layer1Area);
        Assert.True(res.AsTens < naiveTotalArea);

        // Диаметр одинаковый у всех стержней — эквивалентный диаметр не должен зависеть
        // от веса (взвешенное среднее константы равно самой константе).
        Assert.Equal(diam, res.DsEq, 6);
    }

    // Внормативное расширение (см. AcrcByRebar): ширина раскрытия трещины по КАЖДОМУ
    // растянутому стержню отдельно (общий ls, собственные σs/εs/ψs у каждого). Слой 1
    // (y0+0.04, у самой растянутой грани) напряжён сильнее слоя 2 (y0+0.20, ближе к
    // нейтральной оси) — его acrc должен быть больше.
    [Fact]
    public void Compute_AcrcByRebar_MoreStressedBarHasLargerCrackWidth()
    {
        var section = TestSections.RectWithTwoBottomRebarLayers();
        var mcrc = new CrackingSolver(section, CalcType.N).CrackingMoment(0, -1, 0).Mx;

        var solver = new CrackWidthSolver(section, psiSMethod: PsiSMethod.Strain8232);
        var res = solver.Compute(N: 0.0, mxLong: mcrc * 2.5, mxTotal: mcrc * 2.5);

        Assert.True(res.Cracked);
        Assert.Equal(4, res.AcrcByRebar.Count);

        const double y0 = -0.25;
        var layer1 = res.AcrcByRebar.Where(e => Math.Abs(e.Y - (y0 + 0.04)) < 1e-6).ToList();
        var layer2 = res.AcrcByRebar.Where(e => Math.Abs(e.Y - (y0 + 0.20)) < 1e-6).ToList();

        Assert.Equal(2, layer1.Count);
        Assert.Equal(2, layer2.Count);
        Assert.True(layer1.All(e => e.SigmaKPa > 0.0));
        Assert.True(layer2.All(e => e.SigmaKPa > 0.0));
        Assert.True(layer1[0].SigmaKPa > layer2[0].SigmaKPa);
        Assert.True(layer1[0].AcrcLongMm > layer2[0].AcrcLongMm);
        Assert.True(res.AcrcByRebar.All(e => e.PsiS > 0.0 && e.PsiS <= 1.0));
    }

    [Fact]
    public void Compute_BelowCrackingMoment_AcrcByRebarEmpty()
    {
        var section = TestSections.RectWithBottomRebar();
        var solver = new CrackWidthSolver(section);
        var res = solver.Compute(N: 0.0, mxLong: -0.5, mxTotal: -0.5);

        Assert.False(res.Cracked);
        Assert.Empty(res.AcrcByRebar);
    }

    // Как и на уровне сечения (Compute_LongEqualsTotal_ShortEqualsLong), при mxTotal==mxLong
    // (и совпадающих calcService/calcServiceLong по умолчанию) кратковременная составляющая
    // по каждому стержню должна точно совпасть с длительной: acrc2_j==acrc3_j (одни и те же
    // σ/ε на одной и той же диаграмме, разница только в φ1 в SingleAcrc), поэтому
    // acrcShort_j = acrc1_j+acrc2_j-acrc3_j = acrc1_j = acrcLong_j.
    [Fact]
    public void Compute_AcrcByRebar_LongEqualsTotal_ShortEqualsLong()
    {
        var section = TestSections.RectWithBottomRebar();
        var mcrc = new CrackingSolver(section, CalcType.N).CrackingMoment(0, -1, 0).Mx;

        var solver = new CrackWidthSolver(section);
        var res = solver.Compute(N: 0.0, mxLong: mcrc * 3.0, mxTotal: mcrc * 3.0);

        Assert.True(res.Cracked);
        Assert.NotEmpty(res.AcrcByRebar);
        Assert.All(res.AcrcByRebar, e => Assert.Equal(e.AcrcLongMm, e.AcrcShortMm, 6));
        Assert.All(res.AcrcByRebar, e => Assert.Equal(e.PsiS, e.PsiS2, 9));
    }

    // mxTotal > mxLong — кратковременная составляющая должна быть положительной и иметь
    // собственный, отличный от длительного, ψs (своя пара σ/σcrc на кратковременной диаграмме).
    [Fact]
    public void Compute_AcrcByRebar_TotalGreaterThanLong_ShortAcrcPositiveWithOwnPsiS()
    {
        var section = TestSections.RectWithBottomRebar();
        var mcrc = new CrackingSolver(section, CalcType.N).CrackingMoment(0, -1, 0).Mx;

        var solver = new CrackWidthSolver(section);
        var res = solver.Compute(N: 0.0, mxLong: mcrc * 2.5, mxTotal: mcrc * 3.0);

        Assert.True(res.Cracked);
        Assert.NotEmpty(res.AcrcByRebar);
        Assert.All(res.AcrcByRebar, e =>
        {
            Assert.True(e.AcrcShortMm > 0.0);
            Assert.True(e.PsiS2 > 0.0 && e.PsiS2 <= 1.0);
        });
    }
}
