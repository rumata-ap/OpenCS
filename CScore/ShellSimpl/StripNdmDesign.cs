namespace CScore.ShellSimpl;

/// <summary>Исход подбора площадей полосы.</summary>
public enum StripNdmDesignStatus
{
    /// <summary>Арматура не нужна: бетон воспринимает усилия (ПС1) или трещины нет (ПС2).</summary>
    NotRequired,
    /// <summary>Достаточно ряда у растянутой моментом грани.</summary>
    TensionOnly,
    /// <summary>Нужны оба ряда (сжатая арматура, сквозное растяжение).</summary>
    WithCompression,
    /// <summary>Нет решения и при наибольшей площади — недостаточна толщина.</summary>
    Impossible,
}

/// <summary>Требуемые площади полосы, м²/м.</summary>
/// <param name="AsTension">Ряд у грани, растянутой моментом (при M ≥ 0 — верхний).</param>
/// <param name="AsCompression">Ряд у противоположной грани.</param>
/// <param name="TensionTop">Растянутый моментом ряд — верхний.</param>
/// <param name="Value">ПС1 — η при найденных площадях, ПС2 — a_crc, мм.</param>
public sealed record StripNdmDesignResult(double AsTension, double AsCompression, bool TensionTop,
    StripNdmDesignStatus Status, double Value, string Reason = "");

/// <summary>
/// Подбор площадей полосы по НДМ (спека, раздел 4.5): ПС1 — минимальная сумма As_t + As_c при η ≤ 1,
/// ПС2 — As_t при a_crc ≤ a_crc,ult. Точность — 0,5 % площади.
/// </summary>
public static class StripNdmDesign
{
    const double RelTol = 0.005;

    /// <summary>Наибольшая рассматриваемая площадь ряда — 0,1·h·1 м, заведомо больше реальной.</summary>
    public static double MaxArea(double h) => 0.1 * h;

    static StripNdmSection Base(double h, double m, double zTop, double zBot, Diagramm concrete, Diagramm rebar,
        out bool tensionTop)
    {
        tensionTop = m >= 0.0;
        double zt = tensionTop ? zTop : zBot, zc = tensionTop ? zBot : zTop;
        return new StripNdmSection(h, [new StripNdmRow(zt, 0.0), new StripNdmRow(zc, 0.0)], concrete, rebar);
    }

    /// <summary>
    /// ПС1: As_c = 0 и бисекция по As_t; не хватает — наименьшая As_c, при которой решение есть,
    /// и золотое сечение по As_c ∈ [As_c,min, As_max] для минимума As_t + As_c.
    /// </summary>
    /// <param name="zTop">Координата верхнего ряда (&gt; 0), м.</param>
    /// <param name="zBot">Координата нижнего ряда (&lt; 0), м.</param>
    public static StripNdmDesignResult RequiredUls(double n, double m, double h, double zTop, double zBot,
        Diagramm concrete, Diagramm rebar, StripNdmLimits lim) =>
        RequiredUls(n, m, UlsTable(h, zTop, zBot, concrete, rebar, lim));

    /// <summary>Таблица предельного контура полосы подбора: ряды [верх, низ]. Одна на геометрию —
    /// переиспользуется для всех фасеток и строк усилий.</summary>
    public static StripNdmUlsTable UlsTable(double h, double zTop, double zBot, Diagramm concrete, Diagramm rebar,
        StripNdmLimits lim) =>
        StripNdm.Tabulate(new StripNdmSection(h, [new StripNdmRow(zTop, 0.0), new StripNdmRow(zBot, 0.0)], concrete, rebar), lim);

    /// <summary>ПС1 по готовой таблице контура (<see cref="UlsTable"/>, ряды [верх, низ]).</summary>
    public static StripNdmDesignResult RequiredUls(double n, double m, StripNdmUlsTable table)
    {
        bool tensionTop = m >= 0.0;
        double asMax = MaxArea(table.H);
        double Eta(double at, double ac)
        {
            Span<double> areas = stackalloc double[2];
            areas[0] = tensionTop ? at : ac;
            areas[1] = tensionTop ? ac : at;
            return table.Eta(areas, n, m);
        }

        double eta0 = Eta(0.0, 0.0);
        if (eta0 <= 1.0)
            return new StripNdmDesignResult(0.0, 0.0, tensionTop, StripNdmDesignStatus.NotRequired, eta0);

        // Наименьшая x ∈ (0, asMax], при которой ok(x); null — нет и при asMax.
        static double? Bisect(Func<double, bool> ok, double asMax)
        {
            if (!ok(asMax)) return null;
            double lo = 0.0, hi = asMax;
            while (hi - lo > RelTol * hi)
            {
                double mid = 0.5 * (lo + hi);
                if (ok(mid)) hi = mid; else lo = mid;
            }
            return hi;
        }

        var atOnly = Bisect(at => Eta(at, 0.0) <= 1.0, asMax);
        if (atOnly is double a1)
            return new StripNdmDesignResult(a1, 0.0, tensionTop, StripNdmDesignStatus.TensionOnly, Eta(a1, 0.0));

        var acMin = Bisect(ac => Eta(asMax, ac) <= 1.0, asMax);
        if (acMin == null)
            return new StripNdmDesignResult(asMax, asMax, tensionTop, StripNdmDesignStatus.Impossible, Eta(asMax, asMax),
                "сечение не проходит: прочность не обеспечена и при наибольшей площади обоих рядов — недостаточна толщина");

        (double Sum, double At) F(double ac)
        {
            var at = Bisect(x => Eta(x, ac) <= 1.0, asMax);
            return at is double v ? (ac + v, v) : (double.PositiveInfinity, asMax);
        }

        double best = acMin.Value;
        var bestF = F(best);
        void Consider(double ac, (double Sum, double At) f)
        {
            if (f.Sum < bestF.Sum) { best = ac; bestF = f; }
        }

        const double gr = 0.6180339887498949;
        double a = acMin.Value, b = asMax;
        double c = b - gr * (b - a), d = a + gr * (b - a);
        var fc = F(c); var fd = F(d);
        Consider(c, fc); Consider(d, fd);
        while (b - a > RelTol * Math.Max(bestF.Sum, 1e-6))
        {
            if (fc.Sum <= fd.Sum) { b = d; d = c; fd = fc; c = b - gr * (b - a); fc = F(c); Consider(c, fc); }
            else { a = c; c = d; fc = fd; d = a + gr * (b - a); fd = F(d); Consider(d, fd); }
        }
        return new StripNdmDesignResult(bestF.At, best, tensionTop, StripNdmDesignStatus.WithCompression, Eta(bestF.At, best));
    }

