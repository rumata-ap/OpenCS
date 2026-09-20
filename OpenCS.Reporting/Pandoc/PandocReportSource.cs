namespace OpenCS.Reporting.Pandoc;

/// <summary>Материализованный исходник отчёта для одного запуска Pandoc.</summary>
public sealed record PandocReportSource(
    string Directory,
    string MarkdownPath,
    IReadOnlyList<string> AssetPaths,
    bool KeepArtifacts = false);
