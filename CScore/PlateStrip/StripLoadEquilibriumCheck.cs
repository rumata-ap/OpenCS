using CScore.Fem;
using CScore.Planar;

namespace CScore.PlateStrip;

/// <summary>Результат проверки полноты переноса нагрузки на полосу.</summary>
/// <param name="IsCalculable">Ложь при расхождении сверх допуска.</param>
/// <param name="Diagnostics">Диагностики проверки.</param>
/// <param name="ForceKn">Равнодействующая набора StripLoad в осях полосы, кН.</param>
/// <param name="MomentAboutStartKnM">Момент этой равнодействующей относительно начала полосы.</param>
public sealed record StripLoadEquilibriumResult(
    bool IsCalculable,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics,
    PlanarVector3 ForceKn,
    double MomentAboutStartKnM);

/// <summary>
/// Проверка полноты переноса нагрузки на полосу (Срез 7, раздел B.3 спеки).
///
/// Отдельный тип обязателен: StripLoadMapper.Map переносит <b>одну</b> нагрузку и не принимает
/// ни набора, ни опорной схемы, а приватный AccumulateTotals внутри
/// StripLoadConsistentNodalProjection проверяет самосогласованность проекции, а не полноту
/// переноса.
///
/// <b>Осознанное отступление от исходной формулировки спеки.</b> Сверять сумму действий «с
/// реакциями опорной схемы» бессодержательно: в конечно-элементной постановке реакции получаются
/// как K·u − f и удовлетворяют равновесию тождественно, какой бы неполной ни была перенесённая
/// нагрузка — такая проверка не может упасть. Содержательна другая сверка: равнодействующая
/// перенесённого набора против <b>ожидаемой</b> равнодействующей, которую знает вызывающая
/// сторона (например, q·b·L поверхностной нагрузки плюс объявленные краевые действия). Именно
/// потерю или задвоение нагрузки при переносе она и ловит.
/// </summary>
public static class StripLoadEquilibriumCheck
{
    /// <summary>Равнодействующая набора StripLoad и её момент относительно начала полосы.
    /// Распределённые нагрузки интегрируются аналитически по своим участкам.</summary>
    public static (PlanarVector3 Force, double MomentAboutStart) Resultant(
        StripLoadSet loads, double lengthM)
    {
        ArgumentNullException.ThrowIfNull(loads);
        if (!(lengthM > 0.0) || !double.IsFinite(lengthM))
            throw new ArgumentOutOfRangeException(nameof(lengthM),
                "Длина полосы должна быть конечной и положительной.");

        double fx = 0.0, fy = 0.0, fz = 0.0, moment = 0.0;
        foreach (var load in loads.Loads)
        {
            if (load == null) continue;
            if (!load.IsDistributed)
            {
                fx += load.PxKn;
                fy += load.PyKn;
                fz += load.PzKn;
                moment += load.PzKn * load.StationFraction * lengthM;
                continue;
            }

            double a = load.StationStartFraction, b = load.StationEndFraction;
            double span = (b - a) * lengthM;
            if (span <= 0.0) continue;

            var (qx0, qy0, qz0) = load.IntensityAt(a);
            var (qx1, qy1, qz1) = load.IntensityAt(b);

            fx += 0.5 * (qx0 + qx1) * span;
            fy += 0.5 * (qy0 + qy1) * span;
            fz += 0.5 * (qz0 + qz1) * span;

            // ∫ q(s)·x ds по трапецеидальной интенсивности на участке [a·L, b·L].
            double x0 = a * lengthM, x1 = b * lengthM;
            moment += span * (qz0 * (2.0 * x0 + x1) + qz1 * (x0 + 2.0 * x1)) / 6.0;
        }
        return (new PlanarVector3(fx, fy, fz), moment);
    }

    /// <summary>Сверить перенесённый набор с ожидаемой равнодействующей.</summary>
    /// <param name="loads">Перенесённые нагрузки полосы.</param>
    /// <param name="lengthM">Длина полосы.</param>
    /// <param name="expectedForceKn">Ожидаемая равнодействующая, кН.</param>
    /// <param name="expectedMomentAboutStartKnM">Ожидаемый момент относительно начала, кН·м;
    /// null — не проверять.</param>
    /// <param name="relativeTolerance">Относительный допуск.</param>
    /// <param name="absoluteToleranceKn">Абсолютный пол допуска, кН.</param>
    public static StripLoadEquilibriumResult Run(
        StripLoadSet loads,
        double lengthM,
        PlanarVector3 expectedForceKn,
        double? expectedMomentAboutStartKnM = null,
        double relativeTolerance = 1e-9,
        double absoluteToleranceKn = 1e-9)
    {
        var (force, moment) = Resultant(loads, lengthM);
        var diagnostics = new List<FemValidationDiagnostic>();

        Compare("Fx", force.X, expectedForceKn.X);
        Compare("Fy", force.Y, expectedForceKn.Y);
        Compare("Fz", force.Z, expectedForceKn.Z);
        if (expectedMomentAboutStartKnM is { } expectedMoment)
            Compare("My", moment, expectedMoment);

        return new(diagnostics.All(d => !d.IsError), diagnostics, force, moment);

        void Compare(string component, double actual, double expected)
        {
            double tolerance = Math.Max(absoluteToleranceKn,
                relativeTolerance * Math.Max(Math.Abs(expected), Math.Abs(actual)));
            if (Math.Abs(actual - expected) <= tolerance) return;
            diagnostics.Add(new("plate_strip_boundary_equilibrium_mismatch",
                $"Перенос нагрузки неполон по компоненте {component}: перенесено {actual:G6}, " +
                $"ожидалось {expected:G6} (допуск {tolerance:G6})."));
        }
    }
}
