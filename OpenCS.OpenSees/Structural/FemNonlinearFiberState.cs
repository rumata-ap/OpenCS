namespace OpenCS.OpenSees.Structural;

/// <summary>Состояние одной фибры в точке интегрирования forceBeamColumn.</summary>
public sealed record FemNonlinearFiberState(
    int StepIndex,
    double LoadFactor,
    int ElementTag,
    int IntegrationPoint,
    int FiberIndex,
    double StressPa,
    double Strain);
