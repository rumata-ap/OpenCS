namespace CSmath
{
    /// <summary>
    /// Интерфейс вектора фиксированной размерности.
    /// Определяет размерность и возможность преобразования в массив.
    /// </summary>
    public interface IVector
    {
        /// <summary>
        /// Размерность вектора (количество компонентов).
        /// </summary>
        int N { get; }

        /// <summary>
        /// Преобразует вектор в массив значений типа <see cref="double"/>.
        /// </summary>
        /// <returns>Массив, содержащий все компоненты вектора.</returns>
        double[] ToArray();
    }
}