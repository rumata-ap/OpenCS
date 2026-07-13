using CSTriangulation;

namespace CSfea.Torsion;

/// <summary>Один шаг серии сходимости: размер элемента и результат решения на нём.</summary>
public sealed class TorsionConvergenceStep
{
    public double ElementSize { get; init; }
    public TorsionProps Props { get; init; } = null!;
}

/// <summary>
/// Результат серии прогонов с экстраполяцией Ричардсона.
/// Шаги идут от грубого к мелкому: Steps[0] — h0, Steps[i] — h0/2^i.
/// </summary>
public sealed class TorsionAutoConvergeResult
{
    public IReadOnlyList<TorsionConvergenceStep> Steps { get; init; } = [];

    /// <summary>Итоговое It: экстраполированное, если экстраполяция признана надёжной, иначе — с самой мелкой сетки.</summary>
    public double It { get; init; }
    /// <summary>Оценённый порядок сходимости It (null, если оценить не удалось).</summary>
    public double? ItOrder { get; init; }
    public bool ItExtrapolated { get; init; }

    public double ShearCenterX { get; init; } = double.NaN;
    public double ShearCenterY { get; init; } = double.NaN;
    public double? ShearCenterXOrder { get; init; }
    public double? ShearCenterYOrder { get; init; }
    public bool ShearCenterExtrapolated { get; init; }
    public bool HasShearCenter { get; init; }

    /// <summary>Результат самой мелкой сетки — источник поля (τ, потенциал, треугольники/граница) для визуализации.</summary>
    public TorsionProps FinestProps => Steps[^1].Props;

    /// <summary>
    /// Собирает TorsionProps для дальнейшей обработки (расчёт τ_max и т.п.) и визуализации:
    /// It/центр кручения — из экстраполяции (или лучшей доступной оценки), поле — с самой мелкой сетки.
    /// </summary>
    public TorsionProps ToTorsionProps()
    {
        var finest = FinestProps;
        return new TorsionProps
        {
            It = It,
            ShearCenterX = ShearCenterX,
            ShearCenterY = ShearCenterY,
            TauUnitMax = finest.TauUnitMax,
            NodeX = finest.NodeX,
            NodeY = finest.NodeY,
            TauUnitField = finest.TauUnitField,
            PotentialField = finest.PotentialField,
            Triangles = finest.Triangles,
            BoundaryX = finest.BoundaryX,
            BoundaryY = finest.BoundaryY,
            BoundaryJ1 = finest.BoundaryJ1,
            Singular = finest.Singular,
            NElements = finest.NElements
        };
    }
}

/// <summary>
/// Экстраполяция Ричардсона по серии прогонов кручения с половинным шагом.
/// По умолчанию: h0 = минимальная длина ребра контура, N = 3 прогона (h0, h0/2, h0/4).
/// При N≥3 экстраполяция выполняется по последним трём прогонам; при N=2 — берётся мелкая сетка.
/// </summary>
public static class TorsionRichardson
{
    /// <summary>Минимально разумный порядок сходимости, при котором экстраполяции ещё доверяем.</summary>
    const double MinTrustedOrder = 0.05;
    /// <summary>Максимально разумный порядок — выше подозрительно (случайное совпадение на грубых сетках).</summary>
    const double MaxTrustedOrder = 6.0;

    /// <summary>Размеры элементов серии: h0, h0/2, … (n = max(2, nRuns)).</summary>
    internal static double[] BuildRunSizes(double h0, int nRuns)
    {
        int n = Math.Max(2, nRuns);
        var sizes = new double[n];
        for (int i = 0; i < n; i++)
            sizes[i] = h0 / Math.Pow(2.0, i);
        return sizes;
    }

