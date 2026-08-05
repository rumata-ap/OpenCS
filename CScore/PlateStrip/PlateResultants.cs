namespace CScore.PlateStrip;

/// <summary>Шесть результирующих усилий плитного сечения на единицу ширины.</summary>
public readonly record struct PlateResultants(
    double Nx,
    double Ny,
    double Nxy,
    double Mx,
    double My,
    double Mxy)
{
    /// <summary>Возвращает усилия в порядке [Nx, Ny, Nxy, Mx, My, Mxy].</summary>
    public double[] ToArray() => [Nx, Ny, Nxy, Mx, My, Mxy];
}
