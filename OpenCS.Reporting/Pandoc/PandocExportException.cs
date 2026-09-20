namespace OpenCS.Reporting.Pandoc;

/// <summary>Причина отказа автономного конвейера Pandoc/Typst.</summary>
public enum PandocExportFailureReason
{
    /// <summary>В комплекте отсутствует обязательный файл.</summary>
    BundleMissing,
    /// <summary>Контрольная сумма файла комплекта не совпала с манифестом.</summary>
    BundleIntegrity,
    /// <summary>Внешний процесс не удалось запустить.</summary>
    ProcessStartFailed,
    /// <summary>Внешний процесс завершился с ошибкой.</summary>
    ProcessFailed,
    /// <summary>Внешний процесс не завершился за заданный интервал.</summary>
    TimedOut,
    /// <summary>Исходный ресурс отчёта некорректен.</summary>
    InvalidAsset,
    /// <summary>Результирующий файл отсутствует или пуст.</summary>
    InvalidOutput
}

/// <summary>Техническая ошибка автономного экспорта отчёта.</summary>
public sealed class PandocExportException : Exception
{
    /// <summary>Причина отказа.</summary>
    public PandocExportFailureReason Reason { get; }

    /// <summary>Инструмент, завершивший операцию, если он известен.</summary>
    public string? Tool { get; }

    /// <summary>Код завершения внешнего процесса.</summary>
    public int? ExitCode { get; }

    /// <summary>Диагностика stderr внешнего процесса.</summary>
    public string? StandardError { get; }

    /// <summary>Создаёт техническую ошибку экспорта.</summary>
    public PandocExportException(
        PandocExportFailureReason reason,
        string message,
        string? tool = null,
        int? exitCode = null,
        string? standardError = null,
        Exception? inner = null)
        : base(message, inner)
    {
        Reason = reason;
        Tool = tool;
        ExitCode = exitCode;
        StandardError = standardError;
    }
}
