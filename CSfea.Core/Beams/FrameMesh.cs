using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>
/// Базовая рама из балочных элементов: общая COO-сборка и шаговый
/// Ньютоновский CR-решатель. Конкретные элементные матрицы определяют
/// потомки (2D/3D). Порт общей части <c>beam_corotational.py: FrameMesh*</c>.
/// </summary>
public abstract class FrameMeshBase : IFeaMesh
{
    /// <summary>Координаты узлов (N строк: 2 для 2D, 3 для 3D).</summary>
    public double[][] Nodes { get; }

    /// <summary>Элементы — пары индексов узлов.</summary>
    public (int I, int J)[] Elements { get; }

    /// <summary>Отклики сечений по элементам.</summary>
    protected IBeamSectionResponse[] Sections { get; }

    public int DofsPerNode { get; }
    public int NNodes => Nodes.Length;
    public int NDof => DofsPerNode * NNodes;

    protected FrameMeshBase(double[][] nodes, (int, int)[] elements,
                            IBeamSectionResponse[] sections, int dofsPerNode)
    {
        Nodes = nodes;
        Elements = elements;
        Sections = sections;
        DofsPerNode = dofsPerNode;
    }

    /// <summary>Отклик сечения элемента.</summary>
    public IBeamSectionResponse Section(int eIdx) => Sections[eIdx];

    /// <summary>Глобальные DOF элемента.</summary>
    public int[] ElementDofs((int I, int J) elem)
    {
        int n = DofsPerNode;
        var dofs = new int[2 * n];
        for (int k = 0; k < n; k++) { dofs[k] = n * elem.I + k; dofs[n + k] = n * elem.J + k; }
        return dofs;
    }

    /// <summary>Координаты концов элемента.</summary>
    protected double[][] ElementCoords((int I, int J) elem)
        => new[] { Nodes[elem.I], Nodes[elem.J] };

    // абстрактные элементные матрицы
    protected abstract double[,] ElementKLinear(int eIdx);
    protected abstract double[] ElementFInternalCR(int eIdx, double[] ue);
    protected abstract double[,] ElementKTangentCR(int eIdx, double[] ue, bool numerical);

    /// <summary>Числовая тангенциальная матрица по умолчанию (для 3D).</summary>
    protected virtual bool DefaultNumericalTangent => true;

    /// <summary>Линейная глобальная K (COO).</summary>
    public CooMatrix AssembleK()
    {
        var coo = new CooMatrix(NDof, NDof);
        for (int e = 0; e < Elements.Length; e++)
            coo.AddBlock(ElementDofs(Elements[e]), ElementKLinear(e));
        return coo;
    }

    /// <summary>Вектор внутренних сил F_int(u) (CR).</summary>
    public double[] AssembleFInternal(double[] u)
    {
        var f = new double[NDof];
        for (int e = 0; e < Elements.Length; e++)
        {
            var dofs = ElementDofs(Elements[e]);
            var fe = ElementFInternalCR(e, Gather(u, dofs));
            for (int i = 0; i < dofs.Length; i++) f[dofs[i]] += fe[i];
        }
        return f;
    }

    /// <summary>Тангенциальная матрица K_T(u) (COO).</summary>
    public CooMatrix AssembleKTangent(double[] u, bool numerical)
    {
        var coo = new CooMatrix(NDof, NDof);
        for (int e = 0; e < Elements.Length; e++)
        {
            var dofs = ElementDofs(Elements[e]);
            coo.AddBlock(dofs, ElementKTangentCR(e, Gather(u, dofs), numerical));
        }
        return coo;
    }

    /// <summary>Коммит состояния сечений (для stateful-моделей).</summary>
    public void CommitStep(double[] u)
    {
        var seen = new HashSet<IBeamSectionResponse>(ReferenceEqualityComparer.Instance);
        foreach (var resp in Sections)
            if (seen.Add(resp)) resp.Commit();
    }

    // ---------------- решатели ----------------

    /// <summary>Линейная статика (плоский API).</summary>
    public double[] SolveLinear(double[] f, int[] fixedDofs, double[]? uFixed = null)
        => SolveLinear(f, BoundaryConditions.FromArrays(this, fixedDofs, uFixed));

    /// <summary>Линейная статика с граничными условиями (Дирихле + линейные пружины).</summary>
    public double[] SolveLinear(double[] f, BoundaryConditions bc)
    {
        if (bc.HasNonlinearSprings)
            throw new InvalidOperationException(
                "SolveLinear не поддерживает нелинейные пружины — используйте SolveNonlinearCR.");
        var k = AssembleK();
        var kSpring = bc.AssembleKSpring();
        if (kSpring.Count > 0) AppendInto(k, kSpring);
        var reduced = DirichletReducer.Reduce(k, f, bc.FixedDofs, bc.UFixed);
        var uFree = SparseLuSolver.SolveOnce(reduced.Kff, reduced.Fmod);
        return DirichletReducer.Expand(NDof, reduced.Free, uFree, bc.FixedDofs, bc.UFixed);
    }

