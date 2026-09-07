namespace CScore.PlateStrip;

/// <summary>Политика участия PlateStripBeamAnalogy в расчёте относительно shell-региона
/// источника (родительская спека: ShellReplacementPolicy). CoupledWithExplicitPartition
/// Срез 7 добавил CoupledWithExplicitPartition — частичную замену региона, отложенную
/// Срезом 5.</summary>
public enum ShellReplacementPolicy
{
    /// <summary>Beam-аналогия строится только для сравнения; shell-регион остаётся единственным
    /// расчётным владельцем жёсткости и нагрузок.</summary>
    DiagnosticOnly,

    /// <summary>Shell-регион (весь коридор полосы целиком) не должен повторно участвовать в
    /// сборке жёсткости/нагрузок.</summary>
    ReplaceShellRegion,

    /// <summary>Заменяется только явно заданная часть коридора (PartitionPolygon манифеста),
    /// остальная часть региона продолжает считаться оболочкой (Срез 7). Каждый участок границы
    /// разбиения, не совпадающий с границей коридора, обязан быть покрыт
    /// StripBoundaryInterface — иначе действия сохраняемой части передавались бы молча.</summary>
    CoupledWithExplicitPartition
}
