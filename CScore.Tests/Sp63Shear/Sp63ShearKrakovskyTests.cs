using CScore;
using CScore.Sp63Shear;
using Xunit;

namespace CScore.Tests.Sp63Shear;

/// <summary>
/// Валидационные тесты по примерам раздела V «Прочность наклонных сечений железобетонных
/// элементов» Пособия Краковского к СП 63.13330.2012 (папка
/// "work\expert\norms\Пособие Краковского СП63.13330.2012"). По решению пользователя раздел
/// V.Б (бетонная полоса, формула 8.55) не тестируется — формула менялась в последующих
/// изменениях СП63. Здесь проверяются только раздел V.В (наклонное сечение на поперечную
/// силу, 8.56–8.59) и раздел V.Г (наклонное сечение на момент, 8.63–8.65).
///
/// Числа расчётного контроля взяты из "ручного" контроля в тексте пособия там, где он есть;
/// иначе — из таблиц "Условия и результаты расчетов" / "Параметры решений".
/// </summary>
public sealed class Sp63ShearKrakovskyTests
{
    // ---- V.В.1.1 / V.В.2.1: общая геометрия базовой балки ----
    // b=200 мм; h=400 мм; a=a'=40 мм => h0=360 мм; пролёт 5,5 м; бетон В20, γb1=0,9;
    // растянутая арматура 3⌀25, сжатая 3⌀16 А500; нагрузка 50 кН/м.
    const double B = 0.200;
    const double H0 = 0.360;
    const double Rb20 = 10_350.0;  // B20: Rb=11,5 МПа (базовое) × γb1=0,9, кПа
    const double Rbt20 = 810.0;    // B20: Rbt=0,90 МПа (базовое) × γb1=0,9, кПа — подтверждено
                                    // независимо в диагностике ниже (совпадает с нижним
                                    // ограничением 0,5·Rbt·b·h0=29,16≈29,2 кН из примера V.В.2.6).
    const double Span = 5.5;
    const double HalfSpan = Span / 2.0;
    const double Load = 50.0;      // кН/м
    const double SupportReaction = Load * Span / 2.0; // 137,5 кН

    static InclinedSectionGeometryPair Geometry(double ns = 0.0)
    {
        var side = new InclinedSectionGeometry(
            B: B, H0: H0, Ns: ns, As: 0.0015 /* 3⌀25 растянутая, ориентировочно */,
            Rb: Rb20, Rbt: Rbt20,
            Ab: B * (H0 + 0.04), AsTotal: 0.0, Eb: 27_000_000.0,
            Eb0: 0.002, Ebt0: 0.0001,
            Plane: ShearPlane.Vy, TensionOnPositiveSide: true, Warnings: []);
        return new InclinedSectionGeometryPair(side, side);
    }

    static ShearInclinedInput Input(double qsw, double sw) => new(
        B: B, H0: H0, Rb: Rb20, Rbt: Rbt20, Qsw: qsw, Sw: sw, Ns: 0.0,
        Kind: ElementKind.BendingUnstressed, AnchorageFactor: 1.0,
        // Автошаг стоянок (min(H0/2, длина/20) ≈ 137,5 мм) слишком груб, чтобы точно попасть
        // на критическое сечение ≈ 465 мм из «ручного» контроля пособия — задаём мелкий шаг явно.
        StationStep: 0.001, ProjectionStep: 0.0, MomentZoneLength: 0.0,
        BarCutoffs: [], CheckMoment: false, PhiNOverride: null);

    /// <summary>
    /// Пример V.В.1.1 (базовый): «ручной» контроль пособия даёт C=465 мм, Qb=67,73 кН,
    /// Qsw=46,59 кН (при qsw=133,6 Н/мм), Q=114,25 кН, Qb+Qsw=114,32 кН.
    ///
    /// РАНЕЕ ЗДЕСЬ БЫЛ ОБНАРУЖЕН И ИСПРАВЛЕН БАГ (см. заметку памяти "2026-09-10 Пособие
    /// Краковского — подборка примеров для валидации OpenCS.md" и коммит с исправлением
    /// <see cref="InclinedSectionModel.AppliedShear"/>): до исправления `AppliedShear` брал
    /// `profile.MaxAbsQ(Station, Point0)` — максимум |Q| по концам отрезка. Поскольку перебор
    /// станций сам подбирает Station=C (чтобы Point0=0 — точно на опоре), это всегда давало
    /// полную опорную реакцию независимо от C, вместо убывающего Q=QA−q·C по п. 8.1.33 —
    /// расчёт вырождался, C уезжал на границу диапазона 3h0, и OpenCS выдавал η=1,35 (не
    /// выполнено) вместо корректных η≈0,999 (выполнено).
    /// </summary>
    [Fact]
    public void VB1_1_Base_MatchesManualControl()
    {
        var input = Input(qsw: 133.6, sw: 0.05);
        var profile = new UniformLoadProfile(
            q0: SupportReaction, m0: 0.0, n0: 0.0, distributedLoad: Load,
            supportDistance: HalfSpan, supportAtStart: true, supportAtEnd: false);

        var result = ShearInclinedChecker.Check(input, profile, Geometry(), direction: -1);
        var detail = result.Details.Single(d => d.Formula == "8.56");

        // Пособие даёт C=465мм по непрерывной оптимизации; OpenCS ищет по сетке (шаг стоянок
        // 1мм, шаг проекции H0/100=3,6мм), поэтому совпадение — с точностью до дискретизации
        // сетки (±30мм на C), а не до миллиметра. Коэффициент использования (главный критерий
        // «выполнено/не выполнено») совпадает практически точно: 0,9994 против 0,9994 в пособии.
        Assert.InRange(detail.Variables["C"], 0.465 - 0.03, 0.465 + 0.03);
        Assert.InRange(detail.Applied, 114.25 - 1.0, 114.25 + 1.0);
        Assert.InRange(detail.Allowable, 114.32 - 1.0, 114.32 + 1.0);
        Assert.True(detail.Ratio < 1.0, $"Ожидалось 'Выполнены' (η<1), получено η={detail.Ratio:F4}");
    }

