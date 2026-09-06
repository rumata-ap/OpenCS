using CScore.Fem;

namespace CScore.PlateStrip;

/// <summary>Восстановление внешней распределённой нагрузки производной балки по целевой эпюре
/// внутренних усилий полосы — слабой формой равновесия, без поточечного дифференцирования. См.
/// docs/superpowers/specs/2026-09-06-plate-strip-equivalent-beam-load-recovery-design.md.
///
/// Постановка: minimize ‖W·(A·c + S₀ − S_target)‖² + α_eff·‖L·c‖² при ограничениях G·c = g
/// (сохранение равнодействующей силы и момента), решаемая через KKT-систему.</summary>
public static class EquivalentBeamLoadRecovery
{
    /// <summary>Абсолютный пол масштабов: не даёт делить на ноль при тождественно нулевой
    /// целевой эпюре и делает критерий точности абсолютным там, где относительный не определён.</summary>
    public const double AbsoluteScaleFloor = 1e-9;

    public static RecoveredBeamLoadSet Recover(
        EquivalentSection? section,
        TargetBeamResultants target,
        StripBeamSupportScheme scheme,
        LoadRecoveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(scheme);
        ArgumentNullException.ThrowIfNull(options);

        var diagnostics = new List<FemValidationDiagnostic>();
        diagnostics.AddRange(target.Validate());
        diagnostics.AddRange(options.Validate(target.StationCount));

        double lengthM = ValidateSection(section, diagnostics);
        var raw = lengthM > 0.0 && !diagnostics.Any(d => d.Code == "plate_strip_load_recovery_invalid_target")
            ? RawDerivativeResult.Compute(target, lengthM)
            : RawDerivativeResult.Undefined(target.StationFractions);

        string fingerprint = LoadRecoveryFingerprint.Compute(
            target, lengthM, section?.ResultFingerprint ?? "", scheme, options);

        if (diagnostics.Any(d => d.IsError))
            return Failed(target, raw, options, diagnostics, fingerprint);

        if (options.Mode == LoadRecoveryMode.RawDerivativeDiagnostic)
        {
            diagnostics.Add(new("plate_strip_load_recovery_raw_derivative_not_design_load",
                "Режим RawDerivativeDiagnostic даёт только сравнительную эпюру нагрузки; " +
                "расчётной нагрузкой её результат не является.", false));
            return new(section!.Strip.Id, options.Mode, options.EffectiveBasis, target, raw,
                [], [], [], new double[5],
                new LoadRecoveryApproximationError(0.0, 0.0, new double[target.StationCount * 3]),
                true, diagnostics, fingerprint);
        }

        var layout = new LoadBasisLayout(options.EffectiveBasis, target.StationFractions);
        var responseOperator = StripBeamResponseOperatorBuilder.Build(
            section!.BeamTangent, lengthM, layout, scheme, options.Mode, options.KnownEndActions);
        diagnostics.AddRange(responseOperator.Diagnostics);
        if (!responseOperator.IsCalculable)
            return Failed(target, raw, options, diagnostics, fingerprint);

        if (!TrySolve(target, options, layout, responseOperator, lengthM, out double[] coefficients))
        {
            diagnostics.Add(new("plate_strip_load_recovery_singular_system",
                "Система восстановления нагрузки вырождена."));
            return Failed(target, raw, options, diagnostics, fingerprint);
        }

        var residual = ComputeStationResidual(target, responseOperator, coefficients);
        var error = BuildApproximationError(target, residual);
        var equilibriumResidual = ComputeEquilibriumResidual(responseOperator, coefficients, options);

        AddResultDiagnostics(target, scheme, options, raw, coefficients, error,
            equilibriumResidual, diagnostics);

        var loads = layout.ToLoads(coefficients, "recovered");
        var projection = StripLoadConsistentNodalProjection.Project(
            new StripLoadSet(loads), lengthM, target.StationFractions);
        diagnostics.AddRange(projection.Diagnostics);

        return new(section.Strip.Id, options.Mode, options.EffectiveBasis, target, raw,
            coefficients, loads, projection.Elements, equilibriumResidual, error,
            diagnostics.All(d => !d.IsError), diagnostics, fingerprint);
    }

