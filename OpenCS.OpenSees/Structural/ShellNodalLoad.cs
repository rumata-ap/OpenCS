namespace OpenCS.OpenSees.Structural;

/// <summary>Узловая нагрузка shell-модели в глобальной системе (Н, Н·м).</summary>
public sealed record ShellNodalLoad(
    int NodeTag, double Fx, double Fy, double Fz, double Mx, double My, double Mz);
