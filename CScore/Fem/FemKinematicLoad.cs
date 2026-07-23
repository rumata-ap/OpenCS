namespace CScore.Fem;

/// <summary>Статически заданное перемещение или поворот узла FEM-схемы.</summary>
public sealed class FemKinematicLoad
{
    public int Id { get; set; }
    public int SchemaId { get; set; }
    public int LoadCaseId { get; set; }
    public int NodeId { get; set; }

    /// <summary>Номер степени свободы: 1–3 — Ux/Uy/Uz, 4–6 — Rx/Ry/Rz.</summary>
    public int Dof { get; set; }

    /// <summary>Заданное значение: метры для перемещений, радианы для поворотов.</summary>
    public double Value { get; set; }
}
