namespace OpenCS.OpenSees.Structural;

/// <summary>Узловая нагрузка в глобальной системе (Н, Н·м).</summary>
public sealed record FemLinearNodalLoad(
    int NodeTag, double Fx, double Fy, double Fz, double Mx, double My, double Mz);