    /// <summary>Шаговый Ньютоновский CR-решатель (плоский API).</summary>
    public (double[] U, List<NonlinearStepRecord> Records) SolveNonlinearCR(
        double[] f, int[] fixedDofs, double[]? uFixed = null,
        int nSteps = 5, double tol = 1e-6, int maxIter = 30,
        bool? numericalTangent = null, bool lineSearch = false, int maxLs = 10, bool verbose = false)
        => SolveNonlinearCR(f, BoundaryConditions.FromArrays(this, fixedDofs, uFixed),
                            nSteps, tol, maxIter, numericalTangent, lineSearch, maxLs, verbose);

    /// <summary>Шаговый Ньютоновский CR-решатель с граничными условиями.</summary>
    public (double[] U, List<NonlinearStepRecord> Records) SolveNonlinearCR(
        double[] f, BoundaryConditions bc,
        int nSteps = 5, double tol = 1e-6, int maxIter = 30,
        bool? numericalTangent = null, bool lineSearch = false, int maxLs = 10, bool verbose = false)
    {
        bool numerical = numericalTangent ?? DefaultNumericalTangent;
        int ndof = NDof;
        var fixedDofs = bc.FixedDofs;
        var uFixedArr = bc.UFixed;
        int[] free = DirichletReducer.FreeDofs(ndof, fixedDofs);

        var kSpringLinCoo = bc.AssembleKSpring();
        var kSpringLinCsc = kSpringLinCoo.Count > 0 ? kSpringLinCoo.ToCsc() : null;

        var u = new double[ndof];
        var records = new List<NonlinearStepRecord>();

        for (int kStep = 1; kStep <= nSteps; kStep++)
        {
            double lam = (double)kStep / nSteps;
            var fStep = Dense.ScaleV(f, lam);
            for (int t = 0; t < fixedDofs.Length; t++) u[fixedDofs[t]] = lam * uFixedArr[t];

            var residuals = new List<double>();
            bool converged = false;
            int it = 0;
            for (; it < maxIter; it++)
            {
                var fInt = AssembleFInternal(u);
                AddSpringForces(fInt, u, bc, kSpringLinCsc);
                var r = Dense.SubV(fStep, fInt);
                double rNorm = NormAt(r, free);
                residuals.Add(rNorm);
                double refNorm = Math.Max(NormAt(fStep, free), 1e-14);
                if (verbose)
                    Console.WriteLine($"  step {kStep}/{nSteps}  iter {it}  ||r||/||F||={rNorm / refNorm:e3}");
                if (rNorm < tol * refNorm) { converged = true; break; }

                var kt = AssembleKTangent(u, numerical);
                if (kSpringLinCoo.Count > 0) AppendInto(kt, kSpringLinCoo);
                if (bc.HasNonlinearSprings) AppendInto(kt, bc.AssembleKSpringTangent(u));
                var reduced = DirichletReducer.Reduce(kt, r, fixedDofs, null);
                double[] duF;
                try { duF = SparseLuSolver.SolveOnce(reduced.Kff, reduced.Fmod); }
                catch (InvalidOperationException) { break; }
                if (!IsFinite(duF)) break;

                if (lineSearch)
                {
                    double alpha = 1.0;
                    var uBak = Gather(u, free);
                    for (int ls = 0; ls < maxLs; ls++)
                    {
                        for (int i = 0; i < free.Length; i++) u[free[i]] = uBak[i] + alpha * duF[i];
                        var fTrial = AssembleFInternal(u);
                        AddSpringForces(fTrial, u, bc, kSpringLinCsc);
                        double rNew = NormAt(Dense.SubV(fStep, fTrial), free);
                        if (rNew < rNorm || alpha < 1e-4) break;
                        alpha *= 0.5;
                    }
                }
                else
                {
                    for (int i = 0; i < free.Length; i++) u[free[i]] += duF[i];
                }
            }
            records.Add(new NonlinearStepRecord(lam, (double[])u.Clone(), it + 1, converged, residuals));
            bc.CommitStep(u);
            CommitStep(u);
        }
        return (u, records);
    }

    // ---------------- утилиты ----------------

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

