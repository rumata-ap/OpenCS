namespace CScore.Sp63Punching;

/// <summary>Итог подбора поперечной арматуры.</summary>
public enum PunchingDesignState
{
    /// <summary>Кисп по бетону ≤ 1 — арматура не требуется.</summary>
    NotRequired,

    /// <summary>Требуемое qsw — из условия Кисп = 1.</summary>
    ByCalculation,

    /// <summary>Требуемое qsw — по порогу учёта Fsw,ult ≥ 0,25·Fb,ult (п. 8.1.48).</summary>
    ByThreshold,

    /// <summary>Арматурой не обеспечить: Кисп &gt; 1 и при пределе 2Fb, 2Mb.</summary>
    NotEnsured,

    /// <summary>Арматура заданной раскладки не пересекает определяющий контур (лучи креста мимо контура, рядов в пирамиде нет).</summary>
    LayoutIneffective
}

/// <summary>
/// Результат подбора. Требуемая арматура — множитель <see cref="Lambda"/> к qsw заданной раскладки
/// (при отсутствии раскладки — к равномерной q = 1 кН/м, тогда <see cref="QswReq"/> = λ).
/// </summary>
/// <param name="State">Итог основной зоны.</param>
/// <param name="Lambda">Требуемый множитель к qsw раскладки (0 — не требуется, ∞ — не обеспечить).</param>
/// <param name="GoverningId">Контур, определивший требование.</param>
/// <param name="Cross">Раскладка — крест.</param>
/// <param name="QswReq">Требуемое qsw равномерной раскладки, кН/м.</param>
/// <param name="QswXReq">Требуемое qsw лучей вдоль X, кН/м.</param>
/// <param name="QswYReq">Требуемое qsw лучей вдоль Y, кН/м.</param>
/// <param name="QswProvided">qsw заданной раскладки (равномерная), кН/м.</param>
/// <param name="QswXProvided">qsw лучей вдоль X заданной раскладки, кН/м.</param>
/// <param name="QswYProvided">qsw лучей вдоль Y, кН/м.</param>
/// <param name="AswLineReq">Требуемая площадь стержней одной линии в пирамиде, м² (нет раскладки — NaN).</param>
/// <param name="BarAreaReq">Требуемая площадь одного стержня при заданных рядах, м²; null — ряды не заданы.</param>
/// <param name="BarDiameter">Наименьший диаметр из ряда с площадью ≥ требуемой, м; null — нет подходящего.</param>
/// <param name="RowsRequired">Требуемое число рядов для контуров за армированием (не меньше 1,5h0 по п. 10.3.17); null — ряды не заданы.</param>
/// <param name="ZoneExtentRequired">Требуемая граница армированной зоны от грани, м; null — не определялась.</param>
/// <param name="BeyondNotEnsured">Контур за армированием не проходит при любой ширине зоны (до 100 рядов / 20h0).</param>
/// <param name="UtilizationAtRequired">Кисп основной зоны при требуемой арматуре.</param>
/// <param name="ProvidedSufficient">Заданной арматуры достаточно для основной зоны.</param>
/// <param name="CapitalBeyondFails">Не проходит проверка за капителью (по бетону) — арматура у колонны её не исправит.</param>
public sealed record PunchingDesignResult(
    PunchingDesignState State, double Lambda, string? GoverningId, bool Cross,
    double QswReq, double QswXReq, double QswYReq,
    double QswProvided, double QswXProvided, double QswYProvided,
    double AswLineReq, double? BarAreaReq, double? BarDiameter,
    int? RowsRequired, double? ZoneExtentRequired, bool BeyondNotEnsured,
    double UtilizationAtRequired, bool ProvidedSufficient, bool CapitalBeyondFails = false);

/// <summary>
/// Подбор поперечной арматуры при продавливании (пп. 8.1.48, 8.1.50): требуемое qsw при заданной раскладке
/// для Кисп = 1 по контурам основной зоны с порогом 0,25Fb и пределом 2Fb, ширина зоны для контуров за армированием.
/// </summary>
public static class PunchingDesign
{
    /// <summary>Ряд диаметров поперечной арматуры, мм.</summary>
    public static readonly IReadOnlyList<double> StandardDiametersMm = [6, 8, 10, 12, 14, 16, 18, 20, 22, 25, 28, 32];

    const double Tol = 1e-9;

