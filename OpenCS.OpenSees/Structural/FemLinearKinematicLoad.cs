namespace OpenCS.OpenSees.Structural;

/// <summary>Заданное перемещение или поворот узла линейной OpenSees-модели.</summary>
public sealed record FemLinearKinematicLoad(int NodeTag, int Dof, double Value);
