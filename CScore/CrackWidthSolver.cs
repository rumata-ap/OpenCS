using System;
using System.Collections.Generic;
using System.Linq;

namespace CScore;

/// <summary>
/// Метод вычисления коэффициента ψs (неравномерность деформаций растянутой арматуры
/// между трещинами) в задаче ширины раскрытия трещин.
/// </summary>
public enum PsiSMethod
{
    /// <summary>П. 8.2.18, формула (8.137): ψs = 1 − 0.8·σs,crc/σs (по отношению напряжений). Вариант через Mcrc/M приведён в (8.138).</summary>
    Stress8138,
    /// <summary>
    /// П. 8.2.32, формула деформационной модели: ψs = 1 / (1 + 0.8·εs,crc/εs) (по отношению
    /// деформаций). Норма относит эту формулу к расчёту кривизны/жёсткости (многослойная
    /// деформационная модель), а не к формуле раскрытия трещин 8.2.15 — здесь применяется
    /// как внормативно допускаемая альтернатива к тому же "представительному" стержню,
    /// что и Stress8138 (без встраивания в саму итерацию равновесия).
    /// </summary>
    Strain8232
}

/// <summary>Результат расчёта ширины раскрытия нормальных трещин (СП 63.13330 п.8.2). Все ширины — в мм.</summary>
public sealed class CrackWidthResult
{
    public bool Cracked { get; set; }
    public double AcrcLong { get; set; }
    public double AcrcShort { get; set; }
    public double AcrcUltLong { get; set; }
    public double AcrcUltShort { get; set; }
    public bool PassedLong { get; set; } = true;
    public bool PassedShort { get; set; } = true;
    public double UtilLong { get; set; }
    public double UtilShort { get; set; }
    /// <summary>Момент трещинообразования, кН·м.</summary>
    public double Mcrc { get; set; }
    /// <summary>Напряжение в наиболее растянутом стержне от длительных нагрузок, кПа.</summary>
    public double SigmaS { get; set; }
    /// <summary>То же при образовании трещин (на той же диаграмме, что и SigmaS), кПа.</summary>
    public double SigmaSCrc { get; set; }
    /// <summary>
    /// Напряжение в арматуре при образовании трещин на базисе σs,total (для acrc2) —
    /// в общем случае отличается от <see cref="SigmaSCrc"/>, т.к. считается на другой диаграмме.
    /// </summary>
    public double SigmaSCrc2 { get; set; }
    /// <summary>ψs для acrc1/acrc3 (длительная нагрузка, п. 8.2.18).</summary>
    public double PsiS { get; set; }
    /// <summary>
    /// ψs для acrc2 (полная кратковременная нагрузка) — считается на паре σs,total/σs,crc,short,
    /// поэтому в общем случае отличается от <see cref="PsiS"/>.
    /// </summary>
    public double PsiS2 { get; set; }
    /// <summary>Базовое расстояние между трещинами, м.</summary>
    public double Ls { get; set; }
    /// <summary>Эквивалентный диаметр растянутой арматуры, м.</summary>
    public double DsEq { get; set; }
    /// <summary>
    /// Приведённая площадь растянутой арматуры, м² — с весом σi/σs (см.
    /// <see cref="CrackWidthCore"/>.TensileRebarProps): слабо растянутый стержень (у
    /// нейтральной оси) входит в сумму не полной площадью, а пропорционально своей доле
    /// напряжения от самого растянутого стержня.
    /// </summary>
    public double AsTens { get; set; }
    /// <summary>Площадь растянутого бетона (ограниченная), м².</summary>
    public double Abt { get; set; }
    public double Acrc1 { get; set; }
    public double Acrc2 { get; set; }
    public double Acrc3 { get; set; }
    /// <summary>Компонента момента трещинообразования по X, кН·м (см. <see cref="Mcrc"/> — их модуль).</summary>
    public double MxCrc { get; set; }
    /// <summary>Компонента момента трещинообразования по Y, кН·м.</summary>
    public double MyCrc { get; set; }
    /// <summary>Сошёлся ли поиск момента трещинообразования (бисекция в <see cref="CrackingSolver"/>).</summary>
    public bool CrcConverged { get; set; }
    /// <summary>Максимальная растягивающая деформация бетона в момент трещинообразования.</summary>
    public double EpsMaxTension { get; set; }
    /// <summary>Предельная растягивающая деформация бетона (из диаграммы, п. Г.1 СП63.13330).</summary>
    public double EpsTensionLimit { get; set; }
    /// <summary>Эффективная высота сечения h0, м (используется при расчёте ls/Abt).</summary>
    public double H0 { get; set; }
    /// <summary>Высота сечения h по направлению градиента плоскости трещинообразования, м (п. 8.2.17, обобщение на косой изгиб).</summary>
    public double H { get; set; }
    /// <summary>Высота растянутой зоны xt плоскости трещинообразования (до ограничений 2a ≤ xt ≤ 0,5h), м.</summary>
    public double Xt { get; set; }
    /// <summary>Напряжение в наиболее растянутом стержне от полной нагрузки (кратковременная диаграмма), кПа.</summary>
    public double SigmaSTotal { get; set; }
    /// <summary>Rs,ser наиболее растянутой арматуры, кПа (п. 8.2.16: σs ≤ Rs,ser).</summary>
    public double RsSer { get; set; }
    /// <summary>Кисп по условию σs ≤ Rs,ser (п. 8.2.16) — максимум по длительной и полной нагрузке.</summary>
    public double UtilSteel { get; set; }
    /// <summary>Выполнено ли σs ≤ Rs,ser (п. 8.2.16).</summary>
    public bool PassedSteel { get; set; } = true;
    /// <summary>
    /// Кисп сжатого бетона в сечении с трещиной (εb / εb,ult по п. 8.1.30) — справочно: в
    /// <see cref="PassedLong"/>/<see cref="PassedShort"/> не входит (сжатие проверяется по ПС1).
    /// </summary>
    public double UtilConcrete { get; set; }
    /// <summary>
    /// Трещины образуются от продольной силы (сечение трещиновато при M = 0): точка
    /// трещинообразования взята на луче полной нагрузки (N, M), а не при постоянной N.
    /// </summary>
    public bool OnsetByAxialForce { get; set; }
    /// <summary>Пост-трещинная плоскость деформаций (ten=false) от длительной нагрузки, если Newton сошёлся.</summary>
    public Kurvature? PlaneLong { get; set; }
    /// <summary>Пост-трещинная плоскость деформаций от полной нагрузки (кратковременная диаграмма).</summary>
    public Kurvature? PlaneTotal { get; set; }
    /// <summary>
    /// Ширина раскрытия трещины (длительная И кратковременная) ПО КАЖДОМУ растянутому стержню
    /// отдельно — внормативное уточнение п.8.2.15 (норма даёт одно значение на всё сечение, по
    /// "представительному" стержню). Общий ls (см. <see cref="Ls"/>), но собственные σs/εs/ψs
    /// в точке каждого стержня (для длительной и кратковременной части — отдельно, как и на
    /// уровне сечения — см. <see cref="PsiS"/>/<see cref="PsiS2"/>). Актуально при косом
    /// изгибе, где стержни растянутой зоны нагружены существенно неравномерно. Пусто, если
    /// Cracked=false.
    /// </summary>
    public IReadOnlyList<RebarAcrcEntry> AcrcByRebar { get; set; } = [];
}

