using CScore.Fem;
using CScore.Fem.Combinations;
using CSfea.CScoreBridge.Structural;
using OpenCS.Tasks;

namespace OpenCS.Services;

/// <summary>Слагаемое комбинации в линейном результате: шаг загружения и коэффициент.</summary>
public sealed record FemCsfeaCombinationTerm(FemLoadCase LoadCase, double Factor, int StepN);

/// <summary>
/// Комбинация схемы относительно линейного результата: слагаемые с шагами загружений; <see cref="Missing"/> —
/// загружения с k ≠ 0, которых нет в расчёте; <see cref="Error"/> — выражение не разобрано.
/// </summary>
public sealed record FemCsfeaCombinationCase(FemLoadDefinition Definition, IReadOnlyList<FemCsfeaCombinationTerm> Terms,
    IReadOnlyList<FemLoadCase> Missing, string? Error = null)
{
    public bool IsAvailable => Error == null && Missing.Count == 0 && Terms.Count > 0;
}

/// <summary>
/// Комбинации схемы по результату линейного CSfea без перерасчёта (спека 2026-10-16): поля комбинации — сумма
/// k·поля загружения. Суммируются перемещения, усилия пластин и стержней, реакции и ΣR/ΣF — у упругого расчёта всё
/// линейно; состояния сечений у линейного расчёта пусты (NaN, флаги 0).
/// </summary>
public static class FemCsfeaCombinationFields
{
    /// <summary>
    /// Загружение → номер шага линейного результата: строки постановки из одного загружения (k = 1), последний
    /// сошедшийся шаг строки с записанными полями; повтор загружения — первая строка.
    /// </summary>
    public static Dictionary<int, int> CaseSteps(FemAnalysis analysis, FemCsfeaResultSummary summary)
    {
        var steps = new Dictionary<int, int>();
        if (summary.Kind != FemCsfeaRunner.LinearAnalysisKind) return steps;
        var stages = FemAnalysisParams.Parse(analysis.ParamsJson).ResolveStages(analysis);
        for (int i = 0; i < stages.Count; i++)
        {
            if (FemLinearLoadCaseForceSets.StageLoadCaseId(FemLoadExpression.Parse(stages[i].LoadExpressionJson)) is not { } id
                || steps.ContainsKey(id)) continue;
            if (summary.Steps.Where(s => s.Stage == i && s.Converged && s.Fields).MaxBy(s => s.N) is { } step)
                steps[id] = step.N;
        }
        return steps;
    }

    /// <summary>Слагаемые комбинации по шагам загружений (<see cref="CaseSteps"/>).</summary>
    public static FemCsfeaCombinationCase Resolve(FemLoadDefinition definition, IReadOnlyList<FemLoadCase> loadCases,
        IReadOnlyDictionary<int, int> caseSteps)
    {
        IReadOnlyList<(FemLoadCase LoadCase, double Factor)> terms;
        try { terms = FemLoadExpressionResolver.Terms(definition.GetExpression(), loadCases); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException
            or System.Text.Json.JsonException)
        {
            return new FemCsfeaCombinationCase(definition, [], [], ex.Message);
        }
        var resolved = new List<FemCsfeaCombinationTerm>();
        var missing = new List<FemLoadCase>();
        foreach (var (lc, k) in terms)
        {
            if (k == 0) continue;
            if (caseSteps.TryGetValue(lc.Id, out int n)) resolved.Add(new FemCsfeaCombinationTerm(lc, k, n));
            else missing.Add(lc);
        }
        return new FemCsfeaCombinationCase(definition, resolved, missing);
    }

    /// <summary>Поля записаны на одной сетке: одинаковые номера узлов, КЭ и опор в том же порядке.</summary>
    public static bool SameMesh(RcSecantStepFields a, RcSecantStepFields b) =>
        a.NodeIds.AsSpan().SequenceEqual(b.NodeIds) && a.ShellIds.AsSpan().SequenceEqual(b.ShellIds)
        && a.BeamIds.AsSpan().SequenceEqual(b.BeamIds) && a.ReactionNodeIds.AsSpan().SequenceEqual(b.ReactionNodeIds);

