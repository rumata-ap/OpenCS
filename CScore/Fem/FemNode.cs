namespace CScore.Fem;

/// <summary>Узел конечно-элементной сетки.</summary>
public class FemNode
{
    public int    Id      { get; set; }
    public int    SchemaId{ get; set; }
    public string NodeTag { get; set; } = "";
    public double X       { get; set; }
    public double Y       { get; set; }
    public double Z       { get; set; }
    /// <summary>Маска закреплённых DOF (биты 0–5: Tx Ty Tz Rx Ry Rz). 0 = свободен, 63 = полностью закреплён.</summary>
    public int    DofMask { get; set; }
}
