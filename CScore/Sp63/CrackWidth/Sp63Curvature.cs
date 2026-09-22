namespace CScore.Sp63.CrackWidth;

/// <summary>Относительная влажность воздуха окружающей среды для таблиц 6.10 и 6.12 СП 63.</summary>
public enum Sp63Humidity
{
    /// <summary>Выше 75 %.</summary>
    Above75,
    /// <summary>40–75 %.</summary>
    From40To75,
    /// <summary>Ниже 40 %.</summary>
    Below40
}

/// <summary>Одна составляющая кривизны (1/r)i по п. 8.2.24.</summary>
public sealed class Sp63CurvatureTerm
{
    /// <summary>Номер составляющей: 1, 2 или 3.</summary>
    public int Index { get; set; }

    /// <summary>Продолжительное действие нагрузки (иначе — непродолжительное).</summary>
    public bool LongTerm { get; set; }

    /// <summary>Изгибающий момент составляющей относительно середины высоты сечения, кН·м.</summary>
    public double M { get; set; }

    /// <summary>Продольная сила составляющей, кН ("+" — растяжение).</summary>
    public double N { get; set; }

    /// <summary>Момент относительно центра тяжести приведённого сечения, кН·м (п. 8.2.25).</summary>
    public double MRed { get; set; }

    /// <summary>Модуль деформации бетона Eb1 (0,85·Eb, Eb,τ или Eb,red), кПа.</summary>
    public double Eb1 { get; set; }

    /// <summary>Коэффициент ψs по (8.138); NaN для участка без трещин.</summary>
    public double PsiS { get; set; } = double.NaN;

    /// <summary>Средняя высота сжатой зоны xm, м; NaN для участка без трещин.</summary>
    public double Xm { get; set; } = double.NaN;

    /// <summary>Расстояние от сжатой грани до центра тяжести приведённого сечения, м.</summary>
    public double Yc { get; set; }

    /// <summary>Момент инерции приведённого сечения Ired, м⁴.</summary>
    public double IRed { get; set; }

    /// <summary>Изгибная жёсткость D, кН·м².</summary>
    public double D { get; set; }

    /// <summary>Жёсткость ограничена жёсткостью без трещин (п. 8.2.27).</summary>
    public bool LimitedByUncracked { get; set; }

    /// <summary>Кривизна составляющей 1/r, 1/м.</summary>
    public double Curvature { get; set; }
}

/// <summary>Полная кривизна сечения по пп. 8.2.23–8.2.30.</summary>
public sealed class Sp63CurvatureResult
{
    /// <summary>Участок с трещинами — формула (8.141), иначе (8.140).</summary>
    public bool Cracked { get; set; }

    /// <summary>Составляющие кривизны.</summary>
    public List<Sp63CurvatureTerm> Terms { get; set; } = [];

    /// <summary>Полная кривизна 1/r, 1/м.</summary>
    public double Total { get; set; }

    /// <summary>Коэффициент ползучести φb,cr (таблица 6.12).</summary>
    public double PhiBCr { get; set; }

    /// <summary>Деформация εb1,red при продолжительном действии нагрузки (таблица 6.10).</summary>
    public double EpsB1RedLong { get; set; }
}

/// <summary>Исходные данные формульного расчёта кривизны прямоугольного сечения.</summary>
/// <param name="B">Ширина сечения, м.</param>
/// <param name="H">Высота сечения, м.</param>
/// <param name="H0">Рабочая высота, м.</param>
/// <param name="APrime">Расстояние от сжатой грани до центра сжатой арматуры, м.</param>
/// <param name="As">Площадь растянутой арматуры, м².</param>
/// <param name="AsPrime">Площадь сжатой арматуры, м².</param>
/// <param name="Eb">Начальный модуль упругости бетона, кПа.</param>
/// <param name="RbSer">Rb,ser, кПа.</param>
/// <param name="Es">Модуль упругости арматуры, кПа.</param>
/// <param name="ConcreteClass">Класс бетона B (число).</param>
/// <param name="Humidity">Влажность среды.</param>
public sealed record Sp63CurvatureInput(
    double B, double H, double H0, double APrime, double As, double AsPrime,
    double Eb, double RbSer, double Es, double ConcreteClass, Sp63Humidity Humidity);

/// <summary>
/// Кривизна железобетонного элемента прямоугольного сечения без предварительного напряжения
/// по формулам пп. 8.2.23–8.2.30 СП 63.13330.2018 (не по деформационной модели п. 8.2.32).
/// </summary>
public static class Sp63Curvature
{
    // Таблица 6.12: φb,cr для B10, B15, …, B55, B60–B100 (строки — влажность).
    static readonly double[] PhiClasses = [10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60];
    static readonly double[] PhiAbove75 = [2.8, 2.4, 2.0, 1.8, 1.6, 1.5, 1.4, 1.3, 1.2, 1.1, 1.0];
    static readonly double[] Phi40To75 = [3.9, 3.4, 2.8, 2.5, 2.3, 2.1, 1.9, 1.8, 1.6, 1.5, 1.4];
    static readonly double[] PhiBelow40 = [5.6, 4.8, 4.0, 3.6, 3.2, 3.0, 2.8, 2.6, 2.4, 2.2, 2.0];

