using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>
/// Коротационная (CR) формулировка для оболочек Shell3/Shell4: корректное
/// поведение при больших поворотах элемента (P-δ, lateral-torsional buckling,
/// snap-through) при умеренных деформациях внутри элемента.
/// Порт <c>corotational.py</c>.
/// </summary>
public static class ShellCorotational
{
    /// <summary>
    /// Внутренние силы и тангенциальная матрица одного CR-элемента в
    /// глобальной системе. <paramref name="uGlobal"/> — полные 6n перемещения
    /// и повороты (повороты — вектор Родригеса), не инкременты.
    /// </summary>
    public static (double[] FGlobal, double[,] KGlobal) ElementCR(
        double[][] coordsRef, IShellSectionResponse section, double[] uGlobal)
    {
        int n = coordsRef.Length;
        var (xyLocal, uLoc5, drill, p, rr) = Kinematics(coordsRef, uGlobal);
        var fLoc5 = ShellElementForces.FInternalLocal(xyLocal, section, uLoc5);
        var kLoc5 = ShellElementForces.KTangentLocal(xyLocal, section, uLoc5);

        // 5n → 6n.
        var kLoc6 = new double[6 * n, 6 * n];
        for (int i = 0; i < n; i++)
            for (int c = 0; c < 5; c++)
                for (int j = 0; j < n; j++)
                    for (int d = 0; d < 5; d++)
                        kLoc6[6 * i + c, 6 * j + d] = kLoc5[5 * i + c, 5 * j + d];
        double kDrill = DrillingStiffness(section);
        for (int i = 0; i < n; i++)
            kLoc6[6 * i + 5, 6 * i + 5] += kDrill;

        // Проекция и симметризация.
        var kProj = Dense.MatMul(Dense.MatMul(Dense.Transpose(p), kLoc6), p);
        Symmetrize(kProj);

        var t = GlobalTransform(rr, n);
        var fGlobal = ProjectToGlobal(fLoc5, drill, kDrill, p, rr);
        var kGlobal = Dense.MatMul(Dense.MatMul(t, kProj), Dense.Transpose(t));
        return (fGlobal, kGlobal);
    }

    /// <summary>Доля <see cref="IShellSectionResponse.DrillingScale"/> — жёсткость штрафа поворота вокруг нормали.</summary>
    public const double DrillingFactor = 1e-6;

    // Жёсткость поворота вокруг нормали: в F_int и K одна и та же (штраф kDrill·θ_def по деформационному повороту);
    // без неё в F моменты вокруг нормали от проектора P ничем не уравновешены — Ньютон стоит (плита Дорфмана у
    // жёстких тел оголовков колонн).
    private static double DrillingStiffness(IShellSectionResponse section) => DrillingFactor * section.DrillingScale;

    /// <summary>
    /// Только внутренние силы CR-элемента в глобальной системе (как <see cref="ElementCR"/>.FGlobal, без
    /// касательной сечения).
    /// </summary>
    public static double[] ElementFCR(double[][] coordsRef, IShellSectionResponse section, double[] uGlobal)
    {
        var (xyLocal, uLoc5, drill, p, rr) = Kinematics(coordsRef, uGlobal);
        return ProjectToGlobal(ShellElementForces.FInternalLocal(xyLocal, section, uLoc5), drill,
            DrillingStiffness(section), p, rr);
    }

    /// <summary>
    /// Обобщённые деформации центра CR-элемента в локальных осях (как у сечения при u = 0): деформационные перемещения
    /// повёрнутого базиса и проектора, затем кинематика фон Кармана локального КЭ — та же, что в <see cref="ElementFCR"/>.
    /// </summary>
    public static (double[] EpsM, double[] Kappa, double[] Gamma) CenterStrainsCR(double[][] coordsRef, double[] uGlobal)
    {
        var (xyLocal, uLoc5, _, _, _) = Kinematics(coordsRef, uGlobal);
        return ShellElementForces.CenterStrainsLocal(xyLocal, uLoc5, vonKarman: true);
    }

    /// <summary>Относительный шаг центральных разностей <see cref="ElementNumericalTangentCR"/> (к max(‖u_e‖, 1)).</summary>
    public const double NumericalTangentStep = 1e-6;

