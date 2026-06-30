namespace CSfea.Torsion;

/// <summary>Метод решения задачи кручения Сен-Венана.</summary>
public enum TorsionMethod
{
    /// <summary>Метод граничных элементов (порт TORSCON, функция депланации).</summary>
    Bem,

    /// <summary>Метод конечных элементов (функция Прандтля, ∇²φ=−2).</summary>
    Fem
}