    /// <summary>Проверяет эквивалентное сечение и возвращает длину полосы. Длина берётся только
    /// из Strip.Geometry.LengthM: два источника одной величины неизбежно разошлись бы.</summary>
    static double ValidateSection(EquivalentSection? section, List<FemValidationDiagnostic> diagnostics)
    {
        if (section == null)
        {
            diagnostics.Add(new("plate_strip_load_recovery_invalid_section",
                "Эквивалентное сечение не задано."));
            return 0.0;
        }

        if (!section.IsCalculable || section.IsStale)
            diagnostics.Add(new("plate_strip_load_recovery_invalid_section",
                "Эквивалентное сечение не рассчитано или устарело."));

        bool allZero = true;
        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
        {
            if (!double.IsFinite(section.BeamTangent[i, j]))
            {
                diagnostics.Add(new("plate_strip_load_recovery_invalid_section",
                    "Матрица жёсткости сечения должна быть конечной."));
                return 0.0;
            }
            if (section.BeamTangent[i, j] != 0.0)
                allZero = false;
        }
        if (allZero)
            diagnostics.Add(new("plate_strip_load_recovery_invalid_section",
                "Матрица жёсткости сечения вырождена."));

        double lengthM = section.Strip.Geometry.LengthM;
        if (!(lengthM > 0.0) || !double.IsFinite(lengthM))
        {
            diagnostics.Add(new("plate_strip_load_recovery_invalid_length",
                "Длина полосы (Strip.Geometry.LengthM) должна быть конечной и положительной."));
            return 0.0;
        }
        return lengthM;
    }

    /// <summary>Собирает и решает KKT-систему. Строки нормируются на характерные масштабы
    /// компонент (нормируются строки, не столбцы, поэтому коэффициенты остаются физическими).</summary>
    static bool TrySolve(
        TargetBeamResultants target,
        LoadRecoveryOptions options,
        LoadBasisLayout layout,
        StripBeamResponseOperator responseOperator,
        double lengthM,
        out double[] coefficients)
    {
        int stationCount = target.StationCount;
        int rows = stationCount * 3;
        int cols = layout.CoefficientCount;
        var targetVector = target.ToRowVector();

        // Масштаб блока строк берётся по максимуму из целевых значений И самого оператора.
        // Только по цели нельзя: при тождественно нулевой компоненте (например, эпюра без
        // продольной силы) масштаб упал бы на абсолютный пол и раздул её строки на порядки,
        // а разброс масштабов в KKT съедает относительный порог ведущего элемента.
        double scaleN = BlockScale(target.N, responseOperator, rows, cols, component: 0);
        double scaleM = Math.Max(
            BlockScale(target.My, responseOperator, rows, cols, component: 1),
            BlockScale(target.Mz, responseOperator, rows, cols, component: 2));

        var aw = new double[rows, cols];
        var sw = new double[rows];
        for (int row = 0; row < rows; row++)
        {
            int station = row / 3;
            double scale = row % 3 == 0 ? scaleN : scaleM;
            double weight = (options.StationWeights?[station] ?? 1.0) / scale;

            sw[row] = weight * (targetVector[row] - responseOperator.BaseResponse[row]);
            for (int col = 0; col < cols; col++)
                aw[row, col] = weight * responseOperator.Response[row, col];
        }

        var h = new double[cols, cols];
        var rhs = new double[cols];
        for (int i = 0; i < cols; i++)
        {
            double sum = 0.0;
            for (int row = 0; row < rows; row++)
                sum += aw[row, i] * sw[row];
            rhs[i] = 2.0 * sum;

            for (int j = 0; j < cols; j++)
            {
                double value = 0.0;
                for (int row = 0; row < rows; row++)
                    value += aw[row, i] * aw[row, j];
                h[i, j] = 2.0 * value;
            }
        }

        var l = responseOperator.Smoothing;
        int smoothingRows = l.GetLength(0);
        if (smoothingRows > 0 && options.EffectiveAlpha > 0.0)
        {
            var ltl = new double[cols, cols];
            for (int i = 0; i < cols; i++)
            for (int j = 0; j < cols; j++)
            {
                double value = 0.0;
                for (int row = 0; row < smoothingRows; row++)
                    value += l[row, i] * l[row, j];
                ltl[i, j] = value;
            }

            // α безразмерна: приводим её к масштабу H, иначе значение несопоставимо между задачами.
            double alphaEffective = options.EffectiveAlpha *
                FrobeniusNorm(h, cols) / Math.Max(FrobeniusNorm(ltl, cols), AbsoluteScaleFloor);
            for (int i = 0; i < cols; i++)
            for (int j = 0; j < cols; j++)
                h[i, j] += 2.0 * alphaEffective * ltl[i, j];
        }

        var equilibrium = options.UsesEquilibriumConstraints ? options.EquilibriumTarget : null;
        int constraintRows = equilibrium == null ? 0 : 5;
        int size = cols + constraintRows;

        var kkt = new double[size, size];
        var kktRhs = new double[size];
        for (int i = 0; i < cols; i++)
        {
            kktRhs[i] = rhs[i];
            for (int j = 0; j < cols; j++)
                kkt[i, j] = h[i, j];
        }

        if (equilibrium != null)
        {
            var g = equilibrium.ToArray();
            for (int r = 0; r < 5; r++)
            {
                // Каждая строка ограничений нормируется по своему масштабу (силы и моменты
                // различаются на множитель длины) — по максимуму из правой части и самой строки.
                double rowMax = 0.0;
                for (int j = 0; j < cols; j++)
                    rowMax = Math.Max(rowMax, Math.Abs(responseOperator.Constraints[r, j]));
                double scale = Math.Max(Math.Max(rowMax, Math.Abs(g[r])), AbsoluteScaleFloor);

                kktRhs[cols + r] = g[r] / scale;
                for (int j = 0; j < cols; j++)
                {
                    double value = responseOperator.Constraints[r, j] / scale;
                    kkt[cols + r, j] = value;
                    kkt[j, cols + r] = value;
                }
            }
        }

        if (!DensePivotSolver.Solve(kkt, kktRhs, out double[] solution))
        {
            coefficients = new double[cols];
            return false;
        }

        coefficients = solution[..cols];
        return true;
    }

