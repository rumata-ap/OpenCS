using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>
/// Жёсткая связь ведомого узла с ведущим. <paramref name="Mask"/> — подчинённые DOF ведомого (бит i — DOF i:
/// ux, uy, uz, θx, θy, θz): для перемещения u_s,i = u_m,i + (θ_m × r)_i, для поворота θ_s,i = θ_m,i;
/// неподчинённые DOF ведомого остаются свободными.
/// </summary>
public readonly record struct RigidLink(int Master, int Slave, int Mask = RigidLink.All)
{
    /// <summary>Все 6 DOF (жёсткое тело «Beam»).</summary>
    public const int All = 0x3F;

    /// <summary>Только перемещения (жёсткое тело «Bar»): повороты ведомого свободны.</summary>
    public const int Translations = 0x07;
}

/// <summary>
/// Жёсткие тела (MPC) исключением ведомых DOF: u = T·u_red, где u_red — все DOF, кроме ведомых.
/// K_red = Tᵀ·K·T, F_red = Tᵀ·F. Связь <b>линеаризована</b> (малые повороты ведущего: θ × r),
/// поэтому в геометрически нелинейном расчёте она точна лишь при поворотах ≪ 1 рад.
/// Цепочки (ведомый одной связи — ведущий другой) и повторное подчинение DOF запрещены.
/// Сетка — 6 DOF/узел.
/// </summary>
public sealed class RigidLinks
{
    private readonly int _ndof;
    // Для каждой строки T: (индекс в u_red, коэффициент). Не ведомые DOF — одна единица.
    private readonly (int Col, double Coef)[][] _rows;
    private readonly int[] _fullToReduced;   // −1 для ведомых DOF
    private readonly int[] _reducedToFull;

    /// <summary>Связи.</summary>
    public IReadOnlyList<RigidLink> Links { get; }

    /// <summary>Размер полного пространства DOF.</summary>
    public int NDof => _ndof;

    /// <summary>Размер редуцированного пространства (без ведомых DOF).</summary>
    public int NReduced => _reducedToFull.Length;

    public RigidLinks(double[][] nodes, IReadOnlyList<RigidLink> links)
    {
        Links = links;
        _ndof = 6 * nodes.Length;
        var slaveDofs = new Dictionary<int, (int Master, int Comp)>();
        var masters = new HashSet<int>();
        var slaves = new HashSet<int>();
        foreach (var l in links)
        {
            if (l.Master == l.Slave)
                throw new ArgumentException($"Жёсткая связь узла {l.Master} с самим собой.");
            if (l.Master < 0 || l.Master >= nodes.Length || l.Slave < 0 || l.Slave >= nodes.Length)
                throw new ArgumentException($"Жёсткая связь {l.Master}→{l.Slave}: узел вне диапазона.");
            masters.Add(l.Master);
            slaves.Add(l.Slave);
            if ((l.Mask & RigidLink.All) == 0 || (l.Mask & ~RigidLink.All) != 0)
                throw new ArgumentException($"Жёсткая связь {l.Master}→{l.Slave}: недопустимая маска DOF 0x{l.Mask:X}.");
            for (int c = 0; c < 6; c++)
                if ((l.Mask & (1 << c)) != 0 && !slaveDofs.TryAdd(6 * l.Slave + c, (l.Master, c)))
                    throw new ArgumentException(
                        $"DOF {c} узла {l.Slave} подчинён нескольким жёстким связям.");
        }
        foreach (int s in slaves)
            if (masters.Contains(s))
                throw new ArgumentException(
                    $"Цепочка жёстких связей через узел {s} (он ведомый и ведущий одновременно) не поддерживается — " +
                    "подчините все узлы тела одному ведущему.");

        _fullToReduced = new int[_ndof];
        var red = new List<int>(_ndof);
        for (int i = 0; i < _ndof; i++)
        {
            if (slaveDofs.ContainsKey(i)) { _fullToReduced[i] = -1; continue; }
            _fullToReduced[i] = red.Count;
            red.Add(i);
        }
        _reducedToFull = red.ToArray();

        _rows = new (int, double)[_ndof][];
        for (int i = 0; i < _ndof; i++)
        {
            if (!slaveDofs.TryGetValue(i, out var sm)) { _rows[i] = new[] { (_fullToReduced[i], 1.0) }; continue; }
            int m = sm.Master, s = i / 6, c = sm.Comp;
            int R(int comp) => _fullToReduced[6 * m + comp];
            if (c >= 3) { _rows[i] = new[] { (R(c), 1.0) }; continue; }
            double rx = nodes[s][0] - nodes[m][0], ry = nodes[s][1] - nodes[m][1], rz = nodes[s][2] - nodes[m][2];
            // u_s = u_m + θ × r: (θy·rz − θz·ry, θz·rx − θx·rz, θx·ry − θy·rx).
            _rows[i] = c switch
            {
                0 => new[] { (R(0), 1.0), (R(4), rz), (R(5), -ry) },
                1 => new[] { (R(1), 1.0), (R(5), rx), (R(3), -rz) },
                _ => new[] { (R(2), 1.0), (R(3), ry), (R(4), -rx) },
            };
        }
    }

