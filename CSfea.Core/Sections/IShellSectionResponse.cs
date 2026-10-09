namespace CSfea.Core;

/// <summary>Усилия оболочечного сечения: N (3), M (3), Q (2).</summary>
public readonly record struct ShellForces(double[] N, double[] M, double[] Q);

/// <summary>Касательные жёсткости сечения: A, B, D (3x3) и A_s (2x2).</summary>
public readonly record struct ShellTangent(double[,] A, double[,] B, double[,] D, double[,] As);

/// <summary>
/// Полиморфное оболочечное сечение под кинематикой Миндлина–Рейснера.
/// Все векторы/матрицы — в локальной системе элемента.
/// Порт протокола <c>fea/section_response.py: ShellSectionResponse</c>.
/// </summary>
public interface IShellSectionResponse
{
    /// <summary>
    /// Усилия по деформациям: eps_m=[εxx,εyy,γxy], kappa=[κxx,κyy,κxy],
    /// gamma=[γxz,γyz].
    /// </summary>
    ShellForces Forces(double[] epsM, double[] kappa, double[] gamma);

    /// <summary>Касательная жёсткость (A=∂N/∂ε, B=∂N/∂κ, D=∂M/∂κ, As=∂Q/∂γ).</summary>
    ShellTangent Tangent(double[] epsM, double[] kappa, double[] gamma);

    /// <summary>Зафиксировать состояние (хук stateful-моделей; обычно no-op).</summary>
    void Commit();

    /// <summary>Сбросить состояние.</summary>
    void Reset();

    /// <summary>
    /// Касательная не зависит от деформаций и не меняется со временем (линейное сечение): матрицу КЭ при u = 0 можно
    /// посчитать один раз (<see cref="StructuralMesh.SolveLinear(double[], BoundaryConditions)"/> кэширует вклад таких КЭ).
    /// </summary>
    bool IsConstantStiffness => false;

    /// <summary>
    /// Масштаб искусственной жёсткости поворота вокруг нормали CR-оболочки (<see cref="ShellCorotational"/>): наибольшая
    /// диагональ мембранной A при нулевых деформациях. В пределах одного решения должен быть постоянным (это
    /// жёсткость штрафа в F_int, а не касательная); тяжёлые сечения его кэшируют.
    /// </summary>
    double DrillingScale
    {
        get
        {
            var a = Tangent(new double[3], new double[3], new double[2]).A;
            return Math.Max(a[0, 0], Math.Max(a[1, 1], a[2, 2]));
        }
    }
}
