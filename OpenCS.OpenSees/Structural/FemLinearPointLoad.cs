namespace OpenCS.OpenSees.Structural;

/// <summary>Сосредоточенная нагрузка внутри длины одного OpenSees-элемента, в его локальных осях.</summary>
public sealed record FemLinearPointLoad(
    int ElementTag, double Py, double Pz, double Px, double XOverL);