    private static void AppendInto(CooMatrix target, CooMatrix src)
    {
        var csc = src.ToCsc();
        for (int c = 0; c < csc.Cols; c++)
            for (int p = csc.ColPtr[c]; p < csc.ColPtr[c + 1]; p++)
                target.Add(csc.RowIdx[p], c, csc.Values[p]);
    }

    private static double[] Gather(double[] v, int[] idx)
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

    private static bool IsFinite(double[] v)
    {
        foreach (double x in v) if (!double.IsFinite(x)) return false;
        return true;
    }

    /// <summary>Нормализовать сечения (BeamSection/отклик, одно или список).</summary>
    protected static IBeamSectionResponse[] NormalizeSections(
        IBeamSectionResponse[]? responses, BeamSection? singleSection,
        BeamSection[]? sectionsPerElement, int nElements)
    {
        if (responses != null)
        {
            if (responses.Length != nElements)
                throw new ArgumentException("len(section) должен совпадать с числом элементов.");
            return responses;
        }
        if (sectionsPerElement != null)
        {
            if (sectionsPerElement.Length != nElements)
                throw new ArgumentException("len(section) должен совпадать с числом элементов.");
            return sectionsPerElement.Select(s => (IBeamSectionResponse)new LinearBeamResponse(s)).ToArray();
        }
        var resp = (IBeamSectionResponse)new LinearBeamResponse(singleSection!);
        var arr = new IBeamSectionResponse[nElements];
        for (int i = 0; i < nElements; i++) arr[i] = resp;
        return arr;
    }
}

/// <summary>Плоская рама (3 DOF/узел): [u, v, θz].
/// Порт <c>beam_corotational.py: FrameMesh2D</c>.</summary>
public sealed class FrameMesh2D : FrameMeshBase
{
    public FrameMesh2D(double[][] nodes, (int, int)[] elements, BeamSection section)
        : base(nodes, elements, NormalizeSections(null, section, null, elements.Length), 3) { }

    public FrameMesh2D(double[][] nodes, (int, int)[] elements, BeamSection[] sectionsPerElement)
        : base(nodes, elements, NormalizeSections(null, null, sectionsPerElement, elements.Length), 3) { }

    public FrameMesh2D(double[][] nodes, (int, int)[] elements, IBeamSectionResponse[] responses)
        : base(nodes, elements, NormalizeSections(responses, null, null, elements.Length), 3) { }

    protected override bool DefaultNumericalTangent => false;

    protected override double[,] ElementKLinear(int e)
        => BeamElements.Beam2dKGlobal(ElementCoords(Elements[e]), Section(e));

    protected override double[] ElementFInternalCR(int e, double[] ue)
        => BeamCorotational.Beam2dInternalForce(ElementCoords(Elements[e]), Section(e), ue);

    protected override double[,] ElementKTangentCR(int e, double[] ue, bool numerical)
        => BeamCorotational.Beam2dTangent(ElementCoords(Elements[e]), Section(e), ue);
}

/// <summary>Пространственная рама (6 DOF/узел).
/// Порт <c>beam_corotational.py: FrameMesh3D</c>.</summary>
public sealed class FrameMesh3D : FrameMeshBase
{
    private readonly double[]?[] _refs;

    public FrameMesh3D(double[][] nodes, (int, int)[] elements, BeamSection section, double[]? refVec = null)
        : base(nodes, elements, NormalizeSections(null, section, null, elements.Length), 6)
    {
        _refs = MakeRefs(refVec, elements.Length);
    }

    public FrameMesh3D(double[][] nodes, (int, int)[] elements, IBeamSectionResponse[] responses, double[]? refVec = null)
        : base(nodes, elements, NormalizeSections(responses, null, null, elements.Length), 6)
    {
        _refs = MakeRefs(refVec, elements.Length);
    }

    private static double[]?[] MakeRefs(double[]? refVec, int n)
    {
        var refs = new double[]?[n];
        for (int i = 0; i < n; i++) refs[i] = refVec;
        return refs;
    }

    private double[]? Ref(int e) => _refs[e];

    /// <summary>Reference-вектор ориентации сечения для элемента e.</summary>
    public double[]? RefVec(int e) => _refs[e];

    protected override double[,] ElementKLinear(int e)
        => BeamElements.Beam3dKGlobal(ElementCoords(Elements[e]), Section(e), Ref(e));

    protected override double[] ElementFInternalCR(int e, double[] ue)
        => BeamCorotational.Beam3dInternalForce(ElementCoords(Elements[e]), Section(e), ue, Ref(e));

    protected override double[,] ElementKTangentCR(int e, double[] ue, bool numerical)
        => BeamCorotational.Beam3dTangent(ElementCoords(Elements[e]), Section(e), ue, Ref(e), numerical);
}
