using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Audit;

/// <summary>Уровень достоверности energy audit от наиболее к наименее достоверному.</summary>
public enum ShellEnergyConfidence
{
    /// <summary>Native material/backend вернул проверенный energy response.</summary>
    NativeResponse,

    /// <summary>Energy интегрирована по сопряжённым component pairs.</summary>
    StateIntegral,

    /// <summary>Energy вычислена по внешней работе nodal loads.</summary>
    ExternalWorkOnly,

    /// <summary>Источники energy отсутствуют.</summary>
    Unavailable
}

/// <summary>Точка кривой внешней работы: load factor и WorkDot = Σ(load · displacement).</summary>
public sealed record ShellEnergySample(double LoadFactor, double WorkDot);

/// <summary>Состояние stress/strain одного material layer с интеграционным весом.</summary>
public sealed record ShellMaterialEnergySample(
    IReadOnlyList<double> Stress,
    IReadOnlyList<double> Strain,
    double Weight);

/// <summary>Определяет confidence и вычисляет внешнюю, state-integral и кинематическую работу.</summary>
public static class ShellEnergyAuditor
{
    /// <summary>Определяет confidence по приоритету native response, state integral и load history.</summary>
    public static ShellEnergyConfidence DetermineConfidence(
        bool hasNativeEnergyResponse,
        bool hasStateIntegralData,
        bool hasLoadHistory)
    {
        if (hasNativeEnergyResponse)
            return ShellEnergyConfidence.NativeResponse;
        if (hasStateIntegralData)
            return ShellEnergyConfidence.StateIntegral;
        if (hasLoadHistory)
            return ShellEnergyConfidence.ExternalWorkOnly;
        return ShellEnergyConfidence.Unavailable;
    }

    /// <summary>Интегрирует внешнюю работу по правилу трапеций по коэффициенту нагрузки.</summary>
    public static double ExternalWork(IReadOnlyList<ShellEnergySample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        double work = 0;
        for (int i = 1; i < samples.Count; i++)
        {
            ShellEnergySample previous = samples[i - 1];
            ShellEnergySample current = samples[i];
            work += 0.5 * (previous.WorkDot + current.WorkDot) *
                (current.LoadFactor - previous.LoadFactor);
        }
        return work;
    }

    /// <summary>Интегрирует stress · dStrain только по явно заданным сопряжённым индексам.</summary>
    public static double StateIntegral(
        IReadOnlyList<ShellMaterialEnergySample> samples,
        IReadOnlyList<(int StressIndex, int StrainIndex)> conjugatePairs)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(conjugatePairs);
        if (samples.Count < 2 || conjugatePairs.Count == 0)
            return 0;

        double work = 0;
        for (int i = 1; i < samples.Count; i++)
        {
            ShellMaterialEnergySample previous = samples[i - 1];
            ShellMaterialEnergySample current = samples[i];
            if (!double.IsFinite(current.Weight) || current.Weight < 0)
                throw new ArgumentException("Вес material energy sample должен быть конечным и неотрицательным.", nameof(samples));

            foreach ((int stressIndex, int strainIndex) in conjugatePairs)
            {
                if (stressIndex < 0 || stressIndex >= previous.Stress.Count ||
                    stressIndex >= current.Stress.Count || strainIndex < 0 ||
                    strainIndex >= previous.Strain.Count || strainIndex >= current.Strain.Count)
                    throw new ArgumentException("Индекс сопряжённой stress/strain пары выходит за размерность sample.", nameof(conjugatePairs));

                work += 0.5 * (previous.Stress[stressIndex] + current.Stress[stressIndex]) *
                    (current.Strain[strainIndex] - previous.Strain[strainIndex]) * current.Weight;
            }
        }

        return work;
    }

    /// <summary>Суммирует кинематическую работу реакций по converged steps и узлам.</summary>
    public static double KinematicReactionWork(IReadOnlyList<RCShellStepResult> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        double total = 0;
        foreach (RCShellStepResult step in steps)
        {
            if (!step.Converged)
                continue;
            var displacements = step.Displacements.ToDictionary(displacement => displacement.NodeTag);
            foreach (ShellNodeReaction reaction in step.Reactions)
            {
                if (!displacements.TryGetValue(reaction.NodeTag, out ShellNodeDisplacement? displacement))
                    continue;
                total += reaction.Fx * displacement.Ux + reaction.Fy * displacement.Uy +
                         reaction.Fz * displacement.Uz + reaction.Mx * displacement.Rx +
                         reaction.My * displacement.Ry + reaction.Mz * displacement.Rz;
            }
        }
        return total;
    }
}
