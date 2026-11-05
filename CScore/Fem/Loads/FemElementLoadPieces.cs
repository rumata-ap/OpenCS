using CScore.Planar;

namespace CScore.Fem.Loads;

/// <summary>
/// Нагрузки на КЭ → части по виду (<see cref="FemLoadPiece"/>): тот же разбор вида, направления и проверок, что в
/// <see cref="FemElementLoadNodalizer"/>, поэтому ΣF и ΣM частей совпадают с согласованными узловыми силами. Нагрузка,
/// которую нодализатор пропускает (нет значений, точка вне КЭ, оси неизвестны), — пусто.
/// </summary>
public static class FemElementLoadPieces
{
    /// <summary>Части нагрузки <paramref name="load"/> на КЭ <paramref name="e"/> с множителем <paramref name="factor"/>.</summary>
    public static IReadOnlyList<FemLoadPiece> Of(FemElementLoad load, FemElement e, FemLoadMeshContext mesh, double factor)
    {
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(mesh);
        if (mesh.Geometry(e) is not { } g || !(g.IsBar || g.IsShell)) return [];
        var v = load.Values;
        if (load.LoadKind == FemElementLoadKinds.SelfWeight)
            return v.Count < 1 ? [] : SelfWeight(v[0] * factor, e, mesh);
        if (FemElementLoadNodalizer.Direction(load, g, mesh.BarRotationDeg(e)) is not { } dir) return [];

        switch (load.LoadKind)
        {
            case FemElementLoadKinds.Uniform when v.Count >= 1:
                return [Uniform(g, dir * (v[0] * factor))];

            case FemElementLoadKinds.Nodal when g.IsShell && v.Count == g.Points.Count:
                return [new FemSurfacePiece(g, v.Select(x => dir * (x * factor)).ToArray())];

            case FemElementLoadKinds.Point when g.IsShell && v.Count >= 3:
            {
                var at = FemElementLoadNodalizer.ShellPointAt(g, v[1], v[2]);
                return g.ShellShapeAt(at) is null ? [] : [new FemPointPiece(at, dir * (v[0] * factor), PlanarVector3.Zero)];
            }

            case FemElementLoadKinds.Point when g.IsBar && v.Count >= 2:
            {
                double l = g.Length, a = v[1];
                if (a < -1e-9 || a > l + 1e-9) return [];
                var axis = (g.Points[1] - g.Points[0]) * (1 / l);
                return [new FemPointPiece(g.Points[0] + axis * Math.Clamp(a, 0, l), dir * (v[0] * factor), PlanarVector3.Zero)];
            }

            case FemElementLoadKinds.Trapezoid when g.IsBar && v.Count >= 4:
            {
                if (FemElementLoadNodalizer.TrapezoidOnBar(v[0], v[1], v[2], v[3], g.Length) is not var (a, qa, b, qb)) return [];
                var axis = (g.Points[1] - g.Points[0]) * (1 / g.Length);
                return [new FemLinePiece(g.Points[0] + axis * a, g.Points[0] + axis * b, dir * (qa * factor), dir * (qb * factor))];
            }

            case FemElementLoadKinds.Edge when g.IsShell && v.Count >= 4:
                return FemElementLoadNodalizer.ShellEdge(g, v[0], v[1]) is var (i, j)
                    ? [new FemLinePiece(g.Points[i], g.Points[j], dir * (v[2] * factor), dir * (v[3] * factor))]
                    : [];

            default:
                return [];
        }
    }

    /// <summary>
    /// Собственный вес КЭ с коэффициентом <paramref name="coefficient"/> (вниз по −Z, γ·h или γ·A — как
    /// <see cref="FemElementLoadNodalizer.SelfWeightIntensity"/>); пусто — данных нет.
    /// </summary>
    public static IReadOnlyList<FemLoadPiece> SelfWeight(double coefficient, FemElement e, FemLoadMeshContext mesh)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(mesh);
        if (coefficient == 0 || mesh.Geometry(e) is not { } g || !(g.IsBar || g.IsShell)
            || FemElementLoadNodalizer.SelfWeightIntensity(e, g, mesh.SelfWeight) is not { } q) return [];
        return [Uniform(g, new PlanarVector3(0, 0, -q * coefficient))];
    }

    static FemLoadPiece Uniform(FemElementGeometry g, PlanarVector3 q) =>
        g.IsShell ? new FemSurfacePiece(g, Enumerable.Repeat(q, g.Points.Count).ToArray()) : new FemLinePiece(g.Points[0], g.Points[1], q, q);
}