    public static TorsionAutoConvergeResult SolveAutoConverge(
        TorsionBoundary boundary, TorsionMethod method,
        TriangulationMethod triangulation = TriangulationMethod.AdvancingFront,
        FemElementOrder femOrder = FemElementOrder.Linear,
        double? h0 = null,
        int nRuns = 3,
        CancellationToken ct = default)
    {
        double hStart = h0 ?? TorsionBoundaryMetrics.MinEdgeLength(boundary);
        if (!double.IsFinite(hStart) || hStart <= 0.0)
            throw new InvalidOperationException("Не удалось определить масштаб контура для авто-сходимости (вырожденная геометрия).");

        double[] sizes = BuildRunSizes(hStart, nRuns);
        var steps = new List<TorsionConvergenceStep>(sizes.Length);
        foreach (double h in sizes)
        {
            ct.ThrowIfCancellationRequested();
            var props = TorsionSolver.Solve(boundary, method, h, triangulation, femOrder, ct);
            steps.Add(new TorsionConvergenceStep { ElementSize = h, Props = props });
        }

        double itVal;
        double? itOrder;
        bool itExtra;
        if (steps.Count >= 3)
        {
            var itSeries = steps.TakeLast(3).Select(s => s.Props.It).ToArray();
            (itVal, itOrder, itExtra) = Extrapolate(itSeries);
        }
        else
        {
            itVal = steps[^1].Props.It;
            itOrder = null;
            itExtra = false;
        }

        bool hasSc = steps.All(s => double.IsFinite(s.Props.ShearCenterX) && double.IsFinite(s.Props.ShearCenterY));
        double scX = steps[^1].Props.ShearCenterX, scY = steps[^1].Props.ShearCenterY;
        double? scXOrder = null, scYOrder = null;
        bool scXExtra = false, scYExtra = false;
        if (hasSc && steps.Count >= 3)
        {
            var scXSeries = steps.TakeLast(3).Select(s => s.Props.ShearCenterX).ToArray();
            var scYSeries = steps.TakeLast(3).Select(s => s.Props.ShearCenterY).ToArray();
            (scX, scXOrder, scXExtra) = Extrapolate(scXSeries);
            (scY, scYOrder, scYExtra) = Extrapolate(scYSeries);
        }

        return new TorsionAutoConvergeResult
        {
            Steps = steps,
            It = itVal,
            ItOrder = itOrder,
            ItExtrapolated = itExtra,
            ShearCenterX = scX,
            ShearCenterY = scY,
            ShearCenterXOrder = scXOrder,
            ShearCenterYOrder = scYOrder,
            ShearCenterExtrapolated = scXExtra && scYExtra,
            HasShearCenter = hasSc
        };
    }

    /// <summary>
    /// Экстраполяция Ричардсона по 3 точкам (грубая→мелкая, геометрическое сгущение ×2).
    /// Возвращает (значение, оценённый порядок p, признак надёжности экстраполяции).
    /// Если ряд не монотонен, уже сошёлся, или порядок выглядит неправдоподобно —
    /// экстраполяция не применяется, возвращается значение с самой мелкой сетки.
    /// </summary>
    internal static (double value, double? order, bool extrapolated) Extrapolate(double[] seq)
    {
        if (seq.Length != 3 || seq.Any(v => !double.IsFinite(v)))
            return (seq.Length > 0 ? seq[^1] : double.NaN, null, false);

        double i1 = seq[0], i2 = seq[1], i3 = seq[2];
        double d1 = i1 - i2, d2 = i2 - i3;

        double scale = Math.Max(Math.Abs(i1), Math.Max(Math.Abs(i2), Math.Abs(i3)));
        double eps = Math.Max(scale * 1e-10, 1e-300);
        if (Math.Abs(d2) < eps)
            return (i3, null, false); // уже сошлось в пределах точности — экстраполировать нечего

        double ratio = d1 / d2;
        if (!double.IsFinite(ratio) || ratio <= 1.0000001)
            return (i3, null, false); // не монотонно/не убывает как степенной закон — доверять нельзя

        double p = Math.Log(ratio, 2.0);
        if (!double.IsFinite(p) || p < MinTrustedOrder || p > MaxTrustedOrder)
            return (i3, p, false); // порядок неправдоподобен — берём мелкую сетку, но публикуем p для диагностики

        double extrapolated = i3 + (i3 - i2) / (Math.Pow(2.0, p) - 1.0);
        return (extrapolated, p, true);
    }
}