    /// <summary>εb1,red при непродолжительном действии нагрузки, тяжёлый бетон (п. 6.1.21).</summary>
    public const double EpsB1RedShort = 0.0015;

    /// <summary>
    /// Коэффициент ползучести φb,cr по таблице 6.12. Промежуточный класс — по ближайшему
    /// меньшему табличному (в запас); ниже B10 — как B10. <see langword="null"/> — класс не задан.
    /// </summary>
    public static double? PhiBCr(double concreteClass, Sp63Humidity humidity)
    {
        if (!(concreteClass > 0)) return null;
        var row = humidity switch
        {
            Sp63Humidity.Above75 => PhiAbove75,
            Sp63Humidity.Below40 => PhiBelow40,
            _ => Phi40To75
        };
        int col = 0;
        for (int i = 0; i < PhiClasses.Length; i++)
            if (concreteClass >= PhiClasses[i] - 1e-9) col = i;
        return row[col];
    }

    /// <summary>
    /// εb1,red при продолжительном действии нагрузки по таблице 6.10; для высокопрочных
    /// бетонов (B70 и выше) — с множителем (270 − B)/210 (примечание 2).
    /// </summary>
    public static double EpsB1RedLong(double concreteClass, Sp63Humidity humidity)
    {
        double eps = humidity switch
        {
            Sp63Humidity.Above75 => 0.0024,
            Sp63Humidity.Below40 => 0.0034,
            _ => 0.0028
        };
        return concreteClass >= 70 ? eps * (270.0 - concreteClass) / 210.0 : eps;
    }

    /// <summary>
    /// Вычисляет полную кривизну. Для участка без трещин — (8.140): (1/r)1 от
    /// непродолжительного действия кратковременной части (1 − ψ), (1/r)2 от продолжительного
    /// действия длительной части ψ. Для участка с трещинами — (8.141): (1/r)1 от
    /// непродолжительного действия всей нагрузки, (1/r)2 и (1/r)3 — от непродолжительного и
    /// продолжительного действия длительной части.
    /// </summary>
    /// <param name="input">Геометрия, армирование и материалы.</param>
    /// <param name="m">Полный изгибающий момент (модуль) относительно середины высоты, кН·м.</param>
    /// <param name="n">Полная продольная сила, кН ("+" — растяжение).</param>
    /// <param name="longTermShare">Доля длительных нагрузок ψ (0…1).</param>
    /// <param name="cracked">Образуются ли трещины от полной нагрузки (п. 8.2.23).</param>
    /// <param name="mcrcFull">Mcrc при полной продольной силе, кН·м.</param>
    /// <param name="mcrcLong">Mcrc при длительной продольной силе ψ·N, кН·м.</param>
    /// <returns><see langword="null"/>, если класс бетона не задан или сечение растянуто насквозь.</returns>
    public static Sp63CurvatureResult? Compute(Sp63CurvatureInput input, double m, double n,
        double longTermShare, bool cracked, double mcrcFull, double mcrcLong)
    {
        if (PhiBCr(input.ConcreteClass, input.Humidity) is not { } phi) return null;

        double ebShort = 0.85 * input.Eb;                          // (8.146)
        double ebLong = input.Eb / (1.0 + phi);                     // (8.147)
        double ebRedShort = input.RbSer / EpsB1RedShort;            // (6.9)
        double epsLong = EpsB1RedLong(input.ConcreteClass, input.Humidity);
        double ebRedLong = input.RbSer / epsLong;

        double mLong = longTermShare * m, nLong = longTermShare * n;
        var result = new Sp63CurvatureResult { Cracked = cracked, PhiBCr = phi, EpsB1RedLong = epsLong };

        if (!cracked)
        {
            var t1 = Uncracked(input, 1, false, m - mLong, n - nLong, ebShort);
            var t2 = Uncracked(input, 2, true, mLong, nLong, ebLong);
            result.Terms = [t1, t2];
            result.Total = t1.Curvature + t2.Curvature;                   // (8.140)
            return result;
        }

        var c1 = Cracked(input, 1, false, m, n, ebRedShort, ebShort, mcrcFull);
        var c2 = Cracked(input, 2, false, mLong, nLong, ebRedShort, ebShort, mcrcLong);
        var c3 = Cracked(input, 3, true, mLong, nLong, ebRedLong, ebLong, mcrcLong);
        if (c1 is null || c2 is null || c3 is null) return null;
        result.Terms = [c1, c2, c3];
        result.Total = c1.Curvature - c2.Curvature + c3.Curvature;        // (8.141)
        return result;
    }

