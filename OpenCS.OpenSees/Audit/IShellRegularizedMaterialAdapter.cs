using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.Audit;

/// <summary>Контракт adapter-а, фактически применяющего regularization в native material mapping.
/// Наличие enum или поля в manifest не является применением regularization.</summary>
public interface IShellRegularizedMaterialAdapter
{
    /// <summary>Режим regularization, который реализует adapter.</summary>
    ShellRegularizationMode Mode { get; }

    /// <summary>Проверяет, может ли adapter применить режим к native material.</summary>
    bool CanApply(NativeShellMaterialSpec spec);
}
