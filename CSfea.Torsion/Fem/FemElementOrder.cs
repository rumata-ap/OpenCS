namespace CSfea.Torsion;

/// <summary>Порядок конечного элемента МКЭ-решателя кручения.</summary>
public enum FemElementOrder
{
    /// <summary>T3 (CST) — линейное распределение φ, 3 узла на элемент.</summary>
    Linear,

    /// <summary>T6 (LST) — квадратичное распределение φ, 6 узлов на элемент (вершины + середины рёбер).</summary>
    Quadratic
}
