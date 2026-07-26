using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Artifacts;

/// <summary>Shell-метаданные mapping и recorder contract.</summary>
public sealed class ShellManifestData
{
    /// <summary>Fingerprints исходных и нормализованных shell-секций.</summary>
    public IReadOnlyList<string> SectionFingerprints { get; init; } = [];

    /// <summary>Локальные frames элементов или секций.</summary>
    public IReadOnlyList<ShellFrame> Frames { get; init; } = [];

    /// <summary>Упорядоченный snapshot слоёв native LayeredShell.</summary>
    public IReadOnlyList<RCShellLayer> LayerRecords { get; init; } = [];

    /// <summary>Политики integration points элементов.</summary>
    public IReadOnlyList<ShellIntegrationPolicy> IntegrationPolicies { get; init; } = [];

    /// <summary>Связь output-файлов с элементами и integration points.</summary>
    public IReadOnlyList<ShellRecorderMapping> RecorderMappings { get; init; } = [];

    /// <summary>Режим mapping секций.</summary>
    public ShellMappingMode MappingMode { get; init; } = ShellMappingMode.Exact;

    /// <summary>Явно сохранённые признаки аппроксимации.</summary>
    public IReadOnlyList<string> ApproximationFlags { get; init; } = [];

    /// <summary>Единицы shell backend.</summary>
    public string UnitContract { get; init; } = "m,N,Pa";

    /// <summary>Сторона срединной плоскости, соответствующая положительной нормали.</summary>
    public string FaceConvention { get; init; } = "z>0 = +Normal";

    /// <summary>Текстовый контракт знаков результантов.</summary>
    public string SignContract { get; init; } = "";

    /// <summary>Диагностика mapping и recorder contract.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}

/// <summary>Запись соответствия shell recorder-файла topology и section mapping.</summary>
public sealed record ShellRecorderMapping(
    string FileName,
    int ElementTag,
    int? IntegrationPoint,
    string SectionFingerprint);

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

    /// <summary>Необязательные metadata shell-модели; beam-манифесты остаются совместимыми.</summary>
    public ShellManifestData? Shell { get; init; }
}
