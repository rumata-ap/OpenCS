namespace CScore.Fem;

/// <summary>Каноническая узловая нагрузка в глобальной системе координат.</summary>
public sealed class FemNodeLoad
{
    public int Id { get; set; }
    public int SchemaId { get; set; }
    public int LoadCaseId { get; set; }
    public int NodeId { get; set; }
    public double Fx { get; set; }
    public double Fy { get; set; }
    public double Fz { get; set; }
    public double Mx { get; set; }
    public double My { get; set; }
    public double Mz { get; set; }
}
