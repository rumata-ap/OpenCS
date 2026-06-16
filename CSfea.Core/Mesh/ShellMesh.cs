using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>
/// Сетка оболочек: узлы (N×3), элементы (списки индексов узлов по 3 или 4),
/// сечение (ламинат или полиморфный <see cref="IShellSectionResponse"/>).
/// ndof = 6 · N. Разрежённая COO-сборка и решатели (линейный, фон Карман).
/// Порт <c>fea/assembly.py: ShellMesh</c>.
/// </summary>
public sealed class ShellMesh : IFeaMesh
{
    /// <summary>DOF на узел.</summary>
    public int DofsPerNode => 6;

    /// <summary>Координаты узлов (N строк по 3).</summary>
    public double[][] Nodes { get; }

    /// <summary>Элементы — массивы индексов узлов (3 или 4).</summary>
    public int[][] Elements { get; }

    /// <summary>Число узлов.</summary>
    public int NNodes => Nodes.Length;

    /// <summary>Полное число степеней свободы.</summary>
    public int NDof => DofsPerNode * NNodes;

    private readonly Laminate? _laminate;
    private readonly Laminate[]? _laminatesPerElement;
    private readonly IShellSectionResponse? _sectionResponse;
    private readonly IShellSectionResponse[]? _sectionResponsesPerElement;
    private readonly IShellSectionResponse?[] _sectionCache;
    private readonly bool _isLinearLaminate;

    private ShellMesh(double[][] nodes, int[][] elements,
                      Laminate? laminate, Laminate[]? laminatesPerElement,
                      IShellSectionResponse? sectionResponse,
                      IShellSectionResponse[]? sectionResponsesPerElement)
    {
        Nodes = nodes;
        Elements = elements;
        _laminate = laminate;
        _laminatesPerElement = laminatesPerElement;
        _sectionResponse = sectionResponse;
        _sectionResponsesPerElement = sectionResponsesPerElement;
        _isLinearLaminate = laminate != null || laminatesPerElement != null;
        _sectionCache = new IShellSectionResponse?[elements.Length];
    }

    /// <summary>Единый ламинат на все элементы.</summary>
    public ShellMesh(double[][] nodes, int[][] elements, Laminate laminate)
        : this(nodes, elements, laminate, null, null, null) { }

    /// <summary>Свой ламинат на каждый элемент.</summary>
    public ShellMesh(double[][] nodes, int[][] elements, Laminate[] laminatesPerElement)
        : this(nodes, elements, null, laminatesPerElement, null, null) { }

    /// <summary>Единое полиморфное сечение на все элементы.</summary>
    public ShellMesh(double[][] nodes, int[][] elements, IShellSectionResponse sectionResponse)
        : this(nodes, elements, null, null, sectionResponse, null) { }

    /// <summary>Своё полиморфное сечение на каждый элемент.</summary>
    public ShellMesh(double[][] nodes, int[][] elements, IShellSectionResponse[] responsesPerElement)
        : this(nodes, elements, null, null, null, responsesPerElement) { }

    /// <summary>Полиморфное сечение элемента (с ленивым кешем).</summary>
    public IShellSectionResponse Section(int eIdx)
    {
        var cached = _sectionCache[eIdx];
        if (cached != null) return cached;
        IShellSectionResponse resp;
        if (_sectionResponsesPerElement != null)
            resp = _sectionResponsesPerElement[eIdx];
        else if (_sectionResponse != null)
            resp = _sectionResponse;
        else if (_laminatesPerElement != null)
            resp = new LinearLaminateResponse(_laminatesPerElement[eIdx]);
        else
            resp = new LinearLaminateResponse(_laminate!);
        _sectionCache[eIdx] = resp;
        return resp;
    }

    private Laminate Lam(int eIdx)
        => _laminatesPerElement != null ? _laminatesPerElement[eIdx] : _laminate!;

    private double[][] ElementCoords(int eIdx)
    {
        var el = Elements[eIdx];
        var coords = new double[el.Length][];
        for (int i = 0; i < el.Length; i++)
            coords[i] = Nodes[el[i]];
        return coords;
    }

    /// <summary>Глобальные DOF элемента (6 на узел).</summary>
    public int[] ElementDofs(int[] el)
    {
        var dofs = new int[6 * el.Length];
        int k = 0;
        foreach (int node in el)
            for (int c = 0; c < 6; c++)
                dofs[k++] = 6 * node + c;
        return dofs;
    }

