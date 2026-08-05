namespace CScore.PlateStrip;

/// <summary>Обобщённые деформации стержня полосы: осевая и две изгибные кривизны.</summary>
public readonly record struct BeamStrainState(double Eps0, double KappaY, double KappaZ)
{
    /// <summary>Нулевое состояние.</summary>
    public static BeamStrainState Zero => new();
}