    /// <summary>
    /// Касательная CR-элемента K_T = ∂F/∂u центральными разностями по <see cref="ElementFCR"/> (с штрафом поворота
    /// вокруг нормали) — согласованная с F_int, в отличие от <see cref="ElementCR"/>.KGlobal (T·Pᵀ·K_лок·P·Tᵀ без
    /// изменения базиса и проектора). Повороты перемещаются аддитивно — как в шаге Ньютона, поэтому ∂F/∂u несимметрична
    /// (вектор поворота не сопряжён моменту): <paramref name="symmetrize"/> отбрасывает кососимметричную часть (для
    /// Холецкого) ценой сходимости — стена при 0,7·P_cr: 7 решений Ньютона против 5.
    /// </summary>
    public static double[,] ElementNumericalTangentCR(double[][] coordsRef, IShellSectionResponse section,
                                                      double[] uGlobal, double relStep = NumericalTangentStep,
                                                      bool symmetrize = false)
    {
        int m = uGlobal.Length;
        double h = relStep * Math.Max(Dense.Norm(uGlobal), 1.0);
        var ke = new double[m, m];
        var up = (double[])uGlobal.Clone();
        for (int k = 0; k < m; k++)
        {
            up[k] = uGlobal[k] + h;
            var fp = ElementFCR(coordsRef, section, up);
            up[k] = uGlobal[k] - h;
            var fm = ElementFCR(coordsRef, section, up);
            up[k] = uGlobal[k];
            for (int i = 0; i < m; i++) ke[i, k] = (fp[i] - fm[i]) / (2 * h);
        }
        if (symmetrize) Symmetrize(ke);
        return ke;
    }
    // Кинематика CR: локальные координаты узлов (n×2), деформационные 5n DOF и повороты вокруг нормали (n) после
    // проектора, проектор P (6n×6n), текущий базис Rr.
    private static (double[,] XyLocal, double[] ULoc5, double[] Drill, double[,] P, double[,] Rr) Kinematics(
        double[][] coordsRef, double[] uGlobal)
    {
        int n = coordsRef.Length;

        var disp = new double[n][];
        var thetas = new double[n][];
        for (int i = 0; i < n; i++)
        {
            disp[i] = new[] { uGlobal[6 * i], uGlobal[6 * i + 1], uGlobal[6 * i + 2] };
            thetas[i] = new[] { uGlobal[6 * i + 3], uGlobal[6 * i + 4], uGlobal[6 * i + 5] };
        }

        var coordsCur = new double[n][];
        for (int i = 0; i < n; i++)
            coordsCur[i] = Dense.AddV(coordsRef[i], disp[i]);

        var r0 = ShellGeometry.LocalFrame(coordsRef);
        var rr = ShellGeometry.LocalFrame(coordsCur);

        // Локальные координаты узлов в исходном/текущем базисах.
        var xy03d = ProjectFull(coordsRef, r0);   // (n,3)
        var xyCur3d = ProjectFull(coordsCur, rr);  // (n,3)

        // Деформационные перемещения и повороты.
        var uDef = new double[6 * n];
        for (int i = 0; i < n; i++)
        {
            for (int k = 0; k < 3; k++)
                uDef[6 * i + k] = xyCur3d[i, k] - xy03d[i, k];
            var rn = So3.Exp(thetas[i]);
            var rDef = Dense.MatMul(Dense.MatMul(rr, rn), Dense.Transpose(r0));
            var tl = So3.Log(rDef);
            uDef[6 * i + 3] = tl[0];
            uDef[6 * i + 4] = tl[1];
            uDef[6 * i + 5] = tl[2];
        }

        // Матрица проекции жёстких мод P = I − S (SᵀS)⁻¹ Sᵀ.
        var xyMean = new double[3];
        for (int i = 0; i < n; i++)
            for (int k = 0; k < 3; k++)
                xyMean[k] += xy03d[i, k] / n;
        var s = new double[6 * n, 6];
        for (int i = 0; i < n; i++)
        {
            for (int k = 0; k < 3; k++) s[6 * i + k, k] = 1.0;
            var rvec = new[] { xy03d[i, 0] - xyMean[0], xy03d[i, 1] - xyMean[1], xy03d[i, 2] - xyMean[2] };
            var sk = So3.Skew(rvec);
            for (int a = 0; a < 3; a++)
                for (int b = 0; b < 3; b++)
                    s[6 * i + a, 3 + b] = -sk[a, b];
            for (int k = 0; k < 3; k++) s[6 * i + 3 + k, 3 + k] = 1.0;
        }
        var sts = Dense.MatTMul(s, s);            // (6×6)
        var stsInv = DenseLinAlg.Inverse(sts);
        var p = ProjectionMatrix(s, stsInv, 6 * n);

        uDef = Dense.MatVec(p, uDef);

        // Локальный 5-DOF вызов фон-кармановского элемента.
        var xyLocal = new double[n, 2];
        for (int i = 0; i < n; i++) { xyLocal[i, 0] = xy03d[i, 0]; xyLocal[i, 1] = xy03d[i, 1]; }
        var uLoc5 = new double[5 * n];
        for (int i = 0; i < n; i++)
        {
            uLoc5[5 * i + 0] = uDef[6 * i + 0];
            uLoc5[5 * i + 1] = uDef[6 * i + 1];
            uLoc5[5 * i + 2] = uDef[6 * i + 2];
            uLoc5[5 * i + 3] = uDef[6 * i + 3];
            uLoc5[5 * i + 4] = uDef[6 * i + 4];
        }
        var drill = new double[n];
        for (int i = 0; i < n; i++) drill[i] = uDef[6 * i + 5];
        return (xyLocal, uLoc5, drill, p, rr);
    }