    // ---------------- разрежённая сборка ----------------

    private CooMatrix AssembleTripletwise(Func<int, double[,]> elementMatrix)
    {
        var coo = new CooMatrix(NDof, NDof, Elements.Length * 24 * 24);
        for (int e = 0; e < Elements.Length; e++)
        {
            var ke = elementMatrix(e);
            var dofs = ElementDofs(Elements[e]);
            coo.AddBlock(dofs, ke);
        }
        return coo;
    }

    /// <summary>Линейная K всей сетки (COO).</summary>
    public CooMatrix AssembleK()
    {
        return AssembleTripletwise(e =>
        {
            if (_isLinearLaminate)
                return ShellElementMatrices.ElementKLinearGlobal(ElementCoords(e), Lam(e));
            var coords = ElementCoords(e);
            var uZero = new double[6 * coords.Length];
            return ShellElementForces.ElementKTangentGlobal(coords, Section(e), uZero);
        });
    }

    /// <summary>Геометрическая K_σ(u) (только для ламинатного сечения).</summary>
    public CooMatrix AssembleKg(double[] u)
    {
        if (!_isLinearLaminate)
            throw new InvalidOperationException(
                "K_geometric отдельно строится только для ламината. " +
                "Для произвольного сечения используйте SolveNonlinear.");
        return AssembleTripletwise(e =>
        {
            var dofs = ElementDofs(Elements[e]);
            var ue = Gather(u, dofs);
            return ShellElementMatrices.ElementKGeometricGlobal(ElementCoords(e), Lam(e), ue);
        });
    }

    /// <summary>Полная тангенциальная K_T = dF_int/du. При u=0 = AssembleK().</summary>
    public CooMatrix AssembleKTangent(double[] u)
    {
        return AssembleTripletwise(e =>
        {
            var dofs = ElementDofs(Elements[e]);
            var ue = Gather(u, dofs);
            return ShellElementForces.ElementKTangentGlobal(ElementCoords(e), Section(e), ue);
        });
    }

    /// <summary>Вектор внутренних сил F_int(u) (фон Карман).</summary>
    public double[] AssembleFInternal(double[] u)
    {
        var f = new double[NDof];
        for (int e = 0; e < Elements.Length; e++)
        {
            var dofs = ElementDofs(Elements[e]);
            var ue = Gather(u, dofs);
            var fe = ShellElementForces.ElementFInternalGlobal(ElementCoords(e), Section(e), ue);
            for (int i = 0; i < dofs.Length; i++)
                f[dofs[i]] += fe[i];
        }
        return f;
    }

    /// <summary>Зафиксировать состояние сечений после успешного шага нагружения.</summary>
    public void CommitStep(double[] u)
    {
        for (int e = 0; e < Elements.Length; e++)
            Section(e).Commit();
    }

    /// <summary>Сбросить состояние всех сечений.</summary>
    public void ResetSectionHistory()
    {
        for (int e = 0; e < Elements.Length; e++)
            Section(e).Reset();
    }

    // ---------------- решатели ----------------

    /// <summary>Статика: K·u = F с граничными условиями Дирихле (плоский API).</summary>
    public double[] SolveLinear(double[] f, int[] fixedDofs, double[]? uFixed = null,
                                string method = "direct")
        => SolveLinear(f, BoundaryConditions.FromArrays(this, fixedDofs, uFixed), method);

    /// <summary>Статика: K·u = F с граничными условиями (Дирихле + линейные пружины).</summary>
    public double[] SolveLinear(double[] f, BoundaryConditions bc, string method = "direct")
    {
        if (bc.HasNonlinearSprings)
            throw new InvalidOperationException(
                "SolveLinear не поддерживает нелинейные пружины — используйте SolveNonlinear.");
        var fixedDofs = bc.FixedDofs;
        var uFixed = bc.UFixed;
        var k = AssembleK();
        var kSpring = bc.AssembleKSpring();
        if (kSpring.Count > 0)
            AppendInto(k, kSpring);
        var reduced = DirichletReducer.Reduce(k, f, fixedDofs, uFixed);
        double[] uFree = SolveReduced(reduced.Kff, reduced.Fmod, method);
        return DirichletReducer.Expand(NDof, reduced.Free, uFree, fixedDofs, uFixed);
    }

