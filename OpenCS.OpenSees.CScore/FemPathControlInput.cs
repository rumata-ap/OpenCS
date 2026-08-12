using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore;

/// <summary>Зеркало FemDisplacementControlSettings на NodeId схемы вместо mesh-тега —
/// см. FemPathControlSettings в OpenCS.OpenSees.Structural про беззнаковые модули и
/// абсолютный TargetDisplacement.</summary>
public sealed record FemDisplacementControlInput(
    int ControlNodeId, int ControlDof,
    double InitialIncrement, double MinIncrement, double MaxIncrement,
    double TargetDisplacement, int MaxSteps);

/// <summary>Зеркало FemArcLengthSettings на NodeId схемы.</summary>
public sealed record FemArcLengthInput(
    double S, double Alpha, double MinS, int MaxSteps,
    int MonitorNodeId, int MonitorDof);

/// <summary>Зеркало FemPathControlSettings на NodeId схемы — вход резолвера
/// (FemNonlinearModelResolver резолвит NodeId → mesh-тег, см. FemPathControlSettings в
/// структурном слое).</summary>
public sealed record FemPathControlInput(
    FemPathControlMode Mode = FemPathControlMode.LoadControl,
    FemDisplacementControlInput? DisplacementControl = null,
    FemArcLengthInput? ArcLength = null,
    FemPathControlMode? ContinueWithMode = null,
    FemDisplacementControlInput? ContinueWithDisplacementControl = null,
    FemArcLengthInput? ContinueWithArcLength = null);