/// <summary>Ширина раскрытия трещины (длительная и кратковременная) в точке одного стержня — см. <see cref="CrackWidthResult.AcrcByRebar"/>.</summary>
/// <param name="X">Координата стержня, м.</param>
/// <param name="Y">Координата стержня, м.</param>
/// <param name="Diameter">Диаметр стержня, м.</param>
/// <param name="Eps">Деформация стержня от длительной эксплуатационной нагрузки по плоскости деформаций; начальная деформация преднапряжения не включается.</param>
/// <param name="EpsCrc">Деформация стержня в момент трещинообразования по плоскости деформаций, длительная диаграмма (0, если стержень тогда ещё не был растянут).</param>
/// <param name="SigmaKPa">Напряжение стержня от длительной эксплуатационной нагрузки по скалярной диаграмме без начального напряжения преднапряжения, кПа.</param>
/// <param name="SigmaCrcKPa">Напряжение стержня в момент трещинообразования по плоскости деформаций, длительная диаграмма, кПа.</param>
/// <param name="PsiS">ψs (длительная часть, acrc1/acrc3) в точке этого стержня (по методу солвера — Stress8138 или Strain8232).</param>
/// <param name="AcrcLongMm">Ширина раскрытия трещины, длительная (acrc1), в точке этого стержня, мм.</param>
/// <param name="PsiS2">ψs (кратковременная часть, acrc2) в точке этого стержня — своя пара σ/σcrc на кратковременной диаграмме.</param>
/// <param name="AcrcShortMm">Ширина раскрытия трещины, непродолжительная (acrc1+acrc2-acrc3), в точке этого стержня, мм.</param>
public sealed record RebarAcrcEntry(
    double X, double Y, double Diameter,
    double Eps, double EpsCrc,
    double SigmaKPa, double SigmaCrcKPa,
    double PsiS, double AcrcLongMm,
    double PsiS2, double AcrcShortMm);

