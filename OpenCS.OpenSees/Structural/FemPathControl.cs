namespace OpenCS.OpenSees.Structural;

/// <summary>Способ продвижения по равновесной траектории нелинейного расчёта.</summary>
public enum FemPathControlMode { LoadControl, DisplacementControl, ArcLength }

/// <summary>Параметры integrator DisplacementControl. InitialIncrement/MinIncrement/
/// MaxIncrement — БЕЗЗНАКОВЫЕ модули приращения; направление вычисляется в Tcl в момент
/// старта стадии из фактического перемещения узла относительно TargetDisplacement (см.
/// FemNonlinearTclGenerator.advanceDisplacement) — задавать знак заранее нельзя: начальное
/// перемещение узла к этому моменту зависит от предыдущих стадий и на этапе UI/резолва
/// неизвестно. TargetDisplacement — абсолютное перемещение узла, не приращение.</summary>
public sealed record FemDisplacementControlSettings(
    int ControlNodeTag, int ControlDof,
    double InitialIncrement, double MinIncrement, double MaxIncrement,
    double TargetDisplacement, int MaxSteps);

/// <summary>Параметры integrator ArcLength. MaxS сознательно отсутствует: нативной
/// адаптации шага ArcLength вверх в этой версии OpenSees нет, рост шага не реализуется —
/// единственная используемая граница на уменьшение — MinS (backoff-пол при отказе).
/// MonitorNodeTag/Dof не участвует в самом integrator (метод не требует контрольного DOF)
/// — используется только для построения графика λ–u в результатах.</summary>
public sealed record FemArcLengthSettings(
    double S, double Alpha, double MinS, int MaxSteps,
    int MonitorNodeTag, int MonitorDof);

/// <summary>Настройки способа управления траекторией одной стадии нагружения.
/// Continuation (ContinueWith...) осмыслен только при Mode == LoadControl — автоматическое
/// переключение на DisplacementControl/ArcLength, если дробление шага LoadControl
/// исчерпано (см. FemNonlinearTclGenerator, «Оркестрация стадии»).</summary>
public sealed record FemPathControlSettings(
    FemPathControlMode Mode = FemPathControlMode.LoadControl,
    FemDisplacementControlSettings? DisplacementControl = null,
    FemArcLengthSettings? ArcLength = null,
    FemPathControlMode? ContinueWithMode = null,
    FemDisplacementControlSettings? ContinueWithDisplacementControl = null,
    FemArcLengthSettings? ContinueWithArcLength = null);
