using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>
/// Контейнер узловых граничных условий: жёсткие закрепления, вынужденные
/// смещения, линейные и нелинейные упругие опоры (одно- и двухузловые).
/// Порт <c>boundary_conditions.py: BoundaryConditions</c>.
/// </summary>
public sealed class BoundaryConditions
{
    private sealed class NlSpring
    {
        public bool IsPair;
        public int Node, Dof;
        public int NodeI, DofI, NodeJ, DofJ;
        public ISpringModel Model = null!;
        public double[,] R = null!;
    }

    private readonly IFeaMesh _mesh;
    private readonly int _n;       // DOF на узел
    private readonly int _ndof;

    private readonly Dictionary<int, double> _dirichlet = new();
    private readonly List<int> _springRows = new();
    private readonly List<int> _springCols = new();
    private readonly List<double> _springVals = new();
    private readonly List<NlSpring> _nonlinear = new();

    public BoundaryConditions(IFeaMesh mesh)
    {
        _mesh = mesh;
        _n = mesh.DofsPerNode;
        _ndof = mesh.NDof;
    }

    /// <summary>Фабрика из плоских массивов закреплённых DOF (совместимость).</summary>
    public static BoundaryConditions FromArrays(IFeaMesh mesh, int[]? fixedDofs, double[]? uFixed = null)
    {
        var bc = new BoundaryConditions(mesh);
        if (fixedDofs == null) return bc;
        for (int i = 0; i < fixedDofs.Length; i++)
            bc._dirichlet[fixedDofs[i]] = uFixed != null ? uFixed[i] : 0.0;
        return bc;
    }

    // ----- жёсткие закрепления и вынужденные смещения -----

    /// <summary>Жёстко закрепить указанные DOF узлов (u = 0).</summary>
    public BoundaryConditions Fix(IEnumerable<int> nodes, IEnumerable<int>? dofs = null)
    {
        foreach (int node in nodes)
        {
            var localDofs = dofs ?? Enumerable.Range(0, _n);
            foreach (int d in localDofs)
            {
                int g = node * _n + d;
                _dirichlet.TryAdd(g, 0.0);
            }
        }
        return this;
    }

    /// <summary>Вынужденное смещение конкретного DOF узла.</summary>
    public BoundaryConditions Prescribe(int node, int dof, double value)
    {
        _dirichlet[node * _n + dof] = value;
        return this;
    }

    // ----- линейные пружины -----

    /// <summary>Скалярная пружина жёсткостью k по DOF узла (опц. ЛСК).</summary>
    public BoundaryConditions Spring(int node, int dof, double k, NodeFrame? frame = null)
    {
        if (frame != null)
        {
            var kLocal = new double[_n, _n];
            kLocal[dof, dof] = k;
            return SpringMatrix(node, kLocal, frame);
        }
        int g = node * _n + dof;
        _springRows.Add(g); _springCols.Add(g); _springVals.Add(k);
        return this;
    }

    /// <summary>Матрица узловой жёсткости (n×n) в ЛСК узла.</summary>
    public BoundaryConditions SpringMatrix(int node, double[,] kLocal, NodeFrame? frame = null)
    {
        if (kLocal.GetLength(0) != _n || kLocal.GetLength(1) != _n)
            throw new ArgumentException($"K_local должна быть ({_n}×{_n}).");
        var r = NodeFrame.Build(frame, _n);
        var kg = Dense.MatMul(Dense.MatTMul(r, kLocal), r); // R^T·K·R
        int g0 = node * _n;
        for (int i = 0; i < _n; i++)
            for (int j = 0; j < _n; j++)
                if (kg[i, j] != 0.0)
                {
                    _springRows.Add(g0 + i); _springCols.Add(g0 + j); _springVals.Add(kg[i, j]);
                }
        return this;
    }