/// <summary>
/// Ширина раскрытия нормальных трещин по СП 63.13330 п.8.2.15-8.2.18.
/// Единицы: кН, кН·м, м, кПа на входе/внутри. Требует, чтобы
/// <see cref="CrossSection.ResolveAndBuildDiagramms"/> уже был вызван вызывающей стороной.
/// Точку трещинообразования ищет <see cref="CrackingSolver"/>, саму ширину раскрытия
/// считает <see cref="CrackWidthCore"/>.
/// </summary>
public sealed class CrackWidthSolver
{
    readonly CrossSection _section;
    readonly CalcType _calcCrc;
    readonly double _acrcUltLong;
    readonly double _acrcUltShort;
    readonly CrackWidthCore _core;

    public CrackWidthSolver(
        CrossSection section,
        CalcType calcCrc = CalcType.N,
        CalcType calcService = CalcType.N,
        CalcType? calcServiceLong = null,
        double phi2 = 0.5,
        double acrcUltLong = 0.3,
        double acrcUltShort = 0.4,
        double sp63EtaMin = 0.85,
        double solverTol = 0.5,
        PsiSMethod psiSMethod = PsiSMethod.Stress8138,
        double ekbEtaMin = 0.05)
    {
        _section = section ?? throw new ArgumentNullException(nameof(section));
        _calcCrc = calcCrc;
        _acrcUltLong = acrcUltLong;
        _acrcUltShort = acrcUltShort;
        _core = new CrackWidthCore(section, calcService, calcServiceLong ?? calcService, phi2,
            acrcUltLong, acrcUltShort, psiSMethod, sp63EtaMin, ekbEtaMin, solverTol);
    }

    /// <summary>Вычислить ширину раскрытия нормальных трещин.</summary>
    /// <param name="phi3">
    /// Коэффициент вида нагружения (п. 8.2.15): 1.0 — изгибаемые и внецентренно сжатые,
    /// 1.2 — растянутые элементы. При null определяется автоматически по знаку N
    /// (N > 0 — растяжение, тем же способом, что и в FemCheckRunner для плит).
    /// </param>
    public CrackWidthResult Compute(
        double N,
        double mxLong,
        double? mxTotal = null,
        double myLong = 0.0,
        double? myTotal = null,
        double? phi3 = null)
    {
        double mxTot = mxTotal ?? mxLong;
        double myTot = myTotal ?? myLong;

        double mxDir = Math.Abs(mxLong) > 1e-12 ? mxLong : (Math.Abs(mxTot) > 1e-12 ? mxTot : 1.0);
        double myDir = Math.Abs(mxLong) > 1e-12 || Math.Abs(myLong) > 1e-12 ? myLong : myTot;

        // Трещинообразование ищется только по грани, растянутой внешней нагрузкой: у сильно
        // обжатого сечения противоположная грань может треснуть от одного обжатия, и без
        // фильтра Mcrc относился бы к ней (см. CrackingSolver.LoadedTensionZone).
        var crcSolver = new CrackingSolver(_section, _calcCrc, solverTol: 0.5,
            tensionZone: CrackingSolver.LoadedTensionZone(_section, N, mxDir, myDir, nAtZeroMoment: N));

        var crcRes = crcSolver.CrackingMoment(N, mxDir, myDir);
        double mcrc = Math.Sqrt(crcRes.Mx * crcRes.Mx + crcRes.My * crcRes.My);
        double epsTensionLimit = crcSolver.TensionLimit();

        double mLong = Math.Sqrt(mxLong * mxLong + myLong * myLong);

        CrackWidthResult WithCrackingFields(CrackWidthResult r)
        {
            r.Mcrc = mcrc;
            r.MxCrc = crcRes.Mx;
            r.MyCrc = crcRes.My;
            r.CrcConverged = crcRes.Converged;
            r.EpsMaxTension = crcRes.EpsMaxTension;
            r.EpsTensionLimit = epsTensionLimit;
            return r;
        }

        var zero = new CrackWidthResult { Cracked = false, AcrcUltLong = _acrcUltLong, AcrcUltShort = _acrcUltShort };

        if (mLong <= mcrc || !crcRes.StrainPlane.HasValue) return WithCrackingFields(zero);

        var onset = _core.Onset(N, crcRes.Mx, crcRes.My, crcRes.StrainPlane.Value, byAxialForce: false);
        if (onset == null) return WithCrackingFields(zero);

        return WithCrackingFields(_core.Evaluate(onset, N, mxLong, myLong, mxTot, myTot, phi3));
    }
}
