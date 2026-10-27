using CScore;
using CScore.History;

namespace OpenCS.LoadHistory;

/// <summary>Сечение в состоянии шага результата load_history — для полей окна результата.</summary>
public static class LoadHistoryReplay
{
    /// <summary>
    /// Копия <paramref name="source"/> после шагов 0…<paramref name="index"/> пути (с историей или без) с теми же
    /// ролями, учётом растяжения и гистерезисом, что у задачи. Без гистерезиса — копия с включёнными частями.
    /// </summary>
    public static StagedSection Section(StagedSection source, LoadHistoryResultData d, CalcType calc, bool noHistory, int index)
    {
        var steps = (noHistory ? d.StepsNoHistory ?? [] : d.Steps)
            .Select(x => new LoadHistorySolver.ReplayStep(x.K, x.Parts,
                d.ConcreteTension && !x.Warnings.Contains("without_concrete_tension"))).ToList();
        var o = new LoadHistoryOptions
        {
            Calc = calc, Ten = d.ConcreteTension, Hysteresis = d.Hysteresis,
            Failure = new FailureOptions { SofteningStrain = d.SofteningStrain > 0 ? d.SofteningStrain : 0.0005 },
            Roles = d.NonStandardRoles.Select(r => new PartRoles(r.PartIndex, r.Concrete, r.Rebar)).ToList(),
        };
        var planes = d.ActivationPlanes.Select(p => new Kurvature { e0 = p[0], ky = p[1], kz = p[2] }).ToList();
        return LoadHistorySolver.Replay(source, o, steps, planes, withHistory: !noHistory, index);
    }
}
