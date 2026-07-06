using CSfea.Sparse;
using CSTriangulation;

namespace CSfea.Torsion;

/// <summary>
/// МКЭ-решатель задачи кручения (функция Прандтля).
///
/// Многосвязные области:
///   φ = 0   на внешнем контуре Γ₀ (Дирихле)
///   φ = c_k на k-м отверстии Γ_k (c_k — неизвестная константа)
///
/// c_k определяются из условий Бредта:
///   ∮_{Γk} ∂φ/∂n ds = +2·A_k   (n — нормаль к Ω, направлена внутрь отверстия)
///
/// Расширенная СЛАУ решается методом дополнения Шура:
///   [K_ff  Col ] [φ_f]   [f_f]
///   [ColT  M   ] [c  ] = [g  ]
/// где g_k = +2·A_k + Σ_{Γk} F_i.
///
/// Момент инерции при кручении:
///   It = 2·∫∫_Ω φ dA + 2·Σ_k c_k·A_k
/// </summary>
public static class TorsionFemSolver
{
    /// <summary>
    /// Вычислить характеристики кручения. Корректно работает на многосвязных областях
    /// (полые сечения с произвольным числом отверстий).
    /// ShearCenterX/Y = NaN (φ-формулировка центр кручения не даёт).
    /// </summary>
    public static TorsionProps Solve(TorsionBoundary boundary, double maxElementSize,
        TriangulationMethod triangulation = TriangulationMethod.AdvancingFront,
        FemElementOrder order = FemElementOrder.Linear)
    {
        var mesh = MeshBuilder.Build(boundary, maxElementSize, triangulation);
        if (order == FemElementOrder.Quadratic)
            mesh = MeshBuilder.Promote(mesh, boundary);
        int ndof = mesh.NodesX.Length;
        int nHoles = mesh.HoleNodeSets.Length;

        var (K, F) = PrandtlAssembler.Assemble(mesh);

        double[] phi;
        double it;

        if (nHoles == 0)
        {
            // Односвязная область: стандартное условие Дирихле φ=0 на внешнем контуре
            var reduced = DirichletReducer.Reduce(K, F, mesh.OuterDofs, uFixed: null);
            double[] uFree = SparseLuSolver.SolveOnce(reduced.Kff, reduced.Fmod);
            phi = DirichletReducer.Expand(ndof, reduced.Free, uFree, mesh.OuterDofs, uFixed: null);
            it = TorsionPostprocessor.ComputeIt(mesh, phi);
        }
        else
        {
            // Многосвязная область: условия Бредта, Schur complement
            double[] holeAreas = ComputeHoleAreas(boundary);
            double[] ck;
            (phi, ck) = SolveBredt(mesh, K, F, holeAreas);

            it = TorsionPostprocessor.ComputeIt(mesh, phi);
            for (int k = 0; k < nHoles; k++)
                it += 2.0 * ck[k] * holeAreas[k];
        }

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
            It           = it,
            ShearCenterX = double.NaN,
            ShearCenterY = double.NaN,
            TauUnitMax   = tauMax,
            NodeX        = mesh.NodesX,
            NodeY        = mesh.NodesY,
            TauUnitField = tauUnit,
            PotentialField = phi,
            Triangles    = mesh.Triangles,
            Singular     = false,
            NElements    = mesh.Triangles.Length,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Расширенная СЛАУ с условиями Бредта (Schur complement)
    // ─────────────────────────────────────────────────────────────────────────

    private static (double[] phi, double[] ck) SolveBredt(
        TorsionMesh mesh, CooMatrix K, double[] F, double[] holeAreas)
    {
        int n = mesh.NodesX.Length;
        int nHoles = mesh.HoleNodeSets.Length;

        // Классификация узлов
        var isOuter = new bool[n];
        foreach (int d in mesh.OuterDofs) isOuter[d] = true;

        var holeOf = new int[n];   // -1 = не на дыре, k = на k-й дыре
        for (int i = 0; i < n; i++) holeOf[i] = -1;
        for (int k = 0; k < nHoles; k++)
            foreach (int d in mesh.HoleNodeSets[k]) holeOf[d] = k;

        // Свободные узлы (не на внешнем, не на дырах)
        var freeList  = new List<int>(n);
        var freeLocal = new int[n];
        for (int i = 0; i < n; i++) freeLocal[i] = -1;
        for (int i = 0; i < n; i++)
        {
            if (!isOuter[i] && holeOf[i] < 0)
            {
                freeLocal[i] = freeList.Count;
                freeList.Add(i);
            }
        }
        int nFree = freeList.Count;

        // CSC для итерации по столбцам
        var csc = K.ToCsc();

        // ── K_ff (COO) и f_free: итерируем только свободные столбцы ──────────
        var kffCoo = new CooMatrix(nFree, nFree, csc.Nnz);
        var fFree  = new double[nFree];
        for (int li = 0; li < nFree; li++)
        {
            int c = freeList[li];
            fFree[li] = F[c];
            for (int p = csc.ColPtr[c]; p < csc.ColPtr[c + 1]; p++)
            {
                int rf = freeLocal[csc.RowIdx[p]];
                if (rf >= 0)
                    kffCoo.Add(rf, li, csc.Values[p]);
            }
        }

        // ── Col[nFree × nHoles] и M[nHoles × nHoles]: столбцы отверстий ─────
        // Col[rf, k] = Σ_{j∈hole_k} K[free_rf, j]
        // M[m, k]    = Σ_{r∈hole_m, j∈hole_k} K[r, j]
        var col = new double[nFree, nHoles];
        var M   = new double[nHoles, nHoles];
        for (int k = 0; k < nHoles; k++)
        {
            foreach (int jHole in mesh.HoleNodeSets[k])
            {
                for (int p = csc.ColPtr[jHole]; p < csc.ColPtr[jHole + 1]; p++)
                {
                    int r  = csc.RowIdx[p];
                    double v = csc.Values[p];
                    int rf   = freeLocal[r];
                    int rm   = holeOf[r];
                    if (rf >= 0) col[rf, k] += v;
                    if (rm >= 0) M[rm, k]   += v;
                }
            }
        }

        // ── g_k = +2·A_k + Σ_{Γk} F_i ───────────────────────────────────────
        var g = new double[nHoles];
        for (int k = 0; k < nHoles; k++)
        {
            g[k] = 2.0 * holeAreas[k];
            foreach (int d in mesh.HoleNodeSets[k])
                g[k] += F[d];
        }

        // ── Дополнение Шура ───────────────────────────────────────────────────
        // 1. Факторизовать K_ff
        var kffSolver = new SparseLuSolver();
        kffSolver.Factorize(kffCoo.ToCsc());

        // 2. W = K_ff⁻¹ · Col  (nFree × nHoles)
        var W = new double[nFree, nHoles];
        for (int k = 0; k < nHoles; k++)
        {
            var colK = new double[nFree];
            for (int i = 0; i < nFree; i++) colK[i] = col[i, k];
            double[] wk = kffSolver.Solve(colK);
            for (int i = 0; i < nFree; i++) W[i, k] = wk[i];
        }

        // 3. z0 = K_ff⁻¹ · f_free
        double[] z0 = kffSolver.Solve(fFree);

        // 4. S = M − ColT · W  (nHoles × nHoles)
        var S = new double[nHoles, nHoles];
        for (int k = 0; k < nHoles; k++)
            for (int m = 0; m < nHoles; m++)
            {
                S[k, m] = M[k, m];
                for (int i = 0; i < nFree; i++)
                    S[k, m] -= col[i, k] * W[i, m];
            }

        // 5. g̃ = g − ColT · z0
        var gTilde = (double[])g.Clone();
        for (int k = 0; k < nHoles; k++)
            for (int i = 0; i < nFree; i++)
                gTilde[k] -= col[i, k] * z0[i];

        // 6. Решить S · ck = g̃
        double[] ck = DenseLinAlg.Solve(S, gTilde);

        // 7. φ_f = z0 − W · ck
        var phiFree = (double[])z0.Clone();
        for (int i = 0; i < nFree; i++)
            for (int k = 0; k < nHoles; k++)
                phiFree[i] -= W[i, k] * ck[k];

        // 8. Развернуть в полный вектор
        var phi = new double[n]; // outer: φ=0 (уже 0)
        for (int li = 0; li < nFree; li++)
            phi[freeList[li]] = phiFree[li];
        for (int k = 0; k < nHoles; k++)
            foreach (int d in mesh.HoleNodeSets[k])
                phi[d] = ck[k];

        return (phi, ck);
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static double[] ComputeHoleAreas(TorsionBoundary boundary)
    {
        if (boundary.Holes == null || boundary.Holes.Count == 0) return [];
        var areas = new double[boundary.Holes.Count];
        for (int k = 0; k < boundary.Holes.Count; k++)
        {
            var (x, y) = boundary.Holes[k];
            double area = 0.0;
            int m = x.Length;
            for (int i = 0; i < m; i++)
            {
                int j = (i + 1) % m;
                area += x[i] * y[j] - x[j] * y[i];
            }
            areas[k] = Math.Abs(area) * 0.5;
        }
        return areas;
    }
}
