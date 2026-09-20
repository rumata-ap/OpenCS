namespace OpenCS.Reporting.Pandoc;

/// <summary>Абсолютные пути к файлам автономного комплекта Pandoc и Typst.</summary>
public sealed record PandocRuntimePaths(
    string Root,
    string PandocExe,
    string TypstExe,
    string ReferenceDocx,
    string TypstTemplate,
    string LuaFilter,
    string FontDirectory,
    string Manifest);
