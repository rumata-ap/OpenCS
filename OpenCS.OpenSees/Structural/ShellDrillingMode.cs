namespace OpenCS.OpenSees.Structural;

/// <summary>Режим drilling-стабилизации shell-элемента. Q4 поддерживает все три режима, T3 —
/// только None и NonlinearDrilling (нет числового -drillingStab у T3).</summary>
public enum ShellDrillingMode
{
    None,
    Stabilization,
    NonlinearDrilling
}
