using CScore.Fem;

namespace CScore.PlateStrip;

/// <summary>Режим восстановления нагрузки по целевой эпюре (родительская спека,
/// «Режимы восстановления»).</summary>
public enum LoadRecoveryMode
{
    /// <summary>Основной режим: слабая форма равновесия, регуляризация первой разностью.</summary>
    WeakEquilibriumProjection,
    /// <summary>Гладкое приближение с регуляризацией кривизны нагрузки.</summary>
    ConstrainedSmoothFit,
    /// <summary>Кусочно-постоянная нагрузка без сглаживания.</summary>
    PiecewiseUniformOrLinear,
    /// <summary>Только диагностическое дифференцирование эпюры; расчётной нагрузкой не является.</summary>
    RawDerivativeDiagnostic
}

/// <summary>Базис разложения нагрузки по станциям.</summary>
public enum LoadBasis
{
    /// <summary>n−1 функций: по одной постоянной на интервал между станциями.</summary>
    PiecewiseConstant,
    /// <summary>n hat-функций станций (крайние — полу-hat).</summary>
    PiecewiseLinear
}

/// <summary>Известные концевые усилия полосы, заданные как <b>значения эпюры</b> [N, My, Mz]
/// на первой и последней станции — в тех же величинах и знаках, что TargetBeamResultants,
/// поэтому напрямую сравнимы с целевой эпюрой.
///
/// Это <b>вход</b>, а не неизвестное задачи: постоянный момент создаётся парой концевых
/// моментов, и без них целый класс целевых эпюр недостижим никакой распределённой нагрузкой
/// (при c = 0 отклик A·c тождественно нулевой). Прикладываются к балочной модели отдельным
/// прогоном, дающим базовый отклик S₀.
///
/// Через StripLoad задать их нельзя: у точечной StripLoad нет компоненты My (в Срезе 4 она по
/// построению нулевая), поэтому end actions прикладываются прямо к узловым DOF балки.</summary>
public sealed record KnownEndActions(
    double StartN = 0.0, double StartMy = 0.0, double StartMz = 0.0,
    double EndN = 0.0, double EndMy = 0.0, double EndMz = 0.0)
{
    public bool IsZero =>
        StartN == 0.0 && StartMy == 0.0 && StartMz == 0.0 &&
        EndN == 0.0 && EndMy == 0.0 && EndMz == 0.0;

    public bool IsFinite =>
        double.IsFinite(StartN) && double.IsFinite(StartMy) && double.IsFinite(StartMz) &&
        double.IsFinite(EndN) && double.IsFinite(EndMy) && double.IsFinite(EndMz);
}

/// <summary>Равнодействующая внешней нагрузки полосы относительно её начала.
/// Из целевой эпюры не выводится (в контракте Среза 3a нет поперечных сил, а значит и опорных
/// реакций) — источником служит уже перенесённая на полосу нагрузка Среза 4.</summary>
public sealed record EquilibriumTarget(double Fx, double Fy, double Fz, double My, double Mz)
{
    /// <summary>Строит равнодействующую как тонкую обёртку над
    /// StripLoadConsistentNodalProjection.Project на тех же станциях: равенство с его
    /// TotalForceCheck/TotalMomentCheck верно по построению, а не по совпадению округлений,
    /// и политика границы элемента наследуется автоматически.</summary>
    public static (EquilibriumTarget? Target, IReadOnlyList<FemValidationDiagnostic> Diagnostics)
        FromStripLoadSet(StripLoadSet loads, double lengthM, IReadOnlyList<double> stationFractions)
    {
        ArgumentNullException.ThrowIfNull(loads);
        ArgumentNullException.ThrowIfNull(stationFractions);

        var projection = StripLoadConsistentNodalProjection.Project(loads, lengthM, stationFractions);
        if (!projection.IsCalculable)
            return (null, projection.Diagnostics);

        return (new EquilibriumTarget(
            projection.TotalForceCheck[0],
            projection.TotalForceCheck[1],
            projection.TotalForceCheck[2],
            projection.TotalMomentCheck[1],
            projection.TotalMomentCheck[2]), projection.Diagnostics);
    }

