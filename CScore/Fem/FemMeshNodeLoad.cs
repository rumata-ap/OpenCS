namespace CScore.Fem;

/// <summary>Узловая нагрузка на узел сетки (сеточный уровень схемы) в глобальной системе координат.</summary>
public sealed class FemMeshNodeLoad
{
    public int Id { get; set; }
    public int SchemaId { get; set; }
    public int LoadCaseId { get; set; }

    /// <summary>Тег узла сетки (<see cref="FemMeshNode.NodeTag"/>).</summary>
    public string MeshNodeTag { get; set; } = "";

    /// <summary>Происхождение: <see cref="FemLoadOrigin.Manual"/> или «import:&lt;источник&gt;».</summary>
    public string Origin { get; set; } = FemLoadOrigin.Manual;

    public double Fx { get; set; }
    public double Fy { get; set; }
    public double Fz { get; set; }
    public double Mx { get; set; }
    public double My { get; set; }
    public double Mz { get; set; }
}
