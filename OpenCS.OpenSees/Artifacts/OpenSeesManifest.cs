namespace OpenCS.OpenSees.Artifacts;

/// <summary>Манифест запуска OpenSees, сохраняемый рядом с артефактами.</summary>
public sealed class OpenSeesManifest
{
    /// <summary>Версия формата манифеста.</summary>
    public string SchemaVersion { get; init; } = "stage-0-1";

    /// <summary>Время создания артефакта UTC.</summary>
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Статус анализа.</summary>
    public string Status { get; set; } = "created";

    /// <summary>Код завершения процесса.</summary>
    public int? ExitCode { get; set; }

    /// <summary>Диагностика запуска и парсинга.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}
