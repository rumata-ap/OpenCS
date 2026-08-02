using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.Audit;

/// <summary>Registry adapter-ов regularization. По умолчанию registry пуст, потому что
/// текущий срез не объявляет верифицированную capability для PlasticDamageConcrete.</summary>
public sealed class ShellRegularizationCapability
{
    private readonly IReadOnlyList<IShellRegularizedMaterialAdapter> _adapters;

    /// <summary>Создаёт registry adapter-ов regularization.</summary>
    public ShellRegularizationCapability(IReadOnlyList<IShellRegularizedMaterialAdapter> adapters)
    {
        _adapters = adapters ?? throw new ArgumentNullException(nameof(adapters));
    }

    /// <summary>Проверяет, поддерживает ли какой-либо adapter заданный режим.</summary>
    public bool CanApply(ShellRegularizationMode mode) =>
        _adapters.Any(adapter => adapter.Mode == mode);

    /// <summary>Проверяет, поддерживает ли adapter режим для native material.</summary>
    public bool CanApplyTo(ShellRegularizationMode mode, NativeShellMaterialSpec spec) =>
        _adapters.Any(adapter => adapter.Mode == mode && adapter.CanApply(spec));

    /// <summary>Возвращает режимы, поддерживаемые зарегистрированными adapter-ами.</summary>
    public IReadOnlyList<ShellRegularizationMode> SupportedModes =>
        _adapters.Select(adapter => adapter.Mode).Distinct().ToArray();
}