    /// <summary>Характерный масштаб блока строк одной компоненты: максимум из целевых значений,
    /// строк оператора отклика и базового отклика на концевые усилия.</summary>
    static double BlockScale(
        IReadOnlyList<double> targetComponent,
        StripBeamResponseOperator responseOperator,
        int rows, int cols, int component)
    {
        double scale = MaxAbs(targetComponent);
        for (int row = component; row < rows; row += 3)
        {
            scale = Math.Max(scale, Math.Abs(responseOperator.BaseResponse[row]));
            for (int col = 0; col < cols; col++)
                scale = Math.Max(scale, Math.Abs(responseOperator.Response[row, col]));
        }
        return Math.Max(scale, AbsoluteScaleFloor);
    }

    static double[] ComputeStationResidual(
        TargetBeamResultants target, StripBeamResponseOperator responseOperator, double[] coefficients)
    {
        var targetVector = target.ToRowVector();
        int rows = targetVector.Length;
        var residual = new double[rows];
        for (int row = 0; row < rows; row++)
        {
            double value = responseOperator.BaseResponse[row];
            for (int col = 0; col < coefficients.Length; col++)
                value += responseOperator.Response[row, col] * coefficients[col];
            residual[row] = value - targetVector[row];
        }
        return residual;
    }

    static LoadRecoveryApproximationError BuildApproximationError(
        TargetBeamResultants target, double[] residual)
    {
        double absolute = Norm(residual);
        double targetNorm = Norm(target.ToRowVector());
        return new(absolute / Math.Max(targetNorm, AbsoluteScaleFloor), absolute, residual);
    }

    static double[] ComputeEquilibriumResidual(
        StripBeamResponseOperator responseOperator, double[] coefficients, LoadRecoveryOptions options)
    {
        var residual = new double[5];
        var equilibrium = options.UsesEquilibriumConstraints ? options.EquilibriumTarget : null;
        var g = equilibrium?.ToArray();

        for (int r = 0; r < 5; r++)
        {
            double value = 0.0;
            for (int col = 0; col < coefficients.Length; col++)
                value += responseOperator.Constraints[r, col] * coefficients[col];
            residual[r] = g == null ? value : value - g[r];
        }
        return residual;
    }