    /// <summary>Подбирает арматуру.</summary>
    /// <param name="input">Исходные данные (арматура — раскладка; её площадь — масштаб).</param>
    /// <param name="includeZone">Искать требуемую ширину зоны (перестроение геометрии; в пакете — false).</param>
    /// <param name="geometry">Готовая геометрия узла (пакет).</param>
    public static PunchingDesignResult Solve(PunchingInput input, bool includeZone = true, PunchingGeometryCache? geometry = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        var options = input.Options ?? new PunchingOptions();
        var provided = input.Reinforcement;
        // У капители арматура работает у колонны — рабочая высота h0 + hc.
        double h0 = PunchingCapitalCheck.ColumnH0(input);
        var reference = ReferenceLayout(input.Reinforcement, h0);
        var refInput = input with { Reinforcement = reference };
        // Геометрия контуров основной зоны от площади стержня не зависит; кэш пакета годится, если раскладка та же.
        var geom = geometry is not null && ReferenceEquals(reference, provided) ? geometry : PunchingGeometryCache.Prepare(refInput);

        var refSw = SwOf(reference, h0);
        var provSw = SwOf(provided, h0);

        var r0 = PunchingChecker.Check(refInput, false, geom, 0);
        bool capFails = r0.Variants.Any(v => v.Variant.Zone == PunchingZone.BeyondCapital && v.Utilization > 1 + Tol);
        var at0 = Main(r0, options);
        double k0 = at0.Count > 0 ? at0.Max(v => v.Utilization) : 0;
        PunchingDesignResult Make(PunchingDesignState st, double lam, string? gov, double util)
        {
            double asw = refSw.Asw * lam;
            double? barReq = reference?.Rows is { } rows && double.IsFinite(lam) ? rows.BarArea * lam : null;
            double? dia = barReq is double b ? StandardDiametersMm.Select(d => d / 1000).Cast<double?>()
                .FirstOrDefault(d => Math.PI * d!.Value * d.Value / 4 >= b * (1 - 1e-9)) : null;
            if (barReq is 0) dia = null;
            var (rowsReq, extReq, beyondBad) = includeZone && st is PunchingDesignState.ByCalculation or PunchingDesignState.ByThreshold
                ? Zone(input, provided) : (null, null, false);
            double qReq = refSw.Q * lam, qxReq = refSw.QX * lam, qyReq = refSw.QY * lam;
            bool enough = st == PunchingDesignState.NotRequired
                || (st is PunchingDesignState.ByCalculation or PunchingDesignState.ByThreshold && provided is not null
                    && provSw.Q >= qReq * (1 - 1e-9) && provSw.QX >= qxReq * (1 - 1e-9) && provSw.QY >= qyReq * (1 - 1e-9));
            return new PunchingDesignResult(st, lam, gov, reference?.Cross is not null,
                qReq, qxReq, qyReq, provSw.Q, provSw.QX, provSw.QY,
                provided is null ? double.NaN : asw, barReq, dia,
                rowsReq, extReq, beyondBad, util, enough, capFails);
        }

        if (at0.Count == 0 || k0 <= 1 + Tol)
            return Make(PunchingDesignState.NotRequired, 0, at0.MaxBy(v => v.Utilization)?.Variant.Id, k0);

        // Верхняя граница: Fsw ≥ Fb и Msw ≥ Mb у всех контуров основной зоны.
        var at1 = Main(PunchingChecker.Check(refInput, false, geom, 1), options);
        double cap = 0;
        foreach (var v in at1)
        {
            var sw = v.Sw;
            if (sw is null || sw.FswRaw <= 0)
            {
                if (v.UtilizationConcreteOnly > 1 + Tol)
                    return Make(PunchingDesignState.LayoutIneffective, double.PositiveInfinity, v.Variant.Id, v.Utilization);
                continue;
            }
            cap = Math.Max(cap, v.Fb / sw.FswRaw);
            if (sw.MswxRaw > 0) cap = Math.Max(cap, v.Mbx / sw.MswxRaw);
            if (sw.MswyRaw > 0) cap = Math.Max(cap, v.Mby / sw.MswyRaw);
        }
        cap *= 1 + 1e-9;
        double KAt(double lam, out PunchingVariantResult? gov)
        {
            var list = Main(PunchingChecker.Check(refInput, false, geom, lam), options);
            gov = list.MaxBy(v => v.Utilization);
            return gov?.Utilization ?? 0;
        }
        double kcap = KAt(cap, out var govCap);
        if (kcap > 1 + Tol)
            return Make(PunchingDesignState.NotEnsured, double.PositiveInfinity, govCap?.Variant.Id, kcap);

        double lo = 0, hi = cap;
        for (int i = 0; i < 100 && hi - lo > 1e-12 * Math.Max(1, hi); i++)
        {
            double mid = 0.5 * (lo + hi);
            if (KAt(mid, out _) <= 1) hi = mid; else lo = mid;
        }
        var atReq = Main(PunchingChecker.Check(refInput, false, geom, hi), options);
        var govReq = atReq.MaxBy(v => v.Utilization);
        // Порог: у контура, которому нужна арматура, Fsw ровно 0,25Fb — меньшая арматура не учитывается совсем.
        bool threshold = atReq.Any(v => v.SwCounted && v.UtilizationConcreteOnly > 1 + Tol
            && Math.Abs(v.Sw!.FswRaw - 0.25 * v.Fb) <= 1e-6 * v.Fb);
        return Make(threshold ? PunchingDesignState.ByThreshold : PunchingDesignState.ByCalculation, hi,
            govReq?.Variant.Id, govReq?.Utilization ?? 0);
    }