    /// <summary>Пример V.В.2.1: заданы реальные хомуты (3-ветвевые ⌀8 А240, шаг 150 мм),
    /// qsw=170e3×3×0,503e-4/0,15=171,02 Н/мм. Пособие: C=425 мм, Q=116,2 кН, Qult=128,6 кН,
    /// η=0,903 (Выполнены). См. также XML-комментарий к
    /// <see cref="VB1_1_Base_MatchesManualControl"/> про исправленный баг в
    /// <see cref="InclinedSectionModel.AppliedShear"/>.</summary>
    [Fact]
    public void VB2_1_CheckGivenStirrups_MatchesManualControl()
    {
        double asw = 3.0 * 0.503e-4; // 3 ветви ⌀8, номинальная площадь 0,503 см² на стержень
        double sw = 0.15;
        double qsw = 170_000.0 * asw / sw;

        var input = Input(qsw: qsw, sw: sw);
        var profile = new UniformLoadProfile(
            q0: SupportReaction, m0: 0.0, n0: 0.0, distributedLoad: Load,
            supportDistance: HalfSpan, supportAtStart: true, supportAtEnd: false);

        var result = ShearInclinedChecker.Check(input, profile, Geometry(), direction: -1);
        var detail = result.Details.Single(d => d.Formula == "8.56");

        // См. примечание о точности сеточного поиска в VB1_1_Base_MatchesManualControl.
        Assert.InRange(detail.Variables["C"], 0.425 - 0.03, 0.425 + 0.03);
        Assert.InRange(detail.Applied, 116.2 - 1.0, 116.2 + 1.0);
        Assert.InRange(detail.Allowable, 128.6 - 1.0, 128.6 + 1.0);
        Assert.True(detail.Ratio < 1.0, $"Ожидалось 'Выполнены' (η<1), получено η={detail.Ratio:F4}");
    }

    // ---- V.Г.1: наклонное сечение на момент (8.63–8.65) ----
    // b=200; h=400; h0=360 мм; пролёт 6,0 м; бетон В30, γb1=0,9; растянутая арматура 2⌀28
    // А500 (As=12,32 см²); хомуты 2⌀8 А240 шаг 200 мм (Asw=0,000101 м² — см. примечание ниже);
    // нагрузка 29 кН/м. Расчётное сечение на 760 мм от опоры/торца, конец стержней — на 740 мм
    // от того же торца (зона анкеровки l0,an=740 мм).
    //
    // ПРИМЕЧАНИЕ О ВЫЯВЛЕННОЙ ОПЕЧАТКЕ ИСТОЧНИКА: в тексте пособия формула
    // qsw = Rsw·Asw/sw = 170×10³×0,000101/0,2 напечатана с результатом «83,85», хотя
    // арифметически 170000×0,000101/0,2 = 85,85 (не 83,85). Далее по тексту пособия
    // именно 85,85 используется в вычислении Msw и даёт итоговое совпадение с M_ult=60,4,
    // указанным как результат «по программе». Поэтому ниже используется qsw=85,85 кН/м как
    // единственное самосогласованное значение; «83,85» — опечатка самого источника,
    // а не альтернативная величина.
    const double H0G = 0.360;
    const double QswG = 85.85; // кН/м, см. примечание выше

    static ShearInclinedInput MomentInput(double ns, double qsw) => new(
        B: 0.200, H0: H0G, Rb: 15_300.0, Rbt: 1_040.0, Qsw: qsw, Sw: 0.2, Ns: ns,
        Kind: ElementKind.BendingUnstressed, AnchorageFactor: 1.0,
        StationStep: 0.0, ProjectionStep: 0.0, MomentZoneLength: 0.0,
        BarCutoffs: [], CheckMoment: true, PhiNOverride: null);

