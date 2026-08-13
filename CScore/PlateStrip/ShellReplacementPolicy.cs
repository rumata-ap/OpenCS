namespace CScore.PlateStrip;

/// <summary>Политика участия PlateStripBeamAnalogy в расчёте относительно shell-региона
/// источника (родительская спека: ShellReplacementPolicy). CoupledWithExplicitPartition
/// (частичная замена региона) не введена — см. Срез 5 «Не входит».</summary>
public enum ShellReplacementPolicy
{
    /// <summary>Beam-аналогия строится только для сравнения; shell-регион остаётся единственным
    /// расчётным владельцем жёсткости и нагрузок.</summary>
    DiagnosticOnly,

    /// <summary>Shell-регион (в объёме проекта — весь коридор полосы целиком, без частичной
    /// замены) не должен повторно участвовать в сборке жёсткости/нагрузок.</summary>
    ReplaceShellRegion
}
