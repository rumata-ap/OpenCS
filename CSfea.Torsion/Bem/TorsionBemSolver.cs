namespace CSfea.Torsion;

/// <summary>Решатель кручения методом граничных элементов (порт TORSCON, функция депланации ω).</summary>
public static class TorsionBemSolver
{
    /// <summary>
    /// Решает задачу Неймана для депланации ω: Δω=0, ∂ω/∂n = y·nx − x·ny.
    /// Возвращает It, центр кручения, τ/(GΘ), поле депланации.
    /// </summary>
    public static TorsionProps Solve(TorsionBoundary boundary, double maxElementSize)
    {
        var d = BoundaryDiscretizer.Discretize(boundary, maxElementSize);
        var (G, H, xm, ym, enx, eny) = BemMatrices.Build(d);
        var (ub, unb, singular) = BemSystem.Solve(G, H, xm, ym, enx, eny, d);
        if (singular)
            return new TorsionProps { It = 0.0, Singular = true, NElements = xm.Length };

        var (xtc, ytc, ct) = ShearCenter.Compute(ub, unb, d);
        var (ubU, unbU) = WarpingUpdate.Update(xm, ym, enx, eny, ub, unb, xtc, ytc, ct);
        double it = TorsionStiffness.Compute(ubU, xm, ym, xtc, ytc, d);
        double[] tau = TorsionStress.Compute(ubU, xm, ym, xtc, ytc, d);

        double tauMax = 0.0;
        for (int i = 0; i < tau.Length; i++)
            tauMax = Math.Max(tauMax, Math.Abs(tau[i]));

        return new TorsionProps
        {
            It = it,
            ShearCenterX = xtc,
            ShearCenterY = ytc,
            TauUnitMax = tauMax,
            NodeX = xm,
            NodeY = ym,
            TauUnitField = tau,
            PotentialField = ubU,
            Singular = false,
            NElements = xm.Length
        };
    }
}
