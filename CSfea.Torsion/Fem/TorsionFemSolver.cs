using CSfea.Sparse;

namespace CSfea.Torsion;

/// <summary>Решатель кручения методом конечных элементов (функция Прандтля).</summary>
public static class TorsionFemSolver
{
    /// <summary>
    /// Решает ∇²φ=−2, φ=0 на всех контурах. Возвращает TorsionProps (It, τ-поле, τ_max).
    /// ShearCenterX/Y = NaN (φ-формулировка его не даёт).
    /// </summary>
    public static TorsionProps Solve(TorsionBoundary boundary, double maxElementSize)
    {
        var mesh = MeshBuilder.Build(boundary, maxElementSize);
        int ndof = mesh.NodesX.Length;
        int[] fixedDofs = mesh.FixedDofs;
        double[]? uFixed = null; // φ=0 на границе

        var (K, F) = PrandtlAssembler.Assemble(mesh);
        var reduced = DirichletReducer.Reduce(K, F, fixedDofs, uFixed);
        double[] uFree = SparseLuSolver.SolveOnce(reduced.Kff, reduced.Fmod);
        double[] phi = DirichletReducer.Expand(ndof, reduced.Free, uFree, fixedDofs, uFixed);

        double it = TorsionPostprocessor.ComputeIt(mesh, phi);
        var (tauX, tauY) = TorsionPostprocessor.ComputeStresses(mesh, phi);
        var tauUnit = new double[ndof];
        double tauMax = 0.0;
        for (int i = 0; i < ndof; i++)
        {
            tauUnit[i] = Math.Sqrt(tauX[i] * tauX[i] + tauY[i] * tauY[i]);
            if (tauUnit[i] > tauMax) tauMax = tauUnit[i];
        }

        return new TorsionProps
        {
            It = it,
            ShearCenterX = double.NaN,
            ShearCenterY = double.NaN,
            TauUnitMax = tauMax,
            NodeX = mesh.NodesX,
            NodeY = mesh.NodesY,
            TauUnitField = tauUnit,
            PotentialField = phi,
            Singular = false,
            NElements = mesh.Triangles.Length
        };
    }
}