    /// <summary>
    /// Полный прогиб по п. 8.2.24 (формула 8.141, перенесённая с кривизн сечения на перемещения схемы): u = u₁ − u₂ + u₃
    /// покомпонентно (перемещения и повороты узлов); u₁ — полная нагрузка, u₂ — длительная часть на тех же диаграммах
    /// (null — нулевая), u₃ — длительная часть на диаграммах NL. Усилия и реакции не складываются — нули, состояния
    /// пластин пусты (NaN, флаги 0).
    /// </summary>
    public static RcSecantStepFields FullDeflection(RcSecantStepFields total, RcSecantStepFields? longPart, RcSecantStepFields longNL)
    {
        ArgumentNullException.ThrowIfNull(total);
        ArgumentNullException.ThrowIfNull(longNL);
        if (!SameMesh(total, longNL) || longPart != null && !SameMesh(total, longPart))
            throw new InvalidOperationException("Поля полной нагрузки и длительной части записаны на разных сетках.");
        var u = new double[total.Displacements.Length];
        for (int i = 0; i < u.Length; i++)
            u[i] = total.Displacements[i] - (longPart?.Displacements[i] ?? 0) + longNL.Displacements[i];
        var states = new double[total.ShellStates.Length];
        Array.Fill(states, double.NaN);
        return new RcSecantStepFields
        {
            Stage = -1, Step = 1, LoadFactor = 1,
            NodeIds = total.NodeIds, Displacements = u,
            ShellIds = total.ShellIds, ShellForces = new double[total.ShellForces.Length], ShellStates = states,
            ShellFlags = new byte[total.ShellFlags.Length],
            BeamIds = total.BeamIds, BeamForces = new double[total.BeamForces.Length], BeamFlags = new byte[total.BeamFlags.Length],
        };
    }

    /// <summary>
    /// Σ k·поля. Поля всех слагаемых — одной сетки (одинаковые номера узлов, КЭ и опор), иначе
    /// <see cref="InvalidOperationException"/>. ΣR/ΣF суммируются, если есть у всех слагаемых.
    /// </summary>
    public static RcSecantStepFields Superpose(IReadOnlyList<(RcSecantStepFields Fields, double Factor)> terms)
    {
        if (terms.Count == 0) throw new ArgumentException("Нет слагаемых.", nameof(terms));
        var first = terms[0].Fields;
        foreach (var (f, _) in terms.Skip(1))
            if (!SameMesh(f, first))
                throw new InvalidOperationException("Поля загружений записаны на разных сетках.");

        double[] Sum(Func<RcSecantStepFields, double[]> pick)
        {
            int len = pick(first).Length;
            if (terms.Any(t => pick(t.Fields).Length != len)) return [];
            var acc = new double[len];
            foreach (var (f, k) in terms)
            {
                var a = pick(f);
                for (int i = 0; i < len; i++) acc[i] += k * a[i];
            }
            return acc;
        }

        var states = new double[first.ShellStates.Length];
        Array.Fill(states, double.NaN);
        return new RcSecantStepFields
        {
            Stage = -1, Step = 1, LoadFactor = 1,
            NodeIds = first.NodeIds, Displacements = Sum(f => f.Displacements),
            ShellIds = first.ShellIds, ShellForces = Sum(f => f.ShellForces), ShellStates = states,
            ShellFlags = new byte[first.ShellFlags.Length],
            BeamIds = first.BeamIds, BeamForces = Sum(f => f.BeamForces), BeamFlags = new byte[first.BeamFlags.Length],
            ReactionNodeIds = first.ReactionNodeIds, Reactions = Sum(f => f.Reactions), ReactionKinds = first.ReactionKinds,
            ReactionSums = Sum(f => f.ReactionSums), LoadSum = Sum(f => f.LoadSum),
        };
    }
}