    /// <summary>
    /// ПС1 при заданной площади ряда у грани, сжатой моментом (<paramref name="asOther"/>), — проверка
    /// через ограничения: наименьшая As_t, при которой η ≤ 1. Если не хватает и наибольшей As_t, —
    /// подбор обоих рядов (<see cref="RequiredUls(double, double, StripNdmUlsTable)"/>), второй ряд
    /// тогда тоже становится требованием.
    /// </summary>
    public static StripNdmDesignResult RequiredTensionUls(double n, double m, StripNdmUlsTable table, double asOther)
    {
        bool tensionTop = m >= 0.0;
        double asMax = MaxArea(table.H);
        double Eta(double at)
        {
            Span<double> areas = stackalloc double[2];
            areas[0] = tensionTop ? at : asOther;
            areas[1] = tensionTop ? asOther : at;
            return table.Eta(areas, n, m);
        }

        double eta0 = Eta(0.0);
        if (eta0 <= 1.0)
            return new StripNdmDesignResult(0.0, asOther, tensionTop, StripNdmDesignStatus.NotRequired, eta0);
        if (Eta(asMax) > 1.0)
            return RequiredUls(n, m, table);

        double lo = 0.0, hi = asMax;
        while (hi - lo > RelTol * hi)
        {
            double mid = 0.5 * (lo + hi);
            if (Eta(mid) <= 1.0) hi = mid; else lo = mid;
        }
        return new StripNdmDesignResult(hi, asOther, tensionTop, StripNdmDesignStatus.TensionOnly, Eta(hi));
    }

    /// <summary>
    /// ПС2: бисекция по As_t до a_crc ≤ a_crc,ult. При сквозном растяжении (оба ряда растянуты по
    /// равновесию двух рядов) второй ряд наращивается пропорционально своей доле усилия, но не
    /// меньше заданной <paramref name="asCompression"/>.
    /// </summary>
    /// <param name="concreteN">Диаграмма бетона вида расчёта N (п. 6.1.26).</param>
    /// <param name="asCompression">As второго ряда — из ПС1 той же фасетки, иначе 0 (в запас).</param>
    public static StripNdmDesignResult RequiredSls(double n, double m, double h, double zTop, double zBot,
        double asCompression, double acrcLimMm, Diagramm concreteN, Diagramm rebarN,
        double dsTop, double dsBot, double es, double phi1, double phi2,
        SigmaSCrcMethod sigmaSCrcMethod = SigmaSCrcMethod.ReleasedConcrete8137)
    {
        var sec = Base(h, m, zTop, zBot, concreteN, rebarN, out bool tensionTop);
        double asMax = MaxArea(h);
        double zt = sec.Rows[0].Z, zc = sec.Rows[1].Z;
        double[] ds = tensionTop ? [dsTop, dsBot] : [dsBot, dsTop];

        double ft = (m - n * zc) / (zt - zc), fcomp = n - ft;
        double share = ft > 0 && fcomp > 0 ? fcomp / ft : 0.0;
        double Ac(double at) => Math.Max(asCompression, at * share);
        StripNdmSls Sls(double at) => StripNdm.CrackWidth(sec.WithAreas(at, Ac(at)), n, m, ds, es, phi1, phi2, sigmaSCrcMethod);

        const double tiny = 1e-7;
        var first = Sls(tiny);
        if (!first.Cracked)
            return new StripNdmDesignResult(0.0, asCompression, tensionTop, StripNdmDesignStatus.NotRequired, 0.0);

        bool Ok(double at) { var r = Sls(at); return r.Converged && r.AcrcMm <= acrcLimMm; }
        if (!Ok(asMax))
            return new StripNdmDesignResult(asMax, Ac(asMax), tensionTop, StripNdmDesignStatus.Impossible, Sls(asMax).AcrcMm,
                "ширина раскрытия трещин не обеспечивается и при наибольшей площади");
        double lo = tiny, hi = asMax;
        while (hi - lo > RelTol * hi)
        {
            double mid = 0.5 * (lo + hi);
            if (Ok(mid)) hi = mid; else lo = mid;
        }
        double ac = Ac(hi);
        var status = ac > asCompression + 1e-12 ? StripNdmDesignStatus.WithCompression : StripNdmDesignStatus.TensionOnly;
        return new StripNdmDesignResult(hi, ac, tensionTop, status, Sls(hi).AcrcMm);
    }
}