    /// <summary>Составляющая без трещин: (8.142)–(8.145), приведённое сечение с α = Es/Eb1.</summary>
    static Sp63CurvatureTerm Uncracked(Sp63CurvatureInput s, int index, bool longTerm,
        double m, double n, double eb1)
    {
        double alpha = s.Es / eb1;
        double area = s.B * s.H + alpha * (s.As + s.AsPrime);
        double yc = (s.B * s.H * s.H / 2.0 + alpha * s.As * s.H0 + alpha * s.AsPrime * s.APrime) / area;
        double iRed = s.B * Math.Pow(s.H, 3) / 12.0 + s.B * s.H * Math.Pow(s.H / 2.0 - yc, 2)
            + alpha * s.As * Math.Pow(s.H0 - yc, 2) + alpha * s.AsPrime * Math.Pow(yc - s.APrime, 2);
        double mRed = MomentAboutCentroid(s, m, n, yc);
        double d = eb1 * iRed;
        return new Sp63CurvatureTerm
        {
            Index = index, LongTerm = longTerm, M = m, N = n, MRed = mRed, Eb1 = eb1,
            Yc = yc, IRed = iRed, D = d, Curvature = m > 0 ? mRed / d : 0.0
        };
    }

    /// <summary>
    /// Составляющая с трещинами: xm по (8.151) с поправкой (8.154) на продольную силу,
    /// Ired по (8.148) с αs1 = Es/Eb,red и αs2 = Es/(ψs·Eb,red), ψs по (8.138);
    /// жёсткость не более жёсткости без трещин при той же продолжительности (п. 8.2.27).
    /// </summary>
    static Sp63CurvatureTerm? Cracked(Sp63CurvatureInput s, int index, bool longTerm,
        double m, double n, double ebRed, double eb1Uncracked, double mcrc)
    {
        var uncracked = Uncracked(s, index, longTerm, m, n, eb1Uncracked);
        if (!(m > 0)) return uncracked;

        // (8.138); та же граница [0,1; 1], что и в расчёте ширины раскрытия трещин.
        double psiS = Math.Clamp(1.0 - 0.8 * mcrc / m, 0.1, 1.0);
        double as1 = s.Es / ebRed;                     // (8.157)
        double as2 = s.Es / psiS / ebRed;              // (8.158), (8.159)
        double bh0 = s.B * s.H0;
        double sum = s.As / bh0 * as2 + s.AsPrime / bh0 * as1;
        double xM = s.H0 * (Math.Sqrt(sum * sum + 2.0 * (s.As / bh0 * as2
            + s.AsPrime / bh0 * as1 * s.APrime / s.H0)) - sum);   // (8.151)

        // (8.154): Ired, Ared полного сечения — с тем же αs1, что и xM (как в ShellSimplSolver).
        double aFull = s.B * s.H + as1 * (s.As + s.AsPrime);
        double ycFull = (s.B * s.H * s.H / 2.0 + as1 * s.As * s.H0 + as1 * s.AsPrime * s.APrime) / aFull;
        double iFull = s.B * Math.Pow(s.H, 3) / 12.0 + s.B * s.H * Math.Pow(s.H / 2.0 - ycFull, 2)
            + as1 * s.As * Math.Pow(s.H0 - ycFull, 2) + as1 * s.AsPrime * Math.Pow(ycFull - s.APrime, 2);
        double xm = xM - iFull * n / (aFull * m);      // "+" растяжение → знак «минус»
        if (xm <= 0.0) return null;                     // сквозное растяжение — вне формульного пути
        xm = Math.Min(xm, s.H0);

        // (8.148): приведённое сечение без растянутого бетона, моменты инерции — относительно
        // его центра тяжести (для изгиба совпадает с xm).
        double area = s.B * xm + as2 * s.As + as1 * s.AsPrime;
        double yc = (s.B * xm * xm / 2.0 + as2 * s.As * s.H0 + as1 * s.AsPrime * s.APrime) / area;
        double iRed = s.B * Math.Pow(xm, 3) / 12.0 + s.B * xm * Math.Pow(yc - xm / 2.0, 2)
            + as2 * s.As * Math.Pow(s.H0 - yc, 2) + as1 * s.AsPrime * Math.Pow(yc - s.APrime, 2);
        double d = ebRed * iRed;
        bool limited = d > uncracked.D;
        if (limited) d = uncracked.D;
        double mRed = MomentAboutCentroid(s, m, n, yc);
        return new Sp63CurvatureTerm
        {
            Index = index, LongTerm = longTerm, M = m, N = n, MRed = mRed, Eb1 = ebRed,
            PsiS = psiS, Xm = xm, Yc = yc, IRed = iRed, D = d, LimitedByUncracked = limited,
            Curvature = mRed / d
        };
    }

    /// <summary>
    /// Момент относительно центра тяжести приведённого сечения (п. 8.2.25): сила N приложена
    /// в середине высоты; растягивающая сила ниже центра тяжести увеличивает момент.
    /// </summary>
    static double MomentAboutCentroid(Sp63CurvatureInput s, double m, double n, double yc) =>
        m + n * (s.H / 2.0 - yc);
}
