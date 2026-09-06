using CScore.Fem;

namespace CScore.PlateStrip;

/// <summary>Ошибка приближения целевой эпюры: относительная (с безопасным знаменателем),
/// абсолютная и поэлементная эпюра невязки в порядке строк отклика.</summary>
public sealed record LoadRecoveryApproximationError(
    double Relative,
    double Absolute,
    IReadOnlyList<double> StationResidual);

/// <summary>Результат восстановления нагрузки производной балки полосы. См.
/// docs/superpowers/specs/2026-09-06-plate-strip-equivalent-beam-load-recovery-design.md.
///
/// TargetResultants и RawDerivative заполнены при любой блокирующей ошибке: расчётная
/// нагрузка не принимается молча.</summary>
public sealed record RecoveredBeamLoadSet(
    string StripAnalogyId,
    LoadRecoveryMode RecoveryMode,
    LoadBasis Basis,
    TargetBeamResultants TargetResultants,
    RawDerivativeResult RawDerivative,
    IReadOnlyList<double> Coefficients,
    IReadOnlyList<StripLoad> ElementLoadDefinitions,
    IReadOnlyList<StripElementNodalLoad> ConsistentNodalLoads,
    IReadOnlyList<double> EquilibriumResidual,
    LoadRecoveryApproximationError ApproximationError,
    bool IsCalculable,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics,
    string InputFingerprint);
