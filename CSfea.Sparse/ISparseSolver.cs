namespace CSfea.Sparse;

/// <summary>
/// Прямой разреженный решатель систем A·x = b. Разделение на факторизацию и
/// решение позволяет переиспользовать разложение для нескольких правых частей
/// и (в перспективе) символическую факторизацию в NR-цикле, где паттерн
/// разрежённости K_T постоянен внутри шага нагружения (см. конспект).
/// Единый интерфейс для собственного <see cref="SparseLuSolver"/> и
/// опционального backend на CSparse — для кросс-валидации.
/// </summary>
public interface ISparseSolver : IDisposable
{
    /// <summary>Факторизовать матрицу A (CSC).</summary>
    void Factorize(CscMatrix a);

    /// <summary>Решить A·x = b по готовой факторизации.</summary>
    double[] Solve(double[] b);
}
