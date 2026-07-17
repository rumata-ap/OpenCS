namespace CScore.Fem;

/// <summary>Узел конечно-элементной сетки, созданный при дискретизации конструктивного элемента.</summary>
public class FemMeshNode
{
    /// <summary>Идентификатор узла в базе данных.</summary>
    public int Id { get; set; }

    /// <summary>Идентификатор FEM-схемы.</summary>
    public int SchemaId { get; set; }

    /// <summary>Тег узла в пределах FEM-схемы.</summary>
    public string NodeTag { get; set; } = "";

    /// <summary>Координата узла по оси X, м.</summary>
    public double X { get; set; }

    /// <summary>Координата узла по оси Y, м.</summary>
    public double Y { get; set; }

    /// <summary>Координата узла по оси Z, м.</summary>
    public double Z { get; set; }

    /// <summary>Тег исходного узла до дискретизации, если узел получен из существующей модели.</summary>
    public string? SourceNodeTag { get; set; }

    /// <summary>Тег исходного конструктивного элемента до дискретизации.</summary>
    public string? SourceMemberTag { get; set; }
}
