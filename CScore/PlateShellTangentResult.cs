namespace CScore;

/// <summary>
/// Результат вычисления плитного сечения: усилия и касательные блоки A/B/D/As.
/// Единицы: кН/м, кН·м/м.
/// </summary>
public sealed class PlateShellTangentResult
{
    public double Nx { get; init; }
    public double Ny { get; init; }
    public double Nxy { get; init; }
    public double Mx { get; init; }
    public double My { get; init; }
    public double Mxy { get; init; }

    /// <summary>∂N/∂ε_m, ∂N/∂κ (3×3).</summary>
    public required double[,] A { get; init; }
    public required double[,] B { get; init; }

    /// <summary>∂M/∂κ (3×3).</summary>
    public required double[,] D { get; init; }

    /// <summary>Линейная жёсткость поперечного сдвига 2×2, кН/м.</summary>
    public required double[,] As { get; init; }
}
