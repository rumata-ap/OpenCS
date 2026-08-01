using CScore.Planar;

namespace OpenCS.Utilites;

/// <summary>Глобальные настройки внешнего генератора сетки Gmsh.</summary>
public sealed class GmshSettings
{
    /// <summary>Явный путь к gmsh.exe. Пустое значение разрешает fallback resolver-а.</summary>
    public string? ExecutablePath { get; set; }

    /// <summary>Код 2D-алгоритма Gmsh (см. Mesh.Algorithm). 6 = Frontal-Delaunay.</summary>
    public int Algorithm { get; set; } = 6;

    /// <summary>Режим элементов по умолчанию для новых построений — Mixed даёт смешанные
    /// quad+tri сетки «из коробки».</summary>
    public PlanarMeshElementMode ElementMode { get; set; } = PlanarMeshElementMode.Mixed;

    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>false — папка запуска (.geo/.msh/manifest/логи) удаляется сразу после успешного
    /// разбора snapshot; при ошибке артефакты сохраняются всегда, независимо от флага.</summary>
    public bool KeepArtifacts { get; set; } = true;

    /// <summary>Каталог артефактов запусков Gmsh. Пусто — каталог "GmshArtifacts" рядом с
    /// исполняемым файлом OpenCS (см. ResolveArtifactsPath).</summary>
    public string? ArtifactsPath { get; set; }

    public string ResolveArtifactsPath() =>
        string.IsNullOrWhiteSpace(ArtifactsPath)
            ? System.IO.Path.Combine(AppContext.BaseDirectory, "GmshArtifacts")
            : ArtifactsPath;

    public GmshSettings Clone() => new()
    {
        ExecutablePath = ExecutablePath,
        Algorithm = Algorithm,
        ElementMode = ElementMode,
        TimeoutSeconds = TimeoutSeconds,
        KeepArtifacts = KeepArtifacts,
        ArtifactsPath = ArtifactsPath
    };
}