    static InclinedSectionGeometryPair MomentGeometry(double ns) => new(
        new InclinedSectionGeometry(
            B: 0.200, H0: H0G, Ns: ns, As: 0.001232, Rb: 15_300.0, Rbt: 1_040.0,
            Ab: 0.08, AsTotal: 0.001232, Eb: 32_500_000.0, Eb0: 0.002, Ebt0: 0.0001,
            Plane: ShearPlane.Vy, TensionOnPositiveSide: true, Warnings: []),
        new InclinedSectionGeometry(
            B: 0.200, H0: H0G, Ns: ns, As: 0.001232, Rb: 15_300.0, Rbt: 1_040.0,
            Ab: 0.08, AsTotal: 0.001232, Eb: 32_500_000.0, Eb0: 0.002, Ebt0: 0.0001,
            Plane: ShearPlane.Vy, TensionOnPositiveSide: false, Warnings: []));

    /// <summary>
    /// Пример V.Г.1.1 (базовый). Ns берётся уже «уменьшенным» анкеровкой —
    /// Ns = Rbond·us·l0,an = 2,6e3×0,088×0,74 = 169,312 кН (формула (10.1), см. текст),
    /// AnchorageFactor=1 (усилие уже учитывает анкеровку). Пособие: Ms=54,8; Msw=5,6 (с
    /// учётом исправленного qsw=85,85, см. примечание выше); Mult=60,4; M=57,7 — выполнено.
    /// </summary>
    [Fact]
    public void VG1_1_Base_MatchesManualControl()
    {
        double ns = 2_600.0 * 0.088 * 0.74; // 169,312 кН
        var input = MomentInput(ns, QswG);
        var profile = new ConstantProfile(q: 0.0, m: 57.7, n: 0.0, supportDistance: 0.0);

        var result = ShearInclinedChecker.Check(input, profile, MomentGeometry(ns), direction: -1);
        var detail = result.Details.Single(d => d.Formula == "8.63");

        // Пособие: Ms=54,8; Msw=5,6; Mult=60,4; M=57,7 < Mult — требования выполнены.
        Assert.Equal(57.7, detail.Applied, 2);
        Assert.Equal(60.4, detail.Allowable, 1);
        Assert.True(detail.Ratio < 1.0, $"Ожидалось 'Выполнены' (η<1), получено η={detail.Ratio:F4}");
    }

    /// <summary>
    /// Пример V.Г.1.4: та же геометрия, но l0,an уменьшена до 500 мм.
    /// Пособие: Mult=42,6 кН·м (Не выполнены — M=57,7 > 42,6).
    /// </summary>
    [Fact]
    public void VG1_4_ShorterAnchorage_MatchesManualControl()
    {
        double ns = 2_600.0 * 0.088 * 0.50; // 114,4 кН
        var input = MomentInput(ns, QswG);
        var profile = new ConstantProfile(q: 0.0, m: 57.7, n: 0.0, supportDistance: 0.0);

        var result = ShearInclinedChecker.Check(input, profile, MomentGeometry(ns), direction: -1);
        var detail = result.Details.Single(d => d.Formula == "8.63");

        // Пособие: Mult=42,6 кН·м; M=57,7 > Mult — требования НЕ выполнены.
        Assert.Equal(42.6, detail.Allowable, 1);
        Assert.True(detail.Ratio > 1.0, $"Ожидалось 'Не выполнены' (η>1), получено η={detail.Ratio:F4}");
    }

    /// <summary>
    /// Пример V.Г.1.6: шаг хомутов уменьшен с 200 до 150 мм — qsw возрастает пропорционально
    /// (85,85×200/150=114,47 кН/м). Пособие: Mult=62,2 кН·м (выполнено).
    /// </summary>
    [Fact]
    public void VG1_6_TighterStirrups_MatchesManualControl()
    {
        double ns = 2_600.0 * 0.088 * 0.74;
        double qsw = QswG * 200.0 / 150.0; // 114,467 кН/м
        var input = MomentInput(ns, qsw);
        var profile = new ConstantProfile(q: 0.0, m: 57.7, n: 0.0, supportDistance: 0.0);

        var result = ShearInclinedChecker.Check(input, profile, MomentGeometry(ns), direction: -1);
        var detail = result.Details.Single(d => d.Formula == "8.63");

        // Пособие: Mult=62,2 кН·м; M=57,7 < Mult — требования выполнены. Допуск 0,2 кН·м —
        // источник указывает qsw округлённо, фактический расчёт даёт Mult≈62,275.
        Assert.InRange(detail.Allowable, 62.2 - 0.2, 62.2 + 0.2);
        Assert.True(detail.Ratio < 1.0, $"Ожидалось 'Выполнены' (η<1), получено η={detail.Ratio:F4}");
    }
}