    // Локальные 5n сил и штраф поворота вокруг нормали kDrill·θ → 6n → Pᵀ → глобальные оси (блоки Rrᵀ).
    private static double[] ProjectToGlobal(double[] fLoc5, double[] drill, double kDrill, double[,] p, double[,] rr)
    {
        int n = fLoc5.Length / 5;
        var fLoc6 = new double[6 * n];
        for (int i = 0; i < n; i++)
            for (int c = 0; c < 5; c++)
                fLoc6[6 * i + c] = fLoc5[5 * i + c];
        for (int i = 0; i < n; i++) fLoc6[6 * i + 5] = kDrill * drill[i];
        return Dense.MatVec(GlobalTransform(rr, n), Dense.MatTVec(p, fLoc6));
    }

    // Трансформация в глобальную систему: блоки Rrᵀ на каждый узел.
    private static double[,] GlobalTransform(double[,] rr, int n)
    {
        var t = new double[6 * n, 6 * n];
        for (int i = 0; i < n; i++)
            for (int a = 0; a < 3; a++)
                for (int b = 0; b < 3; b++)
                {
                    t[6 * i + a, 6 * i + b] = rr[b, a];       // Rr^T
                    t[6 * i + 3 + a, 6 * i + 3 + b] = rr[b, a];
                }
        return t;
    }

    private static void Symmetrize(double[,] k)
    {
        int m = k.GetLength(0);
        for (int i = 0; i < m; i++)
            for (int j = i + 1; j < m; j++)
            {
                double avg = 0.5 * (k[i, j] + k[j, i]);
                k[i, j] = avg; k[j, i] = avg;
            }
    }

    private static double[,] ProjectFull(double[][] coords, double[,] r)
    {
        int n = coords.Length;
        var xy = new double[n, 3];
        for (int i = 0; i < n; i++)
        {
            var d = Dense.SubV(coords[i], coords[0]);
            for (int k = 0; k < 3; k++)
                xy[i, k] = d[0] * r[k, 0] + d[1] * r[k, 1] + d[2] * r[k, 2];
        }
        return xy;
    }

    private static double[,] ProjectionMatrix(double[,] s, double[,] stsInv, int m)
    {
        // P = I − S·stsInv·Sᵀ
        var sStsInv = Dense.MatMul(s, stsInv);           // (m×6)
        var p = Dense.MatMul(sStsInv, Dense.Transpose(s)); // (m×m)
        for (int i = 0; i < m; i++)
            for (int j = 0; j < m; j++)
                p[i, j] = (i == j ? 1.0 : 0.0) - p[i, j];
        return p;
    }

    // ---------------- сеточные обёртки ----------------

    /// <summary>CR-сборка вектора внутренних сил F_int(u).</summary>
    public static double[] AssembleFInternalCR(ShellMesh mesh, double[] u)
    {
        var f = new double[mesh.NDof];
        for (int e = 0; e < mesh.Elements.Length; e++)
        {
            var el = mesh.Elements[e];
            var coords = Coords(mesh, el);
            var dofs = mesh.ElementDofs(el);
            var ue = GatherDofs(u, dofs);
            var fe = ElementFCR(coords, mesh.Section(e), ue);
            for (int i = 0; i < dofs.Length; i++) f[dofs[i]] += fe[i];
        }
        return f;
    }

    /// <summary>CR-сборка тангенциальной матрицы K_T(u) (COO).</summary>
    public static CooMatrix AssembleKTangentCR(ShellMesh mesh, double[] u,
                                               bool useNumerical = false, double eps = NumericalTangentStep)
    {
        var coo = new CooMatrix(mesh.NDof, mesh.NDof);
        for (int e = 0; e < mesh.Elements.Length; e++)
        {
            var el = mesh.Elements[e];
            var coords = Coords(mesh, el);
            var dofs = mesh.ElementDofs(el);
            var ue = GatherDofs(u, dofs);
            double[,] ke;
            if (useNumerical)
                ke = ElementNumericalTangentCR(coords, mesh.Section(e), ue, eps);
            else
                ke = ElementCR(coords, mesh.Section(e), ue).KGlobal;
            coo.AddBlock(dofs, ke);
        }
        return coo;
    }