    static List<PunchingVariantResult> Main(PunchingResult r, PunchingOptions o)
        => r.Variants.Where(v => v.Variant.Zone == PunchingZone.Main
            && (v.Variant.Kind != PunchingVariantKind.PylonReduced || o.PylonReducedGoverns)).ToList();

    /// <summary>Asw линии и qsw раскладки (единичный множитель).</summary>
    static (double Asw, double Q, double QX, double QY) SwOf(PunchingShearReinforcement? r, double h0)
    {
        if (r is null) return (0, 0, 0, 0);
        double asw = PunchingSwLayout.AswPerLine(r, h0);
        if (r.Cross is not null)
        {
            var (qx, qy) = PunchingSwLayout.QCross(r, h0);
            return (asw, 0, qx, qy);
        }
        return (asw, PunchingSwLayout.QUniform(r, h0), 0, 0);
    }

    /// <summary>
    /// Раскладка-масштаб: заданная, если её qsw &gt; 0; с нулевой площадью или Rsw — та же геометрия с единичными
    /// площадью и Rsw; без арматуры — равномерная q = 1 кН/м.
    /// </summary>
    static PunchingShearReinforcement? ReferenceLayout(PunchingShearReinforcement? r, double h0)
    {
        if (r is null) return new PunchingShearReinforcement(1, 1, 1);
        var s = SwOf(r, h0);
        if (s.Q > 0 || s.QX > 0 || s.QY > 0) return r;
        var x = r with { Rsw = r.Rsw > 0 ? r.Rsw : 1 };
        if (x.Rows is { } rows) x = x with { Rows = rows with { BarArea = rows.BarArea > 0 ? rows.BarArea : 1e-4 } };
        else x = x with { Asw = x.Asw > 0 ? x.Asw : 1e-4 };
        if (x.Cross is null && !(x.Sw > 0)) x = x with { Sw = 0.1 };
        return x;
    }

    /// <summary>
    /// Ширина зоны для контуров за армированием (по бетону). С рядами — наименьшее число рядов
    /// (и не меньше требуемого п. 10.3.17 для 1,5h0); без рядов — граница зоны бисекцией до 20h0.
    /// </summary>
    static (int? Rows, double? Extent, bool NotEnsured) Zone(PunchingInput input, PunchingShearReinforcement? provided)
    {
        if (provided is null) return (null, null, false);
        double h0 = PunchingCapitalCheck.ColumnH0(input);
        bool BeyondOk(PunchingShearReinforcement r)
        {
            var res = PunchingChecker.Check(input with { Reinforcement = r }, false);
            return res.Variants.Where(v => v.Variant.Zone == PunchingZone.BeyondReinforcement).All(v => v.Utilization <= 1 + Tol);
        }
        if (provided.Rows is { } rows)
        {
            int minByDetailing = rows.Step > 0 ? Math.Max(1, (int)Math.Ceiling((1.5 * h0 - rows.FirstRow) / rows.Step - 1e-9) + 1) : 1;
            for (int n = 1; n <= 100; n++)
                if (BeyondOk(provided with { Rows = rows with { Count = n } }))
                {
                    int req = Math.Max(n, minByDetailing);
                    return (req, rows.FirstRow + (req - 1) * rows.Step, false);
                }
            return (null, null, true);
        }
        double lo = 0, hi = 20 * h0;
        if (!BeyondOk(provided with { ZoneExtent = hi })) return (null, null, true);
        if (BeyondOk(provided with { ZoneExtent = 1e-6 })) return (null, 0, false);
        for (int i = 0; i < 50 && hi - lo > 1e-5; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (BeyondOk(provided with { ZoneExtent = mid })) hi = mid; else lo = mid;
        }
        return (null, hi, false);
    }
}
