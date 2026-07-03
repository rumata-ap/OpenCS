using CSfea.Sparse;

namespace CSfea.Thermal.Bc;

/// <summary>
/// Модель граничных условий теплопроводности: добавляет вклад в глобальные K и F.
/// </summary>
public interface IHeatBoundaryModel
{
    /// <summary>
    /// Добавить вклад граничных условий в матрицы жёсткости и вектор нагрузки.
    /// </summary>
    /// <param name="time_s">Время, с (для кривых пожара).</param>
    /// <param name="nodalT">Температура в узлах, °C (для линеаризации Робина).</param>
    /// <param name="kTargets">Целевые COO-матрицы жёсткости (обычно одна — глобальная K).</param>
    /// <param name="f">Глобальный вектор нагрузки F.</param>
    void Contribute(double time_s, double[] nodalT, IList<CooMatrix> kTargets, double[] f);
}
