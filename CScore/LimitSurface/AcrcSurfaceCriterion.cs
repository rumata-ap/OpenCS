namespace CScore.LimitSurface;

/// <summary>
/// ПС2, ширина раскрытия нормальных трещин (пп. 8.2.6–8.2.18 СП 63): тело в пространстве полных усилий
/// (N, Mx, My), внутри которого выполняются (8.118) по продолжительному (8.119) и непродолжительному
/// (8.120) раскрытию и σs ≤ Rs,ser (п. 8.2.16). Длительные усилия — (N, k·Mx, k·My) с долей k на задачу.
/// Точка трещинообразования — по телу образования трещин (<see cref="CrackStrainCriterion"/>), поэтому
/// тело ширины раскрытия охватывает тело ПС2 без щелей.
/// </summary>
public sealed class AcrcSurfaceCriterion
{
    /// <summary>Создаёт критерий.</summary>
    /// <param name="calcCrc">Характеристики трещинообразования (тело ПС2): N или NL.</param>
    /// <param name="longUseNL">Длительная часть — на диаграмме NL (иначе N), как у crack_width.</param>
    /// <param name="longShare">Доля длительной нагрузки k ∈ (0, 1].</param>
    /// <param name="phi2">φ2 (п. 8.2.15).</param>
    /// <param name="acrcUltLong">Предельная ширина продолжительного раскрытия, мм.</param>
    /// <param name="acrcUltShort">Предельная ширина непродолжительного раскрытия, мм.</param>
    /// <param name="psiSMethod">Метод ψs.</param>
    public AcrcSurfaceCriterion(CalcType calcCrc = CalcType.N, bool longUseNL = false, double longShare = 0.7,
        double phi2 = 0.5, double acrcUltLong = 0.3, double acrcUltShort = 0.4,
        PsiSMethod psiSMethod = PsiSMethod.Stress8138, double sp63EtaMin = 0.85, double ekbEtaMin = 0.05)
    {
        if (calcCrc is not (CalcType.N or CalcType.NL))
            throw new ArgumentException(
                "Поверхность ширины раскрытия строится по характеристикам 2-й группы (N, NL).", nameof(calcCrc));
        if (!(longShare > 0.0 && longShare <= 1.0))
            throw new ArgumentOutOfRangeException(nameof(longShare), longShare, "Доля длительной нагрузки k ∈ (0; 1].");
        if (!(acrcUltLong > 0.0) || !(acrcUltShort > 0.0))
            throw new ArgumentOutOfRangeException(nameof(acrcUltLong), "Предельная ширина раскрытия должна быть положительной.");
        CalcCrc = calcCrc;
        LongUseNL = longUseNL;
        LongShare = longShare;
        Phi2 = phi2;
        AcrcUltLong = acrcUltLong;
        AcrcUltShort = acrcUltShort;
        PsiSMethod = psiSMethod;
        Sp63EtaMin = sp63EtaMin;
        EkbEtaMin = ekbEtaMin;
        CrackCriterion = new CrackStrainCriterion(calcCrc);
    }

    /// <summary>Название критерия для сообщений.</summary>
    public string Name => "ПС2, ширина раскрытия трещин, пп. 8.2.6–8.2.18 СП 63";

    /// <summary>Характеристики тела образования трещин.</summary>
    public CalcType CalcCrc { get; }
    /// <summary>Длительная часть — на диаграмме NL.</summary>
    public bool LongUseNL { get; }
    /// <summary>Доля длительной нагрузки k.</summary>
    public double LongShare { get; }
    /// <summary>φ2.</summary>
    public double Phi2 { get; }
    /// <summary>acrc,ult продолжительного раскрытия, мм.</summary>
    public double AcrcUltLong { get; }
    /// <summary>acrc,ult непродолжительного раскрытия, мм.</summary>
    public double AcrcUltShort { get; }
    /// <summary>Метод ψs.</summary>
    public PsiSMethod PsiSMethod { get; }
    /// <summary>Нижняя граница ниспадающей ветви диаграмм СП 63.</summary>
    public double Sp63EtaMin { get; }
    /// <summary>То же для диаграмм ЕКБ.</summary>
    public double EkbEtaMin { get; }

    /// <summary>Критерий тела образования трещин (ПС2, п. 8.2.14).</summary>
    public CrackStrainCriterion CrackCriterion { get; }

    /// <summary>
    /// Ядро ширины раскрытия для сечения (полная нагрузка — диаграмма N, длительная — N или NL). Невязка
    /// Ньютона по умолчанию — 0,01 кН (кН·м), а не 0,5 как у crack_width: при 0,5 разброс σs от затравки
    /// доходит до 1 % и ломает бисекцию границы.
    /// </summary>
    /// <param name="stallIters">Досрочный выход Ньютона при застое (<see cref="CrackWidthCore.StallIters"/>).</param>
    public CrackWidthCore CreateCore(CrossSection section, double solverTol = 0.01, int stallIters = 0) =>
        new(section, CalcType.N, LongUseNL ? CalcType.NL : CalcType.N, Phi2, AcrcUltLong, AcrcUltShort,
            PsiSMethod, Sp63EtaMin, EkbEtaMin, solverTol) { StallIters = stallIters };
}
