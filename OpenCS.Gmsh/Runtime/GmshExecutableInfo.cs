namespace OpenCS.Gmsh.Runtime;

/// <summary>Разрешённый внешний исполняемый файл Gmsh.</summary>
public sealed record GmshExecutableInfo(string Path, string Source, string? RawVersionOutput = null);