    /// <summary>Скалярная пружина k между DOF двух узлов.</summary>
    public BoundaryConditions SpringPair(int nodeI, int nodeJ, int dofI, int dofJ, double k)
    {
        int gi = nodeI * _n + dofI;
        int gj = nodeJ * _n + dofJ;
        AddTriplet(gi, gi, k); AddTriplet(gj, gj, k);
        AddTriplet(gi, gj, -k); AddTriplet(gj, gi, -k);
        return this;
    }

    /// <summary>Матрица жёсткости (n×n) между парой узлов в их ЛСК.</summary>
    public BoundaryConditions SpringPairMatrix(int nodeI, int nodeJ, double[,] kBlock, NodeFrame? frame = null)
    {
        if (kBlock.GetLength(0) != _n || kBlock.GetLength(1) != _n)
            throw new ArgumentException($"K_block должна быть ({_n}×{_n}).");
        var r = NodeFrame.Build(frame, _n);
        var kg = Dense.MatMul(Dense.MatTMul(r, kBlock), r);
        int gi0 = nodeI * _n, gj0 = nodeJ * _n;
        for (int i = 0; i < _n; i++)
            for (int j = 0; j < _n; j++)
            {
                double v = kg[i, j];
                if (v == 0.0) continue;
                AddTriplet(gi0 + i, gi0 + j, v);
                AddTriplet(gj0 + i, gj0 + j, v);
                AddTriplet(gi0 + i, gj0 + j, -v);
                AddTriplet(gj0 + i, gi0 + j, -v);
            }
        return this;
    }

    private void AddTriplet(int r, int c, double v)
    {
        _springRows.Add(r); _springCols.Add(c); _springVals.Add(v);
    }

    // ----- нелинейные пружины -----

    /// <summary>Нелинейная пружина на узел по одному DOF.</summary>
    public BoundaryConditions SpringNonlinear(int node, int dof, ISpringModel model, NodeFrame? frame = null)
    {
        _nonlinear.Add(new NlSpring
        {
            IsPair = false, Node = node, Dof = dof, Model = model, R = NodeFrame.Build(frame, _n),
        });
        return this;
    }

    /// <summary>Нелинейная пружина между парой DOF двух узлов (над Δu).</summary>
    public BoundaryConditions SpringPairNonlinear(int nodeI, int nodeJ, int dofI, int dofJ,
                                                  ISpringModel model, NodeFrame? frame = null)
    {
        _nonlinear.Add(new NlSpring
        {
            IsPair = true, NodeI = nodeI, DofI = dofI, NodeJ = nodeJ, DofJ = dofJ,
            Model = model, R = NodeFrame.Build(frame, _n),
        });
        return this;
    }

    // ----- свойства -----

    /// <summary>Закреплённые DOF (по возрастанию).</summary>
    public int[] FixedDofs
    {
        get
        {
            var keys = _dirichlet.Keys.ToArray();
            Array.Sort(keys);
            return keys;
        }
    }

    /// <summary>Значения предписанных смещений на FixedDofs.</summary>
    public double[] UFixed
    {
        get
        {
            var fd = FixedDofs;
            var v = new double[fd.Length];
            for (int i = 0; i < fd.Length; i++) v[i] = _dirichlet[fd[i]];
            return v;
        }
    }

    /// <summary>Есть ли нелинейные пружины.</summary>
    public bool HasNonlinearSprings => _nonlinear.Count > 0;

    // ----- сборка -----

    /// <summary>Линейная часть K_spring (COO). Пусто, если пружин нет.</summary>
    public CooMatrix AssembleKSpring()
    {
        var coo = new CooMatrix(_ndof, _ndof, _springVals.Count);
        for (int t = 0; t < _springVals.Count; t++)
            coo.Add(_springRows[t], _springCols[t], _springVals[t]);
        return coo;
    }