    private static double[] SolveReduced(CscMatrix kff, double[] rhs, string method)
    {
        if (method == "direct")
            return SparseLuSolver.SolveOnce(kff, rhs);
        if (method == "cg")
        {
            var res = ConjugateGradient.Solve(kff, rhs);
            if (!res.Converged)
                throw new InvalidOperationException($"CG не сошёлся (resid={res.Residual:e3}).");
            return res.X;
        }
        throw new ArgumentException($"Неизвестный метод '{method}'.");
    }

    /// <summary>Запись истории сходимости: шаг, итерация, относительная невязка.</summary>
    public readonly record struct NewtonRecord(int Step, int Iteration, double Residual);

    /// <summary>
    /// Геометрически нелинейная статика (фон Карман) методом Ньютона
    /// (плоский API закреплённых DOF).
    /// </summary>
    public (double[] U, List<NewtonRecord> History) SolveNonlinear(
        double[] f, int[] fixedDofs, double[]? uFixed = null,
        int nSteps = 10, double tol = 1e-6, int maxIter = 25,
        bool lineSearch = true, int maxLsSteps = 15,
        bool useFullTangent = true, bool verbose = false)
        => SolveNonlinear(f, BoundaryConditions.FromArrays(this, fixedDofs, uFixed),
                          nSteps, tol, maxIter, lineSearch, maxLsSteps, useFullTangent, verbose);

