using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Audit;

/// <summary>Один sensitivity-запуск: уровень, модель и fingerprint источника сетки.
/// Разные уровни обязаны иметь разные fingerprints.</summary>
public sealed record ShellSensitivityCase(
    ShellSensitivityLevel Level,
    ShellOpenSeesModel Model,
    string SourceFingerprint);

/// <summary>Фабрика sensitivity-запусков coarse/medium/fine. Реальная реализация может
/// строить сетки Gmsh или использовать заранее подготовленные модели.</summary>
public interface IShellSensitivityCaseFactory
{
    /// <summary>Создаёт по одному запуску на каждый запрошенный уровень.</summary>
    IReadOnlyList<ShellSensitivityCase> Create(IReadOnlyList<ShellSensitivityLevel> levels);
}
