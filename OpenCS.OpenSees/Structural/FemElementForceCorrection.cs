namespace OpenCS.OpenSees.Structural;

/// <summary>
/// Поправка концевых усилий результата, когда нагрузки в пролёте переведены в эквивалентные узловые
/// (<see cref="FemMemberLoadNodalEquivalent"/>): с <c>eleLoad</c> сопротивление элемента равно
/// <c>K·u − p_eq</c>, а <c>localForce</c> при узловых эквивалентах даёт только <c>K·u</c>. Поправка
/// вычитает <c>Σ λ_s·p_eq,s</c> покомпонентно в <b>начальных</b> местных осях — приближённо при
/// Corotational (там <c>localForce</c> отдаётся в текущих повёрнутых осях).
/// </summary>
public static class FemElementForceCorrection
{
    /// <summary>Строка диагностики результата о приближённом переводе нагрузок.</summary>
    public const string LumpedLoadsDiagnostic =
        "Нагрузки стержней приближённо переведены в эквивалентные узловые (geomTransf Corotational): " +
        "концевые усилия исправлены в начальных местных осях, отклик и внутрипролётные усилия КЭ " +
        "отличаются от расчёта с нагрузкой в пролёте; для уточнения сгустите сетку.";

    /// <summary>
    /// Исправляет концевые усилия всех сошедшихся шагов. Для шага стадии <c>s</c> коэффициент текущей
    /// стадии — <c>LoadFactor</c> шага, для предыдущих — <c>LoadFactor</c> их последнего сошедшегося
    /// шага (после <c>loadConst</c> паттерн держится на достигнутом уровне), стадия без сошедшихся
    /// шагов даёт 0. Шаги уточнения обрабатываются как обычные. Пустой список эквивалентов — тот же
    /// объект без изменений.
    /// </summary>
    public static FemNonlinearResult Apply(FemNonlinearResult result, IReadOnlyList<FemElementLoadEquivalent> equivalents,
        string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(equivalents);
        ArgumentNullException.ThrowIfNull(diagnostic);
        if (equivalents.Count == 0) return result;

        var byStage = equivalents.GroupBy(e => e.StageIndex).ToDictionary(g => g.Key, g => g.ToList());
        var finalLambda = new Dictionary<int, double>();
        var steps = new List<FemNonlinearStepResult>(result.Steps.Count);
        foreach (var step in result.Steps)
        {
            if (!step.Converged)
            {
                steps.Add(step);
                continue;
            }
            finalLambda[step.StageIndex] = step.LoadFactor;

            // Σ λ_k·p_eq(k, elem) по стадиям 0..s.
            var shift = new Dictionary<int, double[]>();
            foreach (var (stage, list) in byStage)
            {
                if (stage > step.StageIndex) continue;
                double lambda = stage == step.StageIndex ? step.LoadFactor : finalLambda.GetValueOrDefault(stage, 0);
                if (lambda == 0) continue;
                foreach (var e in list)
                {
                    if (!shift.TryGetValue(e.ElementTag, out var s)) shift[e.ElementTag] = s = new double[12];
                    for (int k = 0; k < 6; k++)
                    {
                        s[k] += lambda * e.LocalI[k];
                        s[k + 6] += lambda * e.LocalJ[k];
                    }
                }
            }

            var forces = step.ElementForces.Select(f => shift.TryGetValue(f.ElemTag, out var s)
                ? new FemElementEndForces(f.ElemTag,
                    f.Ni - s[0], f.Qyi - s[1], f.Qzi - s[2], f.Mxi - s[3], f.Myi - s[4], f.Mzi - s[5],
                    f.Nj - s[6], f.Qyj - s[7], f.Qzj - s[8], f.Mxj - s[9], f.Myj - s[10], f.Mzj - s[11])
                : f).ToList();
            steps.Add(step with { ElementForces = forces });
        }

        return new FemNonlinearResult
        {
            Status = result.Status,
            Steps = steps,
            Diagnostics = [.. result.Diagnostics, diagnostic],
            ArtifactDirectory = result.ArtifactDirectory,
            LimitReached = result.LimitReached,
            LastConvergedLoadFactor = result.LastConvergedLoadFactor,
            FailedLoadFactor = result.FailedLoadFactor,
            RefinementDivisions = result.RefinementDivisions,
            CalcTypeName = result.CalcTypeName,
            FiberStateFileName = result.FiberStateFileName,
            SectionOrderFileName = result.SectionOrderFileName,
            StageTags = result.StageTags,
            StagePathControls = result.StagePathControls,
            PathControlSwitches = result.PathControlSwitches,
            StageCompletions = result.StageCompletions,
            MemberLoadsLumped = true
        };
    }
}
