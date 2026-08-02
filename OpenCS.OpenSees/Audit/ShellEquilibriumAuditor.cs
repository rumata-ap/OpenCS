using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Audit;

/// <summary>Отчёт равновесия одного шага по шести компонентам force/moment.</summary>
public sealed record ShellEquilibriumStepReport(
    int StepIndex,
    int StageIndex,
    double LoadFactor,
    ShellResultant Applied,
    ShellResultant Reaction,
    ShellResultant Residual,
    double AbsoluteError,
    double RelativeError,
    bool Pass);

/// <summary>Проверяет глобальное равновесие сил и моментов с учётом координат узлов.</summary>
public static class ShellEquilibriumAuditor
{
    /// <summary>Восстанавливает applied resultant шага: завершённые стадии умножаются на
    /// MaxLoadFactor, текущая стадия — на текущий коэффициент нагрузки.</summary>
    public static ShellResultant AppliedResultantAtStep(
        IReadOnlyList<ShellNonlinearStage> stages,
        int stageIndex,
        double loadFactor,
        IReadOnlyDictionary<int, NormalizedShellNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentNullException.ThrowIfNull(nodes);

        ShellResultant total = ShellResultant.Zero;
        for (int i = 0; i < stages.Count && i < stageIndex; i++)
            total += StageResultant(stages[i], nodes) * stages[i].MaxLoadFactor;
        if (stageIndex >= 0 && stageIndex < stages.Count)
            total += StageResultant(stages[stageIndex], nodes) * loadFactor;
        return total;
    }

    /// <summary>Вычисляет суммарный resultant узловых нагрузок одной стадии.</summary>
    private static ShellResultant StageResultant(
        ShellNonlinearStage stage,
        IReadOnlyDictionary<int, NormalizedShellNode> nodes)
    {
        ShellResultant sum = ShellResultant.Zero;
        foreach (ShellNodalLoad load in stage.Loads)
        {
            NormalizedShellNode node = nodes[load.NodeTag];
            sum += ShellResultantMath.NodalForce(node.X, node.Y, node.Z,
                new ShellResultant(load.Fx, load.Fy, load.Fz, load.Mx, load.My, load.Mz));
        }
        return sum;
    }

    /// <summary>Вычисляет суммарный resultant реакций относительно глобального начала.</summary>
    public static ShellResultant ReactionResultant(
        IReadOnlyList<ShellNodeReaction> reactions,
        IReadOnlyDictionary<int, NormalizedShellNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(reactions);
        ArgumentNullException.ThrowIfNull(nodes);

        ShellResultant sum = ShellResultant.Zero;
        foreach (ShellNodeReaction reaction in reactions)
        {
            NormalizedShellNode node = nodes[reaction.NodeTag];
            sum += ShellResultantMath.NodalForce(node.X, node.Y, node.Z,
                new ShellResultant(reaction.Fx, reaction.Fy, reaction.Fz,
                    reaction.Mx, reaction.My, reaction.Mz));
        }
        return sum;
    }

    /// <summary>Вычисляет residual applied + reaction и проверяет policy tolerances.</summary>
    public static ShellEquilibriumStepReport Evaluate(
        int stepIndex,
        int stageIndex,
        double loadFactor,
        ShellResultant applied,
        ShellResultant reaction,
        ShellAuditPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(applied);
        ArgumentNullException.ThrowIfNull(reaction);
        ArgumentNullException.ThrowIfNull(policy);

        ShellResultant residual = applied + reaction;
        double absolute = residual.MaxAbsoluteComponent;
        double scale = Math.Max(1.0, applied.MaxAbsoluteComponent);
        double relative = absolute / scale;
        bool pass = absolute <= policy.AbsoluteEquilibriumTolerance ||
                    relative <= policy.RelativeEquilibriumTolerance;

        return new ShellEquilibriumStepReport(
            stepIndex, stageIndex, loadFactor, applied, reaction, residual,
            absolute, relative, pass);
    }
}
