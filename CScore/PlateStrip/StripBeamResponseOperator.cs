using CScore.Fem;

namespace CScore.PlateStrip;

/// <summary>Линейный оператор отклика производной балки: A·c + S₀ — внутренние усилия на
/// станциях от нагрузки с коэффициентами c и от известных концевых усилий.</summary>
/// <param name="IsCalculable">Ложь, если хотя бы один прогон балки не сошёлся.</param>
/// <param name="Diagnostics">Диагностики прогонов.</param>
/// <param name="Response">A: 3n строк (блоками по станциям: N, My, Mz) × число коэффициентов.</param>
/// <param name="BaseResponse">S₀: отклик на KnownEndActions, 3n значений.</param>
/// <param name="Constraints">G: 5 строк (Fx, Fy, Fz, My, Mz) × число коэффициентов.</param>
/// <param name="Smoothing">L: оператор сглаживания, строки × число коэффициентов.</param>
public sealed record StripBeamResponseOperator(
    bool IsCalculable,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics,
    double[,] Response,
    double[] BaseResponse,
    double[,] Constraints,
    double[,] Smoothing);

/// <summary>Сборка оператора отклика полосы: столбцы A получаются прогонами балочной задачи на
/// единичных базисных нагрузках (линейность задачи это допускает), строки G — равнодействующими
/// тех же единичных нагрузок через StripLoadConsistentNodalProjection, оператор L — разностями с
/// учётом неравномерности станций.</summary>
public static class StripBeamResponseOperatorBuilder
{
    public static StripBeamResponseOperator Build(
        double[,] sectionTangent,
        double lengthM,
        LoadBasisLayout layout,
        StripBeamSupportScheme scheme,
        LoadRecoveryMode mode,
        KnownEndActions? endActions)
    {
        ArgumentNullException.ThrowIfNull(sectionTangent);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(scheme);

        var stations = layout.StationFractions;
        int rowCount = stations.Count * 3;
        int columnCount = layout.CoefficientCount;

        var diagnostics = new List<FemValidationDiagnostic>();
        var a = new double[rowCount, columnCount];

        for (int column = 0; column < columnCount; column++)
        {
            var unit = new StripLoadSet(layout.UnitLoads(column));
            var solve = StripBeamModel.Solve(sectionTangent, lengthM, stations, scheme, unit);
            if (!solve.IsCalculable)
            {
                diagnostics.AddRange(solve.Diagnostics);
                return Failed(diagnostics, rowCount, columnCount);
            }

            for (int station = 0; station < stations.Count; station++)
            for (int component = 0; component < 3; component++)
                a[3 * station + component, column] = solve.StationResultants[station][component];
        }

        var baseResponse = new double[rowCount];
        if (endActions != null && !endActions.IsZero)
        {
            var solve = StripBeamModel.Solve(
                sectionTangent, lengthM, stations, scheme, new StripLoadSet([]), endActions);
            if (!solve.IsCalculable)
            {
                diagnostics.AddRange(solve.Diagnostics);
                return Failed(diagnostics, rowCount, columnCount);
            }

            for (int station = 0; station < stations.Count; station++)
            for (int component = 0; component < 3; component++)
                baseResponse[3 * station + component] = solve.StationResultants[station][component];
        }

        var g = BuildConstraints(lengthM, layout, diagnostics);
        if (g == null)
            return Failed(diagnostics, rowCount, columnCount);

        var l = BuildSmoothing(lengthM, layout, mode);
        return new(true, diagnostics, a, baseResponse, g, l);
    }

    /// <summary>Строки равнодействующей (Fx, Fy, Fz, My, Mz) для каждой базисной функции —
    /// через тот же Project, что даёт EquilibriumTarget, поэтому ограничения и целевое значение
    /// заведомо в одной конвенции.</summary>
    static double[,]? BuildConstraints(
        double lengthM, LoadBasisLayout layout, List<FemValidationDiagnostic> diagnostics)
    {
        var g = new double[5, layout.CoefficientCount];
        for (int column = 0; column < layout.CoefficientCount; column++)
        {
            var unit = new StripLoadSet(layout.UnitLoads(column));
            var projection = StripLoadConsistentNodalProjection.Project(
                unit, lengthM, layout.StationFractions);
            if (!projection.IsCalculable)
            {
                diagnostics.AddRange(projection.Diagnostics);
                return null;
            }

            g[0, column] = projection.TotalForceCheck[0];
            g[1, column] = projection.TotalForceCheck[1];
            g[2, column] = projection.TotalForceCheck[2];
            g[3, column] = projection.TotalMomentCheck[1];
            g[4, column] = projection.TotalMomentCheck[2];
        }
        return g;
    }

    /// <summary>Оператор сглаживания: первая разность для WeakEquilibriumProjection, вторая —
    /// для ConstrainedSmoothFit, пустой для остальных режимов. Обе учитывают физический шаг
    /// между положениями коэффициентов, иначе на неравномерной сетке это не производная.</summary>
    static double[,] BuildSmoothing(double lengthM, LoadBasisLayout layout, LoadRecoveryMode mode)
    {
        int m = layout.FunctionsPerComponent;
        var positions = layout.CoefficientPositions();

        int rowsPerComponent = mode switch
        {
            LoadRecoveryMode.WeakEquilibriumProjection => Math.Max(0, m - 1),
            LoadRecoveryMode.ConstrainedSmoothFit => Math.Max(0, m - 2),
            _ => 0
        };
        if (rowsPerComponent == 0)
            return new double[0, layout.CoefficientCount];

        var l = new double[3 * rowsPerComponent, layout.CoefficientCount];
        for (int component = 0; component < 3; component++)
        {
            int offset = layout.ComponentOffset(component);
            int rowOffset = component * rowsPerComponent;

            if (mode == LoadRecoveryMode.WeakEquilibriumProjection)
            {
                for (int i = 0; i < rowsPerComponent; i++)
                {
                    double h = (positions[i + 1] - positions[i]) * lengthM;
                    l[rowOffset + i, offset + i] = -1.0 / h;
                    l[rowOffset + i, offset + i + 1] = 1.0 / h;
                }
            }
            else
            {
                for (int i = 0; i < rowsPerComponent; i++)
                {
                    double h1 = (positions[i + 1] - positions[i]) * lengthM;
                    double h2 = (positions[i + 2] - positions[i + 1]) * lengthM;
                    double scale = 2.0 / (h1 + h2);
                    l[rowOffset + i, offset + i] = scale / h1;
                    l[rowOffset + i, offset + i + 1] = -scale * (1.0 / h1 + 1.0 / h2);
                    l[rowOffset + i, offset + i + 2] = scale / h2;
                }
            }
        }
        return l;
    }

    static StripBeamResponseOperator Failed(
        List<FemValidationDiagnostic> diagnostics, int rowCount, int columnCount) =>
        new(false, diagnostics, new double[rowCount, columnCount], new double[rowCount],
            new double[5, columnCount], new double[0, columnCount]);
}
