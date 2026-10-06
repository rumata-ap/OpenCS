using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>
/// Оболочечное сечение с замороженной секущей матрицей: N = A·ε + B·κ, M = Bᵀ·ε + D·κ,
/// Q = As·γ, касательная — та же матрица. Секущий расчёт (Пикар) пересобирает матрицу между
/// итерациями через <see cref="Update"/>; внутри одной линейной задачи сечение линейно.
/// </summary>
public sealed class SecantShellResponse : IShellSectionResponse
{
    private ShellTangent _abd;

    public SecantShellResponse(ShellTangent abd)
    {
        _abd = Validate(abd);
    }

    /// <summary>Текущая секущая матрица.</summary>
    public ShellTangent Matrix => _abd;

    /// <summary>Заменить секущую матрицу.</summary>
    public void Update(ShellTangent abd) => _abd = Validate(abd);

    public ShellForces Forces(double[] epsM, double[] kappa, double[] gamma)
    {
        var n = Dense.AddV(Dense.MatVec(_abd.A, epsM), Dense.MatVec(_abd.B, kappa));
        var m = Dense.AddV(Dense.MatTVec(_abd.B, epsM), Dense.MatVec(_abd.D, kappa));
        var q = Dense.MatVec(_abd.As, gamma);
        return new ShellForces(n, m, q);
    }

    public ShellTangent Tangent(double[] epsM, double[] kappa, double[] gamma) => _abd;

    public void Commit() { }

    public void Reset() { }

    private static ShellTangent Validate(ShellTangent abd)
    {
        static void Check(double[,] m, int n, string name)
        {
            if (m == null || m.GetLength(0) != n || m.GetLength(1) != n)
                throw new ArgumentException($"Блок {name} секущей матрицы должен быть {n}×{n}");
        }
        Check(abd.A, 3, "A"); Check(abd.B, 3, "B"); Check(abd.D, 3, "D"); Check(abd.As, 2, "As");
        return abd;
    }
}
