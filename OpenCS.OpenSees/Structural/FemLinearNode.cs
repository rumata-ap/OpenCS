namespace OpenCS.OpenSees.Structural;

/// <summary>Узел линейной OpenSees-модели. Fixed — 6 флагов закрепления (Tx,Ty,Tz,Rx,Ry,Rz).</summary>
public sealed record FemLinearNode(int Tag, double X, double Y, double Z, bool[] Fixed);
