namespace CScore;

/// <summary>
/// Результат вычисления отклика поперечного стержневого сечения
/// (усилия N, Mx, My и опционально касательная 3×3).
/// Единицы: кН, кН·м.
/// </summary>
public sealed class SectionResult
{
    /// <summary>Осевая сила N, кН.</summary>
    public double N { get; init; }

    /// <summary>Момент ∫σ·y·dA, кН·м (управляется кривизной ky).</summary>
    public double Mx { get; init; }

    /// <summary>Момент ∫σ·x·dA, кН·м (управляется кривизной kz).</summary>
    public double My { get; init; }

    /// <summary>
    /// Касательная 3×3: J[i,j] = ∂F_i/∂x_j, F=(N,Mx,My), x=(e0,ky,kz).
    /// null, если жёсткость не вычислялась.
    /// </summary>
    public double[,]? Tangent { get; init; }
}
