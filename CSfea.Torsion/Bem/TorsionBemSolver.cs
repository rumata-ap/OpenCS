namespace CSfea.Torsion;

/// <summary>Решатель кручения методом граничных элементов (порт TORSCON, функция депланации ω).</summary>
public static class TorsionBemSolver
{
    /// <summary>
    /// Решает задачу Неймана для депланации ω: Δω=0, ∂ω/∂n = y·nx − x·ny.
    /// Возвращает It, центр кручения, τ/(GΘ), поле депланации.
    ///
    /// Ориентация: внешний контур приводится к CCW, отверстия — к CW.
    /// Это гарантирует правильное направление нормали для условия Неймана.
    /// </summary>
    public static TorsionProps Solve(TorsionBoundary boundary, double maxElementSize)
    {
        boundary = EnsureOrientation(boundary);
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

    // ─────────────────────────────────────────────────────────────────────────
    // Привод ориентации контуров: внешний — CCW, отверстия — CW
    // ─────────────────────────────────────────────────────────────────────────

    private static TorsionBoundary EnsureOrientation(TorsionBoundary b)
    {
        double[] outerX = b.OuterX, outerY = b.OuterY;
        if (SignedArea(outerX, outerY) < 0)   // должно быть CCW (> 0)
            (outerX, outerY) = Reversed(outerX, outerY);

        List<(double[], double[])>? holes = null;
        if (b.Holes is { Count: > 0 })
        {
            holes = new List<(double[], double[])>(b.Holes.Count);
            foreach (var (hx, hy) in b.Holes)
            {
                var (hxr, hyr) = (hx, hy);
                if (SignedArea(hxr, hyr) > 0)  // должно быть CW (< 0)
                    (hxr, hyr) = Reversed(hxr, hyr);
                holes.Add((hxr, hyr));
            }
        }
        return new TorsionBoundary(outerX, outerY, holes);
    }

    private static double SignedArea(double[] x, double[] y)
    {
        double s = 0.0;
        int n = x.Length;
        for (int i = 0; i < n; i++) { int j = (i + 1) % n; s += x[i] * y[j] - x[j] * y[i]; }
        return 0.5 * s;
    }

    private static (double[], double[]) Reversed(double[] x, double[] y)
    {
        var rx = (double[])x.Clone();
        var ry = (double[])y.Clone();
        Array.Reverse(rx);
        Array.Reverse(ry);
        return (rx, ry);
    }
}