    /// <summary>
    /// Геометрически нелинейная статика (фон Карман) методом Ньютона с
    /// равномерными шагами нагружения, backtracking line search и
    /// граничными условиями (Дирихле + линейные/нелинейные пружины).
    /// </summary>
    public (double[] U, List<NewtonRecord> History) SolveNonlinear(
        double[] f, BoundaryConditions bc,
        int nSteps = 10, double tol = 1e-6, int maxIter = 25,
        bool lineSearch = true, int maxLsSteps = 15,
        bool useFullTangent = true, bool verbose = false)
    {
        int ndof = NDof;
        var fixedDofs = bc.FixedDofs;
        var uFixedArr = bc.UFixed;
        int[] free = DirichletReducer.FreeDofs(ndof, fixedDofs);

        var kSpringLinCoo = bc.AssembleKSpring();
        bool hasSprings = kSpringLinCoo.Count > 0 || bc.HasNonlinearSprings;
        var kSpringLinCsc = kSpringLinCoo.Count > 0 ? kSpringLinCoo.ToCsc() : null;

        var u = new double[ndof];
        var history = new List<NewtonRecord>();

        CooMatrix? kLin = null;
        if (!useFullTangent)
            kLin = AssembleK();

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
                var fInt = AssembleFInternal(u);
                AddSpringForces(fInt, u, bc, kSpringLinCsc);
                var r = Dense.SubV(fStep, fInt);
                double resid = NormAt(r, free) / fNorm;
                history.Add(new NewtonRecord(step, it, resid));
                if (verbose)
                    Console.WriteLine($"  step {step}/{nSteps}  iter {it,2}  ||r||/||F||={resid:e3}");
                if (resid < tol) { converged = true; break; }

                CooMatrix kt = useFullTangent ? AssembleKTangent(u) : CombineLinAndKg(kLin!, u);
                if (hasSprings)
                {
                    if (kSpringLinCoo.Count > 0) AppendInto(kt, kSpringLinCoo);
                    if (bc.HasNonlinearSprings) AppendInto(kt, bc.AssembleKSpringTangent(u));
                }
                // Решаем K_T[free,free]·du = r[free] (приращения на закреплённых = 0).
                var reduced = DirichletReducer.Reduce(kt, r, fixedDofs, null);
                var duFree = SparseLuSolver.SolveOnce(reduced.Kff, reduced.Fmod);

                if (!lineSearch)
                {
                    Scatter(u, free, duFree, 1.0);
                    continue;
                }

                double alpha = 1.0;
                double baseNorm = resid;
                var uBak = GatherFree(u, free);
                bool accepted = false;
                for (int ls = 0; ls < maxLsSteps; ls++)
                {
                    SetFree(u, free, uBak, duFree, alpha);
                    var fIntTrial = AssembleFInternal(u);
                    AddSpringForces(fIntTrial, u, bc, kSpringLinCsc);
                    var rTrial = Dense.SubV(fStep, fIntTrial);
                    double newNorm = NormAt(rTrial, free) / fNorm;
                    if (IsFinite(rTrial, free) && newNorm < baseNorm)
                    {
                        accepted = true;
                        break;
                    }
                    alpha *= 0.5;
                }
                if (!accepted)
                    SetFree(u, free, uBak, duFree, alpha);
            }
            if (verbose && !converged)
                Console.WriteLine($"  step {step}: не сошёлся за {maxIter} итераций");
            bc.CommitStep(u);
            CommitStep(u);
        }
        return (u, history);
    }

    /// <summary>Результат линейной задачи устойчивости.</summary>
    public readonly record struct BucklingResult(double[] LambdaCr, double[][] Modes, double[] URef);

    /// <summary>
    /// Линейная задача устойчивости (фон Карман): (K_L + λ·K_σ(u_ref))·φ = 0,
    /// где u_ref — линейное решение под референсной нагрузкой F_ref. Сводится к
    /// обобщённой задаче K·φ = λ·(−K_σ)·φ; младшие критические λ ищутся обратной
    /// степенной итерацией со сдвигом σ=0 и B-ортогональной дефляцией.
    /// Порт <c>fea/assembly.py: solve_buckling</c>.
    /// </summary>
    public BucklingResult SolveBuckling(double[] fRef, int[] fixedDofs, int nModes = 1,
                                        double[]? uFixed = null,
                                        int maxIter = 500, double tol = 1e-10)
    {
        if (!_isLinearLaminate)
            throw new InvalidOperationException(
                "SolveBuckling поддерживается только для ламинатного сечения. " +
                "Для нелинейного материала используйте SolveNonlinear.");

        var uRef = SolveLinear(fRef, fixedDofs, uFixed);
        var kCoo = AssembleK();
        var kgCoo = AssembleKg(uRef);

        int ndof = NDof;
        var zero = new double[ndof];
        var redK = DirichletReducer.Reduce(kCoo, zero, fixedDofs, null);
        var redKg = DirichletReducer.Reduce(kgCoo, zero, fixedDofs, null);
        var kff = redK.Kff;
        var bff = redKg.Kff;            // = K_σ_ff; матрица B = −K_σ_ff
        int[] free = redK.Free;
        int nf = free.Length;

        var solver = new SparseLuSolver();
        solver.Factorize(kff);

        // B·x = −K_σ_ff·x.
        double[] BMul(double[] x)
        {
            var y = bff.Multiply(x);
            for (int i = 0; i < nf; i++) y[i] = -y[i];
            return y;
        }

        var lambdas = new double[nModes];
        var modesFree = new double[nModes][];
        var rng = new Random(12345);

        for (int m = 0; m < nModes; m++)
        {
            var x = new double[nf];
            for (int i = 0; i < nf; i++) x[i] = rng.NextDouble() - 0.5;
            DeflateB(x, modesFree, bff, m, BMul);
            NormalizeB(x, BMul);

            double nu = 0.0;
            for (int it = 0; it < maxIter; it++)
            {
                var y = BMul(x);                 // y = B·x
                var z = solver.Solve(y);         // z = K⁻¹·B·x  (M·x)
                DeflateB(z, modesFree, bff, m, BMul);
                double nuNew = Dot(x, BMul(z)) / Dot(x, BMul(x)); // ⟨x,Bz⟩/⟨x,Bx⟩ = ν=1/λ
                NormalizeB(z, BMul);
                x = z;
                if (Math.Abs(nuNew - nu) <= tol * Math.Max(1.0, Math.Abs(nuNew)))
                {
                    nu = nuNew;
                    break;
                }
                nu = nuNew;
            }

            // Обобщённое отношение Рэлея: λ = ⟨x,Kx⟩/⟨x,Bx⟩.
            double lam = Dot(x, kff.Multiply(x)) / Dot(x, BMul(x));
            lambdas[m] = lam;
            modesFree[m] = x;
        }

        // Сортировка по |λ| (младшие критические первыми).
        var order = Enumerable.Range(0, nModes).OrderBy(i => Math.Abs(lambdas[i])).ToArray();
        var lamSorted = new double[nModes];
        var modes = new double[nModes][];
        for (int k = 0; k < nModes; k++)
        {
            lamSorted[k] = lambdas[order[k]];
            modes[k] = DirichletReducer.Expand(ndof, free, modesFree[order[k]], fixedDofs, null);
        }
        return new BucklingResult(lamSorted, modes, uRef);
    }

    private static void DeflateB(double[] x, double[][] modes, CscMatrix bff, int count,
                                 Func<double[], double[]> bMul)
    {
        // B-ортогонализация против уже найденных мод (моды B-нормированы).
        for (int j = 0; j < count; j++)
        {
            var v = modes[j];
            double coef = Dot(v, bMul(x));
            for (int i = 0; i < x.Length; i++) x[i] -= coef * v[i];
        }
    }

    private static void NormalizeB(double[] x, Func<double[], double[]> bMul)
    {
        double nrm = Dot(x, bMul(x));
        double s = nrm > 0 ? 1.0 / Math.Sqrt(nrm) : 1.0 / Math.Sqrt(Math.Abs(nrm) + 1e-300);
        for (int i = 0; i < x.Length; i++) x[i] *= s;
    }

    private static double Dot(double[] a, double[] b)
    {
        double s = 0.0;
        for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }

    private static void AddSpringForces(double[] fInt, double[] u, BoundaryConditions bc, CscMatrix? kSpringLin)
    {
        if (kSpringLin != null)
        {
            var ks = kSpringLin.Multiply(u);
            for (int i = 0; i < fInt.Length; i++) fInt[i] += ks[i];
        }
        if (bc.HasNonlinearSprings)
        {
            var fnl = bc.AssembleFSpringNonlinear(u);
            for (int i = 0; i < fInt.Length; i++) fInt[i] += fnl[i];
        }
    }

    private CooMatrix CombineLinAndKg(CooMatrix kLin, double[] u)
    {
        // K_L + K_σ(u): объединяем триплеты.
        var kg = AssembleKg(u);
        var combined = new CooMatrix(NDof, NDof, kLin.Count + kg.Count);
        AppendInto(combined, kLin);
        AppendInto(combined, kg);
        return combined;
    }

    private static void AppendInto(CooMatrix target, CooMatrix src)
    {
        var dense = src.ToCsc();
        for (int c = 0; c < dense.Cols; c++)
            for (int p = dense.ColPtr[c]; p < dense.ColPtr[c + 1]; p++)
                target.Add(dense.RowIdx[p], c, dense.Values[p]);
    }

    // ---------------- утилиты ----------------

    private static double[] Gather(double[] u, int[] dofs)
    {
        var r = new double[dofs.Length];
        for (int i = 0; i < dofs.Length; i++)
            r[i] = u[dofs[i]];
        return r;
    }

    private static double[] GatherFree(double[] v, int[] free)
    {
        var r = new double[free.Length];
        for (int i = 0; i < free.Length; i++)
            r[i] = v[free[i]];
        return r;
    }

    private static void Scatter(double[] u, int[] free, double[] duFree, double alpha)
    {
        for (int i = 0; i < free.Length; i++)
            u[free[i]] += alpha * duFree[i];
    }

    private static void SetFree(double[] u, int[] free, double[] uBak, double[] duFree, double alpha)
    {
        for (int i = 0; i < free.Length; i++)
            u[free[i]] = uBak[i] + alpha * duFree[i];
    }

    private static double NormAt(double[] v, int[] idx)
    {
        double s = 0.0;
        foreach (int i in idx) s += v[i] * v[i];
        return Math.Sqrt(s);
    }

    private static bool IsFinite(double[] v, int[] idx)
    {
        foreach (int i in idx)
            if (!double.IsFinite(v[i])) return false;
        return true;
    }

    // ---------------- ГУ-маски ----------------

    /// <summary>
    /// Список глобальных DOF для узлов, удовлетворяющих предикату по координатам.
    /// </summary>
    public static int[] DofsOnBoundary(double[][] nodes, Func<double[], bool> predicate,
                                       IEnumerable<int>? components = null)
    {
        var comps = components?.ToArray() ?? new[] { 0, 1, 2, 3, 4, 5 };
        var dofs = new List<int>();
        for (int nid = 0; nid < nodes.Length; nid++)
            if (predicate(nodes[nid]))
                foreach (int k in comps)
                    dofs.Add(6 * nid + k);
        return dofs.ToArray();
    }

    /// <summary>Зафиксировать все θz-DOF (индекс 5 на каждом узле).</summary>
    public static int[] FixAllDrilling(ShellMesh mesh)
    {
        var dofs = new int[mesh.NNodes];
        for (int nid = 0; nid < mesh.NNodes; nid++)
            dofs[nid] = 6 * nid + 5;
        return dofs;
    }

    /// <summary>Объединить наборы DOF (уникальные, по возрастанию).</summary>
    public static int[] UnionDofs(params int[][] sets)
    {
        var set = new SortedSet<int>();
        foreach (var s in sets)
            foreach (int d in s)
                set.Add(d);
        return set.ToArray();
    }
}
