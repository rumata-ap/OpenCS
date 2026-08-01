namespace CScore.Planar;

/// <summary>Режим линейных элементов, запрашиваемый у внешнего генератора сетки.</summary>
public enum PlanarMeshElementMode
{
    Triangles,
    Quads,
    Mixed
}

/// <summary>Параметры генерации сетки одного плоского конструктивного элемента.</summary>
public sealed record PlanarMeshSettings(double MaxElementSizeM, int Algorithm, PlanarMeshElementMode ElementMode)
{
    public void Validate()
    {
        if (!double.IsFinite(MaxElementSizeM) || MaxElementSizeM <= 0)
            throw new InvalidOperationException("Максимальный размер элемента сетки должен быть конечным и положительным.");
        if (Algorithm <= 0)
            throw new InvalidOperationException("Алгоритм генерации сетки должен быть положительным.");
    }
}

/// <summary>Данные окружения и генератора, входящие в воспроизводимый отпечаток сетки.</summary>
public sealed record PlanarMeshProvenance(string GmshVersion, string GeneratorVersion)
{
    public string? ExecutablePath { get; init; }
    public string? ArtifactDirectory { get; init; }
}