    /// <summary>Является ли полный DOF ведомым.</summary>
    public bool IsSlave(int dof) => _fullToReduced[dof] < 0;

    /// <summary>Индекс полного DOF в редуцированном пространстве (−1 для ведомого).</summary>
    public int ToReducedIndex(int dof) => _fullToReduced[dof];

    /// <summary>Строка T для полного DOF: (индекс в u_red, коэффициент).</summary>
    public IReadOnlyList<(int Col, double Coef)> Row(int dof) => _rows[dof];

    /// <summary>K_red = Tᵀ·K·T.</summary>
    public CooMatrix ReduceMatrix(CooMatrix k)
    {
        var csc = k.ToCsc();
        var res = new CooMatrix(NReduced, NReduced, csc.Nnz);
        for (int c = 0; c < csc.Cols; c++)
        {
            var rowC = _rows[c];
            for (int p = csc.ColPtr[c]; p < csc.ColPtr[c + 1]; p++)
            {
                double v = csc.Values[p];
                if (v == 0.0) continue;
                foreach (var (a, ta) in _rows[csc.RowIdx[p]])
                    foreach (var (b, tb) in rowC)
                        res.Add(a, b, ta * v * tb);
            }
        }
        return res;
    }

    /// <summary>F_red = Tᵀ·F.</summary>
    public double[] ReduceVector(double[] f)
    {
        var r = new double[NReduced];
        for (int i = 0; i < _ndof; i++)
        {
            double fi = f[i];
            if (fi == 0.0) continue;
            foreach (var (a, t) in _rows[i]) r[a] += t * fi;
        }
        return r;
    }

    /// <summary>u = T·u_red.</summary>
    public double[] Expand(double[] uRed)
    {
        var u = new double[_ndof];
        for (int i = 0; i < _ndof; i++)
        {
            double s = 0.0;
            foreach (var (a, t) in _rows[i]) s += t * uRed[a];
            u[i] = s;
        }
        return u;
    }

    /// <summary>Сужение полного вектора на редуцированные DOF (без пересчёта ведомых).</summary>
    public double[] Restrict(double[] u)
    {
        var r = new double[NReduced];
        for (int a = 0; a < r.Length; a++) r[a] = u[_reducedToFull[a]];
        return r;
    }

    /// <summary>Перевести закреплённые DOF в редуцированное пространство; ведомый DOF закреплять нельзя.</summary>
    public int[] ReduceFixedDofs(int[] fixedDofs)
    {
        var r = new int[fixedDofs.Length];
        for (int t = 0; t < fixedDofs.Length; t++)
        {
            int d = fixedDofs[t];
            if (IsSlave(d))
                throw new InvalidOperationException(
                    $"Закреплён DOF {d % 6} узла {d / 6}, подчинённый жёсткой связи; закрепите ведущий узел.");
            r[t] = _fullToReduced[d];
        }
        return r;
    }
}
