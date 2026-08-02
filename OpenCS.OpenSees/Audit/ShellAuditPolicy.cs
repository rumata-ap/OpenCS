using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OpenCS.OpenSees.Audit;

/// <summary>Уровень mesh sensitivity, используемый audit policy и factory.</summary>
public enum ShellSensitivityLevel
{
    /// <summary>Грубая сетка.</summary>
    Coarse,

    /// <summary>Средняя сетка.</summary>
    Medium,

    /// <summary>Мелкая сетка.</summary>
    Fine
}

/// <summary>Режим аудита: Strict блокирует verdict, DiagnosticOnly оставляет
/// результат usable с предупреждением.</summary>
public enum ShellAuditMode
{
    /// <summary>Обязательные checks блокируют расчёт.</summary>
    Strict,

    /// <summary>Обязательные ограничения представлены предупреждениями.</summary>
    DiagnosticOnly
}

/// <summary>Verdict audit-расчёта.</summary>
public enum ShellAuditVerdict
{
    /// <summary>Все обязательные checks подтверждены.</summary>
    Passed,

    /// <summary>Результат usable с явно перечисленными ограничениями.</summary>
    Warning,

    /// <summary>Preflight или обязательная capability не выполнены.</summary>
    Blocked,

    /// <summary>Три sensitivity-запуска сошлись, но tolerance превышена.</summary>
    MeshDependent
}

/// <summary>Минимальный confidence energy audit.</summary>
public enum ShellEnergyConfidenceRequirement
{
    /// <summary>Native material/backend вернул проверенный energy response.</summary>
    NativeResponse,

    /// <summary>Energy вычислена интегрированием сопряжённых component pairs.</summary>
    StateIntegral,

    /// <summary>Energy вычислена по работе внешних nodal loads.</summary>
    ExternalWorkOnly,

    /// <summary>Обязательные исходные данные отсутствуют.</summary>
    Unavailable
}

/// <summary>Политика audit shell-модели: tolerances, response, energy, regularization
/// и mesh sensitivity.</summary>
public sealed record ShellAuditPolicy
{
    /// <summary>Режим Strict или DiagnosticOnly.</summary>
    public ShellAuditMode Mode { get; init; } = ShellAuditMode.DiagnosticOnly;

    /// <summary>Абсолютный допуск равновесия для шести компонент.</summary>
    public double AbsoluteEquilibriumTolerance { get; init; } = 1e-3;

    /// <summary>Относительный допуск равновесия.</summary>
    public double RelativeEquilibriumTolerance { get; init; } = 1e-3;

    /// <summary>Обязательные response-имена материала.</summary>
    public IReadOnlyList<string> RequiredResponses { get; init; } = ["stress", "strain"];

    /// <summary>Минимальный confidence energy.</summary>
    public ShellEnergyConfidenceRequirement MinEnergyConfidence { get; init; } =
        ShellEnergyConfidenceRequirement.ExternalWorkOnly;

    /// <summary>Политика regularization.</summary>
    public ShellRegularizationPolicy Regularization { get; init; } = new();

    /// <summary>Уровни mesh sensitivity: coarse, medium и fine.</summary>
    public IReadOnlyList<ShellSensitivityLevel> SensitivityLevels { get; init; } =
        [ShellSensitivityLevel.Coarse, ShellSensitivityLevel.Medium, ShellSensitivityLevel.Fine];

    /// <summary>Относительный допуск сравнения sensitivity-метрик.</summary>
    public double SensitivityRelativeTolerance { get; init; } = 0.1;

    /// <summary>Fingerprint содержательных настроек политики.</summary>
    public string Fingerprint => ComputeFingerprint();

    private string ComputeFingerprint()
    {
        string canonical = string.Join("|",
            Mode,
            AbsoluteEquilibriumTolerance.ToString("G17", CultureInfo.InvariantCulture),
            RelativeEquilibriumTolerance.ToString("G17", CultureInfo.InvariantCulture),
            string.Join(",", RequiredResponses),
            MinEnergyConfidence,
            Regularization.Mode,
            Regularization.Method,
            string.Join(",", SensitivityLevels),
            SensitivityRelativeTolerance.ToString("G17", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
