namespace CScore.Sp63.Normal;

/// <summary>Чистые формулы таврового/двутаврового сечения (п. 8.1.11 СП 63).</summary>
public static class Sp63TeeFormulas
{
    const double RatioBoundaryTolerance = 1e-12;

    /// <summary>
    /// Проверяет условие (8.6): нейтральная ось проходит в пределах сжатой полки.
    /// Вызывается только при наличии сжатой полки (hf &gt; 0).
    /// </summary>
    public static bool IsNeutralAxisInFlange(double rs, double asT, double rsc,
        double asC, double rb, double bfEff, double hf)
    {
        EnsurePositive(rs, nameof(rs));
        EnsurePositive(rb, nameof(rb));
        EnsurePositive(bfEff, nameof(bfEff));
        EnsurePositive(hf, nameof(hf));
        return rs * asT - rsc * asC <= rb * bfEff * hf;
    }

    /// <summary>Вычисляет x по формуле (8.7) при границе сжатой зоны в ребре, м.</summary>
    public static double BendingX_CaseB(double rs, double asT, double rsc,
        double asC, double rb, double bw, double bfEff, double hf)
    {
        EnsurePositive(rb, nameof(rb));
        EnsurePositive(bw, nameof(bw));
        EnsurePositive(hf, nameof(hf));
        EnsureFlangeWiderThanWeb(bfEff, bw);
        return (rs * asT - rsc * asC - rb * (bfEff - bw) * hf) / (rb * bw);
    }

    /// <summary>Вычисляет Mult по формуле (8.8) при границе сжатой зоны в ребре, кН·м.</summary>
    public static double BendingMoment_CaseB(double rb, double bw, double bfEff,
        double hf, double x, double h0, double rsc, double asC, double aPrime)
    {
        EnsurePositive(rb, nameof(rb));
        EnsurePositive(bw, nameof(bw));
        EnsurePositive(hf, nameof(hf));
        EnsurePositive(x, nameof(x));
        EnsurePositive(h0, nameof(h0));
        if (!(h0 > hf))
            throw new ArgumentOutOfRangeException(nameof(hf),
                "Рабочая высота должна быть больше толщины полки.");
        if (!double.IsFinite(aPrime) || aPrime < 0 || !(h0 > aPrime))
            throw new ArgumentOutOfRangeException(nameof(aPrime),
                "Расстояние до сжатой арматуры должно быть неотрицательным и меньше h0.");
        EnsureFlangeWiderThanWeb(bfEff, bw);
        return rb * bw * x * (h0 - 0.5 * x)
             + rb * (bfEff - bw) * hf * (h0 - 0.5 * hf)
             + rsc * asC * (h0 - aPrime);
    }

    /// <summary>
    /// Эффективная ширина полки по п. 8.1.11 (случай «в», консольные свесы), м.
    /// Вызывается только при наличии сжатой полки; ширина полки строго больше стенки.
    /// </summary>
    public static double EffectiveFlangeWidth(double bw, double bfActual, double hf,
        double h, double spanLength)
    {
        EnsurePositive(bw, nameof(bw));
        EnsurePositive(h, nameof(h));
        EnsurePositive(spanLength, nameof(spanLength));
        EnsurePositive(hf, nameof(hf));
        EnsureFlangeWiderThanWeb(bfActual, bw);

        double ratio = hf / h;
        double overhangLimit = ratio >= 0.10 - RatioBoundaryTolerance ? 6.0 * hf
            : ratio >= 0.05 - RatioBoundaryTolerance ? 3.0 * hf
            : 0.0;
        double actualOverhangPerSide = (bfActual - bw) / 2.0;
        double overhang = Math.Min(Math.Min(overhangLimit, spanLength / 6.0),
            actualOverhangPerSide);
        return bw + 2.0 * Math.Max(0.0, overhang);
    }

    static void EnsureFlangeWiderThanWeb(double bfEff, double bw)
    {
        if (!(bfEff > bw))
            throw new ArgumentOutOfRangeException(nameof(bfEff),
                "Эффективная ширина полки должна быть строго больше ширины стенки.");
    }

    static void EnsurePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(name, "Значение должно быть положительным.");
    }
}