    /// <summary>Вектор нелинейных внутренних сил пружин F_nl(u).</summary>
    public double[] AssembleFSpringNonlinear(double[] u)
    {
        var f = new double[_ndof];
        foreach (var sp in _nonlinear)
        {
            if (!sp.IsPair)
            {
                int g0 = sp.Node * _n;
                double uLocal = LocalComponent(sp.R, u, g0, sp.Dof);
                double fLocal = sp.Model.Force(uLocal);
                for (int i = 0; i < _n; i++)
                    f[g0 + i] += sp.R[sp.Dof, i] * fLocal;
            }
            else
            {
                int gi0 = sp.NodeI * _n, gj0 = sp.NodeJ * _n;
                double uiLoc = LocalComponent(sp.R, u, gi0, sp.DofI);
                double ujLoc = LocalComponent(sp.R, u, gj0, sp.DofJ);
                double fRel = sp.Model.Force(ujLoc - uiLoc);
                for (int i = 0; i < _n; i++)
                {
                    f[gi0 + i] -= sp.R[sp.DofI, i] * fRel;
                    f[gj0 + i] += sp.R[sp.DofJ, i] * fRel;
                }
            }
        }
        return f;
    }

    /// <summary>Тангенциальная K_nl(u) нелинейных пружин (COO).</summary>
    public CooMatrix AssembleKSpringTangent(double[] u)
    {
        var coo = new CooMatrix(_ndof, _ndof);
        foreach (var sp in _nonlinear)
        {
            if (!sp.IsPair)
            {
                int g0 = sp.Node * _n;
                double uLocal = LocalComponent(sp.R, u, g0, sp.Dof);
                double kt = sp.Model.Stiffness(uLocal);
                if (kt == 0.0) continue;
                for (int i = 0; i < _n; i++)
                    for (int j = 0; j < _n; j++)
                    {
                        double v = kt * sp.R[sp.Dof, i] * sp.R[sp.Dof, j];
                        if (v != 0.0) coo.Add(g0 + i, g0 + j, v);
                    }
            }
            else
            {
                int gi0 = sp.NodeI * _n, gj0 = sp.NodeJ * _n;
                double uiLoc = LocalComponent(sp.R, u, gi0, sp.DofI);
                double ujLoc = LocalComponent(sp.R, u, gj0, sp.DofJ);
                double kt = sp.Model.Stiffness(ujLoc - uiLoc);
                if (kt == 0.0) continue;
                for (int i = 0; i < _n; i++)
                    for (int j = 0; j < _n; j++)
                    {
                        double rii = sp.R[sp.DofI, i], rij = sp.R[sp.DofI, j];
                        double rji = sp.R[sp.DofJ, i], rjj = sp.R[sp.DofJ, j];
                        coo.Add(gi0 + i, gi0 + j, kt * rii * rij);
                        coo.Add(gj0 + i, gj0 + j, kt * rji * rjj);
                        coo.Add(gi0 + i, gj0 + j, -kt * rii * rjj);
                        coo.Add(gj0 + i, gi0 + j, -kt * rji * rij);
                    }
            }
        }
        return coo;
    }

    private double LocalComponent(double[,] r, double[] u, int g0, int dof)
    {
        double s = 0.0;
        for (int k = 0; k < _n; k++) s += r[dof, k] * u[g0 + k];
        return s;
    }

    // ----- коммит/сброс -----

    /// <summary>Зафиксировать состояние stateful-моделей пружин.</summary>
    public void CommitStep(double[] u)
    {
        foreach (var sp in _nonlinear)
        {
            if (!sp.IsPair)
            {
                double uLocal = LocalComponent(sp.R, u, sp.Node * _n, sp.Dof);
                sp.Model.Commit(uLocal);
            }
            else
            {
                double uiLoc = LocalComponent(sp.R, u, sp.NodeI * _n, sp.DofI);
                double ujLoc = LocalComponent(sp.R, u, sp.NodeJ * _n, sp.DofJ);
                sp.Model.Commit(ujLoc - uiLoc);
            }
        }
    }

    /// <summary>Сбросить состояние stateful-моделей.</summary>
    public void ResetHistory()
    {
        foreach (var sp in _nonlinear)
            sp.Model.Reset();
    }

    internal IReadOnlyList<(int Node, int Dof)> NonlinearNodeDofs()
        => _nonlinear.Where(s => !s.IsPair).Select(s => (s.Node, s.Dof)).ToList();
}
