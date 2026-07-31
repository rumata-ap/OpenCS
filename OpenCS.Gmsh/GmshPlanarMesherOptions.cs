namespace OpenCS.Gmsh;

/// <summary>Параметры запуска внешнего Gmsh.</summary>
public sealed class GmshPlanarMesherOptions
{
    public string? ExecutablePath { get; init; }
    public string? BundledExecutablePath { get; init; }
    public string ArtifactRoot { get; init; } = Path.Combine(Path.GetTempPath(), "opencs-gmsh");
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);
}
