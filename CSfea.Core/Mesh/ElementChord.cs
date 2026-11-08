namespace CSfea.Core;

/// <summary>Точка линии уровня как линейная комбинация узлов сетки: u(точки) = Σ wᵢ·u(узла i).</summary>
public sealed record ChordPoint(int[] Nodes, double[] Weights)
{
    /// <summary>Точка в узле сетки.</summary>
    public static ChordPoint At(int node) => new([node], [1.0]);
}

/// <summary>
/// Хорда участка вертикального КЭ для режима «только P-δ» (<see cref="StructuralMesh.SetLocalSecondOrder"/>): по
/// каждому узлу КЭ (в порядке узлов КЭ) — точка нижнего уровня <see cref="Bottom"/>, верхнего <see cref="Top"/> и доля
/// высоты <see cref="Zeta"/> (0 — нижний уровень, 1 — верхний). Перемещение хорды в узле:
/// u_х = (1 − ζ)·u(Bottom) + ζ·u(Top); при <see cref="Rotations"/> (стержень) — и поворот хорды
/// φ = e × (u(Top) − u(Bottom))/<see cref="Length"/>, e — единичная ось участка <see cref="Axis"/> (снизу вверх).
/// K_σ КЭ берётся по перемещениям без хорды: (T·u)ᵀ·K_σ·(T·u), T·u = u − u_х.
/// </summary>
public sealed record ElementChord(ChordPoint[] Bottom, ChordPoint[] Top, double[] Zeta, double[] Axis, double Length,
    bool Rotations);