    /// <summary>Ньютоновский CR-решатель (плоский API).</summary>
    public static (double[] U, List<ShellMesh.NewtonRecord> History) SolveNonlinearCR(
        ShellMesh mesh, double[] f, int[] fixedDofs, double[]? uFixed = null,
        int nSteps = 10, double tol = 1e-6, int maxIter = 25,
        bool lineSearch = true, int maxLsSteps = 15,
        bool numericalTangent = false, bool verbose = false)
        => SolveNonlinearCR(mesh, f, BoundaryConditions.FromArrays(mesh, fixedDofs, uFixed),
                            nSteps, tol, maxIter, lineSearch, maxLsSteps, numericalTangent, verbose);

    /// <summary>Ньютоновский CR-решатель с граничными условиями.</summary>
    public static (double[] U, List<ShellMesh.NewtonRecord> History) SolveNonlinearCR(
        ShellMesh mesh, double[] f, BoundaryConditions bc,
        int nSteps = 10, double tol = 1e-6, int maxIter = 25,
        bool lineSearch = true, int maxLsSteps = 15,
        bool numericalTangent = false, bool verbose = false)
    {
        int ndof = mesh.NDof;
        var fixedDofs = bc.FixedDofs;
        var uFixedArr = bc.UFixed;
        int[] free = DirichletReducer.FreeDofs(ndof, fixedDofs);

        var u = new double[ndof];
        var history = new List<ShellMesh.NewtonRecord>();
        double fNorm = Math.Max(NormAt(f, free), 1.0);

        for (int step = 1; step <= nSteps; step++)
        {
            double lam = (double)step / nSteps;
            var fStep = Dense.ScaleV(f, lam);
            for (int t = 0; t < fixedDofs.Length; t++)
                u[fixedDofs[t]] = lam * uFixedArr[t];

            bool converged = false;
            for (int it = 1; it <= maxIter; it++)
            {
                var fInt = AssembleFInternalCR(mesh, u);
                var r = Dense.SubV(fStep, fInt);
                double resid = NormAt(r, free) / fNorm;
                bool ok = resid < tol;
                history.Add(new ShellMesh.NewtonRecord(step, it, resid, ok));
                if (verbose)
                    Console.WriteLine($"  step {step}/{nSteps}  iter {it,2}  ||r||/||F||={resid:e3}");
                if (ok) { converged = true; break; }

                var kt = AssembleKTangentCR(mesh, u, numericalTangent);
                var reduced = DirichletReducer.Reduce(kt, r, fixedDofs, null);
                var duFree = SparseLuSolver.SolveOnce(reduced.Kff, reduced.Fmod);

                if (!lineSearch)
                {
                    for (int i = 0; i < free.Length; i++) u[free[i]] += duFree[i];
                    continue;
                }

                double alpha = 1.0;
                double baseNorm = resid;
                var uBak = GatherDofs(u, free);
                bool accepted = false;
                for (int ls = 0; ls < maxLsSteps; ls++)
                {
                    for (int i = 0; i < free.Length; i++) u[free[i]] = uBak[i] + alpha * duFree[i];
                    var fIntTrial = AssembleFInternalCR(mesh, u);
                    var rTrial = Dense.SubV(fStep, fIntTrial);
                    double newNorm = NormAt(rTrial, free) / fNorm;
                    if (IsFinite(rTrial, free) && newNorm < baseNorm) { accepted = true; break; }
                    alpha *= 0.5;
                }
                if (!accepted)
                    for (int i = 0; i < free.Length; i++) u[free[i]] = uBak[i] + alpha * duFree[i];
            }
            if (verbose && !converged)
                Console.WriteLine($"  step {step}: не сошёлся за {maxIter} итераций");
            mesh.CommitStep(u);
        }
        return (u, history);
    }

    private static double[][] Coords(ShellMesh mesh, int[] el)
    {
        var coords = new double[el.Length][];
        for (int i = 0; i < el.Length; i++) coords[i] = mesh.Nodes[el[i]];
        return coords;
    }

    private static double[] GatherDofs(double[] v, int[] idx)
    {
        var r = new double[idx.Length];
        for (int i = 0; i < idx.Length; i++) r[i] = v[idx[i]];
        return r;
    }

    private static double NormAt(double[] v, int[] idx)
    {
        double s = 0.0;
        foreach (int i in idx) s += v[i] * v[i];
        return Math.Sqrt(s);
    }

    private static bool IsFinite(double[] v, int[] idx)
    {
        foreach (int i in idx) if (!double.IsFinite(v[i])) return false;
        return true;
    }
}
