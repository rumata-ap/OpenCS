using CScore.Fem;

namespace CScore.PlateStrip;

/// <summary>Целевая эпюра внутренних усилий полосы: значения [N, My, Mz] на станциях.
/// Производится вызывающей стороной (обычно StripResultantIntegrator Среза 3a по реальным
/// shell-результатам); станции безразмерны, физическая длина в этот тип не входит — её
/// единственный источник задан в спеке Среза 6 («Длина полосы»).
///
/// Поперечных сил в контракте нет: они не выводятся из B^T·σ, поэтому и равнодействующая
/// внешней нагрузки из этой эпюры не восстанавливается (см. EquilibriumTarget).</summary>
public sealed class TargetBeamResultants
{
    public required IReadOnlyList<double> StationFractions { get; init; }
    public required IReadOnlyList<double> N { get; init; }
    public required IReadOnlyList<double> My { get; init; }
    public required IReadOnlyList<double> Mz { get; init; }

    public int StationCount => StationFractions.Count;

    /// <summary>Проверяет станции и значения; коды диагностик — plate_strip_load_recovery_*.</summary>
    public IReadOnlyList<FemValidationDiagnostic> Validate()
    {
        var diagnostics = new List<FemValidationDiagnostic>();

        if (StationFractions.Count < 3)
        {
            diagnostics.Add(new("plate_strip_load_recovery_invalid_target",
                "Целевая эпюра должна содержать не менее трёх станций."));
            return diagnostics;
        }

        if (N.Count != StationFractions.Count || My.Count != StationFractions.Count ||
            Mz.Count != StationFractions.Count)
        {
            diagnostics.Add(new("plate_strip_load_recovery_invalid_target",
                "Длины массивов N/My/Mz должны совпадать с числом станций."));
            return diagnostics;
        }

        if (Math.Abs(StationFractions[0]) > 1e-12 || Math.Abs(StationFractions[^1] - 1.0) > 1e-12)
            diagnostics.Add(new("plate_strip_load_recovery_invalid_target",
                "Станции должны начинаться с 0.0 и заканчиваться 1.0."));

        for (int i = 1; i < StationFractions.Count; i++)
            if (!double.IsFinite(StationFractions[i]) || StationFractions[i] <= StationFractions[i - 1])
            {
                diagnostics.Add(new("plate_strip_load_recovery_invalid_target",
                    "Станции должны быть конечными и строго возрастающими."));
                break;
            }

        for (int i = 0; i < StationFractions.Count; i++)
            if (!double.IsFinite(N[i]) || !double.IsFinite(My[i]) || !double.IsFinite(Mz[i]))
            {
                diagnostics.Add(new("plate_strip_load_recovery_invalid_target",
                    "Значения целевой эпюры должны быть конечными."));
                break;
            }

        return diagnostics;
    }

    /// <summary>Значения эпюры одной строкой в порядке сборки отклика: блоками по станциям,
    /// внутри станции — N, My, Mz.</summary>
    public double[] ToRowVector()
    {
        var result = new double[StationFractions.Count * 3];
        for (int i = 0; i < StationFractions.Count; i++)
        {
            result[3 * i] = N[i];
            result[3 * i + 1] = My[i];
            result[3 * i + 2] = Mz[i];
        }
        return result;
    }
}