    /// <summary>Правая часть ограничений в порядке строк G: Fx, Fy, Fz, My, Mz.</summary>
    public double[] ToArray() => [Fx, Fy, Fz, My, Mz];
}

/// <summary>Опции восстановления нагрузки.</summary>
public sealed record LoadRecoveryOptions
{
    public LoadRecoveryMode Mode { get; init; } = LoadRecoveryMode.WeakEquilibriumProjection;

    /// <summary>Базис нагрузки; null — базис по умолчанию для выбранного режима.</summary>
    public LoadBasis? Basis { get; init; }

    /// <summary>Безразмерный параметр регуляризации; null — значение по умолчанию режима.</summary>
    public double? Alpha { get; init; }

    /// <summary>Веса станций, длина n, конечные и положительные; null — единичные.</summary>
    public IReadOnlyList<double>? StationWeights { get; init; }

    public EquilibriumTarget? EquilibriumTarget { get; init; }
    public KnownEndActions? KnownEndActions { get; init; }

    public double ResultantTolerance { get; init; } = 1e-6;
    public double AccuracyTolerance { get; init; } = 1e-3;

    /// <summary>Базис, фактически используемый режимом.</summary>
    public LoadBasis EffectiveBasis => Basis ?? Mode switch
    {
        LoadRecoveryMode.PiecewiseUniformOrLinear => LoadBasis.PiecewiseConstant,
        _ => LoadBasis.PiecewiseLinear
    };

    /// <summary>Параметр регуляризации, фактически используемый режимом.</summary>
    public double EffectiveAlpha => Alpha ?? Mode switch
    {
        LoadRecoveryMode.WeakEquilibriumProjection => 1e-3,
        LoadRecoveryMode.ConstrainedSmoothFit => 1e-2,
        _ => 0.0
    };

    /// <summary>Накладываются ли жёсткие ограничения равновесия в этом режиме.</summary>
    public bool UsesEquilibriumConstraints => Mode != LoadRecoveryMode.RawDerivativeDiagnostic;

    public IReadOnlyList<FemValidationDiagnostic> Validate(int stationCount)
    {
        var diagnostics = new List<FemValidationDiagnostic>();

        if (!Enum.IsDefined(Mode))
            diagnostics.Add(new("plate_strip_load_recovery_invalid_options",
                "Неизвестный режим восстановления нагрузки."));
        if (Basis.HasValue && !Enum.IsDefined(Basis.Value))
            diagnostics.Add(new("plate_strip_load_recovery_invalid_options",
                "Неизвестный базис нагрузки."));
        if (Alpha.HasValue && (!double.IsFinite(Alpha.Value) || Alpha.Value < 0.0))
            diagnostics.Add(new("plate_strip_load_recovery_invalid_options",
                "Параметр регуляризации должен быть конечным и неотрицательным."));
        if (!double.IsFinite(ResultantTolerance) || ResultantTolerance <= 0.0 ||
            !double.IsFinite(AccuracyTolerance) || AccuracyTolerance <= 0.0)
            diagnostics.Add(new("plate_strip_load_recovery_invalid_options",
                "Допуски должны быть конечными и положительными."));

        if (StationWeights != null)
        {
            if (StationWeights.Count != stationCount)
                diagnostics.Add(new("plate_strip_load_recovery_invalid_options",
                    "Число весов станций должно совпадать с числом станций."));
            else if (StationWeights.Any(w => !double.IsFinite(w) || w <= 0.0))
                diagnostics.Add(new("plate_strip_load_recovery_invalid_options",
                    "Веса станций должны быть конечными и положительными."));
        }

        if (KnownEndActions != null && !KnownEndActions.IsFinite)
            diagnostics.Add(new("plate_strip_load_recovery_invalid_options",
                "Концевые усилия должны быть конечными."));

        return diagnostics;
    }
}
