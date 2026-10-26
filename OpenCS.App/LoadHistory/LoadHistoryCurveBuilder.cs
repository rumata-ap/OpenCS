using CScore.History;

namespace OpenCS.LoadHistory;

/// <summary>Точка кривой «момент–кривизна»: c, M_u и индекс шага в Steps / StepsNoHistory (−1 — начало).</summary>
public sealed record CurvePoint(double C, double M, int StepIndex);

/// <summary>Кривые «момент–кривизна» с историей и без, включения частей, пределы и пики.</summary>
public sealed record LoadHistoryCurves(
    IReadOnlyList<CurvePoint> WithHistory, IReadOnlyList<CurvePoint> WithoutHistory,
    IReadOnlyList<CurvePoint> Activations,
    CurvePoint? LimitWith, CurvePoint? LimitWithout, CurvePoint? PeakWith, CurvePoint? PeakWithout);

/// <summary>Точки кривых «момент–кривизна» из результата задачи load_history.</summary>
public static class LoadHistoryCurveBuilder
{
    /// <summary>
    /// X — c (проекция кривизны на направление изгиба u), Y — M_u (проекция момента на u); начало (0, 0)
    /// добавляется к обоим путям. Значения строятся как есть: при верном знаке нагрузки они положительны.
    /// </summary>
    public static LoadHistoryCurves Build(LoadHistoryResultData d)
    {
        var with = Curve(d.Steps);
        var without = d.StepsNoHistory is null ? [] : Curve(d.StepsNoHistory);

        var activations = new List<CurvePoint>();
        for (int i = 1; i < d.Steps.Count; i++)
            if (d.Steps[i].Parts > d.Steps[i - 1].Parts)
                activations.Add(Point(d.Steps, i));

        return new LoadHistoryCurves(with, without, activations,
            Limit(d.Steps), d.StepsNoHistory is null ? null : Limit(d.StepsNoHistory),
            Peak(d.Steps), d.StepsNoHistory is null ? null : Peak(d.StepsNoHistory));
    }

    static List<CurvePoint> Curve(IReadOnlyList<HistoryStepDto> steps)
    {
        var list = new List<CurvePoint>(steps.Count + 1) { new(0, 0, -1) };
        for (int i = 0; i < steps.Count; i++) list.Add(Point(steps, i));
        return list;
    }

    static CurvePoint Point(IReadOnlyList<HistoryStepDto> steps, int i) => new(steps[i].C, steps[i].Mu, i);

    static CurvePoint? Limit(IReadOnlyList<HistoryStepDto> steps)
    {
        for (int i = 0; i < steps.Count; i++)
            if (steps[i].LimitPoint) return Point(steps, i);
        return null;
    }

    static CurvePoint? Peak(IReadOnlyList<HistoryStepDto> steps)
    {
        int best = -1;
        for (int i = 0; i < steps.Count; i++)
            if (steps[i].Converged && (best < 0 || Math.Abs(steps[i].Mu) > Math.Abs(steps[best].Mu))) best = i;
        return best < 0 ? null : Point(steps, best);
    }
}
