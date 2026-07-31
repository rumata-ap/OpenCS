namespace OpenCS.Utilites;

/// <summary>Глобальные настройки внешнего генератора сетки Gmsh.</summary>
public sealed class GmshSettings
{
    /// <summary>Явный путь к gmsh.exe. Пустое значение разрешает fallback resolver-а.</summary>
    public string? ExecutablePath { get; set; }

    public GmshSettings Clone() => new() { ExecutablePath = ExecutablePath };
}
