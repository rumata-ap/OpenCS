namespace CScore;

/// <summary>
/// Внутренние усилия в поперечном сечении элемента.
/// </summary>
public record InternalForces
{
    public string LoadCaseName { get; init; } = "";
    public double Position { get; init; }
    public double N { get; init; }   // Осевое (+ растяжение) [Н]
    public double Mx { get; init; }  // Момент X [Н·м]
    public double My { get; init; }  // Момент Y [Н·м]
    public double Mz { get; init; }  // Крутящий момент [Н·м]
    public double Qy { get; init; }  // Поперечная сила Y [Н]
    public double Qz { get; init; }  // Поперечная сила Z [Н]
}
