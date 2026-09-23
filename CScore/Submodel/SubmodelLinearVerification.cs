using CScore.Fem;

namespace CScore.Submodel;

/// <summary>Допуски линейной сверки: относительный к масштабу вида величины и абсолютные пороги.</summary>
public sealed record VerificationTolerances(
    double RelTol = 1e-6,
    double AbsTranslation = 1e-9,
    double AbsRotation = 1e-9,
    double AbsForce = 1e-6,
    double AbsMoment = 1e-6);

/// <summary>Максимальное расхождение одного вида: значение, масштаб вида, тег узла/КЭ и DOF (0..5; −1 — нет данных).</summary>
public sealed record MaxDeviation(double Value, double Scale, string Tag, int Dof)
{
    public static readonly MaxDeviation None = new(0, 0, "", -1);
}

/// <summary>Отчёт сверки линейного расчёта субмодели с родителем при λ = 1.</summary>
public sealed record SubmodelVerificationReport(
    MaxDeviation Translation,
    MaxDeviation Rotation,
    MaxDeviation EndForce,
    MaxDeviation EndMoment,
    MaxDeviation ReactionForce,
    MaxDeviation ReactionMoment,
    bool Passed,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>
/// Сверка линейного результата материализованной субмодели с линейным результатом родителя.
/// Необязательный <c>lambda</c> масштабирует эталон (перемещения, усилия родителя, <c>p_control</c> и
/// приложенные силы) — для сверки состояния нелинейного расчёта при λ ≠ 1 (срез 4b).
/// Теги mesh-узлов и КЭ ребёнка совпадают с родительскими, поэтому сопоставление прямое.
/// Реакции ребёнка во всех DOF с <c>sp</c>: <c>R_child = p_control − p_applied</c>, где
/// <c>p_applied</c> — сила, приложенная в этом DOF (для gauge — граничная, иначе 0); для gauge-DOF это
/// невязка равновесия. Реакцию родителя напрямую сравнивать нельзя: в неё входят отброшенные стержни.
/// </summary>
public static class SubmodelLinearVerification
{
    public static SubmodelVerificationReport Compare(SubmodelExtraction extraction, BoundaryScenario scenario,
        SubmodelMaterializationSummary summary, IParentLinearResult parent, IParentLinearResult child,
        VerificationTolerances? tolerances = null, double lambda = 1)
    {
        if (!double.IsFinite(lambda) || lambda <= 0)
            throw new ArgumentOutOfRangeException(nameof(lambda), lambda, "Коэффициент λ должен быть конечным и положительным.");
        var tol = tolerances ?? new VerificationTolerances();
        var diagnostics = new List<FemValidationDiagnostic>();
        void Info(string message) => diagnostics.Add(new(SubmodelMaterializationDiagnostics.Info, message, false, []));

        // Перемещения.
        var translation = new Tracker();
        var rotation = new Tracker();
        foreach (var node in extraction.Nodes)
        {
            string tag = node.SubmodelNodeTag;
            if (!parent.TryGetDisplacement(tag, out var up) || !child.TryGetDisplacement(tag, out var uc))
            {
                Info($"Узел {tag}: нет перемещений у родителя или субмодели — не сверяется.");
                continue;
            }
            for (int dof = 0; dof < 6; dof++)
            {
                double reference = lambda * up[dof];
                (dof < 3 ? translation : rotation).Add(Math.Abs(uc[dof] - reference), Math.Abs(reference), tag, dof);
            }
        }

        // Концевые усилия выбранных КЭ.
        var endForce = new Tracker();
        var endMoment = new Tracker();
        foreach (var segment in extraction.Segments)
        {
            string tag = segment.SubmodelElementTag;
            if (!parent.TryGetEndForces(tag, out var fp) || !child.TryGetEndForces(tag, out var fc))
            {
                Info($"КЭ {tag}: нет концевых усилий у родителя или субмодели — не сверяется.");
                continue;
            }
            foreach (var (p, c) in new[] { (fp.I, fc.I), (fp.J, fc.J) })
                for (int dof = 0; dof < 6; dof++)
                {
                    double reference = lambda * p[dof];
                    (dof < 3 ? endForce : endMoment).Add(Math.Abs(c[dof] - reference), Math.Abs(reference), tag, dof);
                }
        }

        // Реакции во всех DOF с sp.
        var reactionForce = new Tracker();
        var reactionMoment = new Tracker();
        foreach (var end in scenario.Ends)
        {
            var gaugeDofs = summary.GaugeDofs.Where(g => g.AtStart == end.AtStart).Select(g => g.Dof).ToHashSet();
            var constrained = Enumerable.Range(0, 6)
                .Where(d => end.Dofs[d].Mode is DofMode.Fixed or DofMode.Kinematic || gaugeDofs.Contains(d)).ToList();
            if (constrained.Count == 0) continue;
            if (end.Control.PControl is not { } control)
            {
                Info($"Конец {end.ChildNodeTag}: нет контрольного остатка — реакции не сверяются.");
                continue;
            }
            if (!child.TryGetReaction(end.ChildNodeTag, out var reaction))
            {
                Info($"Конец {end.ChildNodeTag}: у субмодели нет реакций — не сверяются.");
                continue;
            }
            foreach (int dof in constrained)
            {
                double applied = gaugeDofs.Contains(dof) ? end.Dofs[dof].Value ?? 0 : 0;
                double expected = lambda * (control[dof] - applied);
                (dof < 3 ? reactionForce : reactionMoment).Add(Math.Abs(reaction[dof] - expected), Math.Abs(lambda * control[dof]), end.ChildNodeTag, dof);
            }
        }

        var kinds = new (string Name, Tracker Tracker, double Abs)[]
        {
            ("перемещения", translation, tol.AbsTranslation), ("повороты", rotation, tol.AbsRotation),
            ("концевые силы", endForce, tol.AbsForce), ("концевые моменты", endMoment, tol.AbsMoment),
            ("реакции (силы)", reactionForce, tol.AbsForce), ("реакции (моменты)", reactionMoment, tol.AbsMoment),
        };
        bool passed = true;
        foreach (var (name, tracker, abs) in kinds)
        {
            var max = tracker.Result();
            double limit = Math.Max(tol.RelTol * max.Scale, abs);
            if (max.Value <= limit) continue;
            passed = false;
            diagnostics.Add(new(SubmodelMaterializationDiagnostics.VerificationMismatch,
                $"Сверка с родителем, {name}: расхождение {max.Value:G6} в {max.Tag} DOF {max.Dof + 1} " +
                $"превышает допуск {limit:G6} (масштаб {max.Scale:G6}).", false, [max.Tag]));
        }

        return new SubmodelVerificationReport(translation.Result(), rotation.Result(), endForce.Result(), endMoment.Result(),
            reactionForce.Result(), reactionMoment.Result(), passed, diagnostics);
    }

    /// <summary>Максимум расхождения и масштаб (максимум модуля эталонной величины) одного вида.</summary>
    sealed class Tracker
    {
        double _value = -1, _scale;
        string _tag = "";
        int _dof = -1;

        public void Add(double deviation, double reference, string tag, int dof)
        {
            _scale = Math.Max(_scale, reference);
            if (deviation <= _value) return;
            _value = deviation; _tag = tag; _dof = dof;
        }

        public MaxDeviation Result() => _dof < 0 ? MaxDeviation.None with { Scale = _scale } : new(_value, _scale, _tag, _dof);
    }
}