    static void AddResultDiagnostics(
        TargetBeamResultants target,
        StripBeamSupportScheme scheme,
        LoadRecoveryOptions options,
        RawDerivativeResult raw,
        double[] coefficients,
        LoadRecoveryApproximationError error,
        double[] equilibriumResidual,
        List<FemValidationDiagnostic> diagnostics)
    {
        var equilibrium = options.UsesEquilibriumConstraints ? options.EquilibriumTarget : null;
        if (equilibrium == null)
        {
            diagnostics.Add(new("plate_strip_load_recovery_equilibrium_unconstrained",
                "Равнодействующая внешней нагрузки не задана: ограничения равновесия не " +
                "накладывались, сохранение равнодействующей не гарантируется.", false));
        }
        else
        {
            double scale = Math.Max(MaxAbs(equilibrium.ToArray()), AbsoluteScaleFloor);
            if (Norm(equilibriumResidual) / scale > options.ResultantTolerance)
                diagnostics.Add(new("plate_strip_load_recovery_equilibrium_residual",
                    $"Невязка ограничений равновесия {Norm(equilibriumResidual):G6} превышает допуск."));
        }

        double targetNorm = Norm(target.ToRowVector());
        bool accuracyViolated = targetNorm > AbsoluteScaleFloor
            ? error.Relative > options.AccuracyTolerance
            : error.Absolute > options.AccuracyTolerance;

        if (accuracyViolated)
        {
            diagnostics.Add(new("plate_strip_load_recovery_accuracy_violation",
                $"Относительная ошибка приближения эпюры {error.Relative:G6} превышает допуск " +
                $"{options.AccuracyTolerance:G6}: расчётная нагрузка не принимается."));

            if (options.KnownEndActions == null && RequiresEndActions(target, scheme))
                diagnostics.Add(new("plate_strip_load_recovery_end_actions_required",
                    "Целевая эпюра требует ненулевых концевых усилий, которых нет в " +
                    "KnownEndActions: распределённая нагрузка такую эпюру воспроизвести не может.", false));
        }

        if (ChangesSignWithoutRawSupport(raw, coefficients))
            diagnostics.Add(new("plate_strip_load_recovery_sign_change",
                "Восстановленная интенсивность меняет знак там, где диагностическая производная " +
                "его не меняет: возможен избыток сглаживания.", false));
    }

    /// <summary>Может ли целевая эпюра иметь такие концевые значения без концевых усилий:
    /// на шарнирном конце изгибные моменты обязаны быть нулевыми, продольная сила на конце —
    /// только при закреплении обоих концов.</summary>
    static bool RequiresEndActions(TargetBeamResultants target, StripBeamSupportScheme scheme)
    {
        double moment = Math.Max(Math.Max(MaxAbs(target.My), MaxAbs(target.Mz)), AbsoluteScaleFloor);
        double axial = Math.Max(MaxAbs(target.N), AbsoluteScaleFloor);
        const double relative = 1e-6;
        int last = target.StationCount - 1;

        if (scheme.StartCondition == StripBeamEndCondition.Pinned &&
            (Math.Abs(target.My[0]) > relative * moment || Math.Abs(target.Mz[0]) > relative * moment))
            return true;
        if (scheme.EndCondition == StripBeamEndCondition.Pinned &&
            (Math.Abs(target.My[last]) > relative * moment || Math.Abs(target.Mz[last]) > relative * moment))
            return true;
        if (scheme.AxialRestraint == StripAxialRestraint.StartOnly &&
            Math.Abs(target.N[last]) > relative * axial)
            return true;

        return false;
    }

    static bool ChangesSignWithoutRawSupport(RawDerivativeResult raw, double[] coefficients)
    {
        if (!raw.IsDefined || coefficients.Length == 0)
            return false;

        bool recoveredChangesSign = HasBothSigns(coefficients);
        if (!recoveredChangesSign)
            return false;

        var rawValues = raw.Qx.Concat(raw.Qy).Concat(raw.Qz).ToArray();
        return !HasBothSigns(rawValues);
    }

    static bool HasBothSigns(IReadOnlyList<double> values)
    {
        double threshold = 1e-9 * MaxAbs(values);
        bool positive = false, negative = false;
        foreach (double value in values)
        {
            if (value > threshold) positive = true;
            else if (value < -threshold) negative = true;
        }
        return positive && negative;
    }

    static RecoveredBeamLoadSet Failed(
        TargetBeamResultants target,
        RawDerivativeResult raw,
        LoadRecoveryOptions options,
        List<FemValidationDiagnostic> diagnostics,
        string fingerprint) =>
        new("", options.Mode, options.EffectiveBasis, target, raw, [], [], [], new double[5],
            new LoadRecoveryApproximationError(0.0, 0.0, new double[target.StationCount * 3]),
            false, diagnostics, fingerprint);

    static double MaxAbs(IReadOnlyList<double> values)
    {
        double result = 0.0;
        foreach (double value in values)
            result = Math.Max(result, Math.Abs(value));
        return result;
    }

    static double Norm(IReadOnlyList<double> values)
    {
        double sum = 0.0;
        foreach (double value in values)
            sum += value * value;
        return Math.Sqrt(sum);
    }

    static double FrobeniusNorm(double[,] matrix, int size)
    {
        double sum = 0.0;
        for (int i = 0; i < size; i++)
        for (int j = 0; j < size; j++)
            sum += matrix[i, j] * matrix[i, j];
        return Math.Sqrt(sum);
    }
}
