using CSfea.Sparse;

namespace CSfea.Core;

// Режим «только P-δ» теории второго порядка (спека второго порядка фрагмента, срез 1а): K_σ вертикальных КЭ — по
// перемещениям без жёсткого сдвига участка между уровнями (проектированная Tᵀ·K_σ·T), остальные КЭ — без K_σ.
public sealed partial class StructuralMesh
{
    // Блок-поправка КЭ с хордой: расширенные DOF (сначала участвующие DOF КЭ — Rows в порядке ElementDofs, затем
    // поступательные DOF узлов уровней, которых среди них нет) и T = P − C (строки — Rows, столбцы — Dofs).
    private sealed record ChordBlock(int Element, int[] Rows, int[] Dofs, double[,] T);

    private ChordBlock[] _chordBlocks = [];

    /// <summary>
    /// Хорды КЭ режима «только P-δ» (единая нумерация КЭ): null у всей сетки — полный второй порядок; иначе КЭ с
    /// хордой — K_σ по перемещениям без хорды, КЭ без хорды (null) — без K_σ. См. <see cref="SetLocalSecondOrder"/>.
    /// </summary>
    public IReadOnlyList<ElementChord?>? LocalChords { get; private set; }

    /// <summary>
    /// Включить режим «только P-δ» (<paramref name="chords"/> — по КЭ единой нумерации, длина <see cref="ElementCount"/>)
    /// или вернуть полный второй порядок (null). K_σ КЭ с хордой проектируется: Tᵀ·K_σ·T, T·u = u − u_хорды
    /// (<see cref="ElementChord"/>) — жёсткий сдвиг участка между уровнями не даёт поперечной силы P-Δ; КЭ без хорды
    /// (горизонтальные) — без K_σ. Действует в <see cref="SolveSecondOrder"/>, <see cref="ImperfectionForce"/> и
    /// внутренних силах второго порядка (<see cref="ChordForce"/>); устойчивость и <see cref="AssembleKGeometric"/> — без
    /// проекции.
    /// </summary>
    public void SetLocalSecondOrder(IReadOnlyList<ElementChord?>? chords)
    {
        if (chords != null && chords.Count != ElementCount)
            throw new ArgumentException($"Хорд {chords.Count}, КЭ {ElementCount}.", nameof(chords));
        LocalChords = chords;
        _chordBlocks = chords == null ? [] : Enumerable.Range(0, ElementCount)
            .Where(e => chords[e] != null).Select(e => BuildChordBlock(e, chords[e]!)).ToArray();
        _chordBlockOf = null;
        _assembler = null;   // портрет K_ff — с блоками-поправками
    }

    /// <summary>КЭ входит в K_σ (полный второй порядок или КЭ с хордой).</summary>
    public bool HasGeometric(int e) => LocalChords == null || LocalChords[e] != null;

    /// <summary>Число блоков-поправок хорд (КЭ с хордой).</summary>
    internal int ChordBlockCount => _chordBlocks.Length;

    /// <summary>Расширенные DOF блока-поправки <paramref name="k"/>.</summary>
    internal int[] ChordBlockDofs(int k) => _chordBlocks[k].Dofs;

    private ChordBlock BuildChordBlock(int e, ElementChord ch)
    {
        var dofs = ElementDofs(e);
        bool shell = e < Shells.Count;
        int nNodes = dofs.Length / 6;
        if (ch.Bottom.Length != nNodes || ch.Top.Length != nNodes || ch.Zeta.Length != nNodes)
            throw new ArgumentException($"Хорда КЭ {e}: данных по узлам не {nNodes}.");
        // Оболочке нужны только поступательные DOF: K_σ оболочки поворотов не касается.
        var rows = Enumerable.Range(0, dofs.Length).Where(k => !shell || k % 6 < 3).ToArray();
        var cols = new List<int>(rows.Select(k => dofs[k]));
        var colOf = new Dictionary<int, int>();
        for (int i = 0; i < cols.Count; i++) colOf[cols[i]] = i;
        int Col(int dof)
        {
            if (colOf.TryGetValue(dof, out int c)) return c;
            colOf[dof] = cols.Count;
            cols.Add(dof);
            return cols.Count - 1;
        }
        var entries = new List<(int Row, int Col, double V)>();
        double[] ax = ch.Axis;
        // S·v = e × v.
        double[,] s = { { 0, -ax[2], ax[1] }, { ax[2], 0, -ax[0] }, { -ax[1], ax[0], 0 } };
        for (int r = 0; r < rows.Length; r++)
        {
            int local = rows[r], node = local / 6, comp = local % 6;
            double zeta = ch.Zeta[node];
            void Add(ChordPoint p, double factor, int k)
            {
                for (int j = 0; j < p.Nodes.Length; j++)
                    if (p.Weights[j] != 0) entries.Add((r, Col(6 * p.Nodes[j] + k), factor * p.Weights[j]));
            }
            if (comp < 3)
            {
                Add(ch.Bottom[node], 1 - zeta, comp);
                Add(ch.Top[node], zeta, comp);
            }
            else if (ch.Rotations)
            {
                int a = comp - 3;
                for (int k = 0; k < 3; k++)
                {
                    if (s[a, k] == 0) continue;
                    Add(ch.Top[node], s[a, k] / ch.Length, k);
                    Add(ch.Bottom[node], -s[a, k] / ch.Length, k);
                }
            }
        }
        var t = new double[rows.Length, cols.Count];
        for (int r = 0; r < rows.Length; r++) t[r, r] = 1.0;
        foreach (var (r, c, v) in entries) t[r, c] -= v;
        return new ChordBlock(e, rows, cols.ToArray(), t);
    }

    /// <summary>
    /// Поправка блока <paramref name="k"/> к K_σ его КЭ: ΔK = Tᵀ·G·T − Pᵀ·G·P на расширенных DOF, G — K_σ КЭ
    /// (<see cref="ElementKGeometric"/>) при <paramref name="uState"/> на участвующих DOF.
    /// </summary>
    internal double[,] ChordCorrection(int k, double[] uState, bool shellInPlane)
    {
        var b = _chordBlocks[k];
        var g = Sub(ElementKGeometric(b.Element, uState, shellInPlane), b.Rows);
        var d = Dense.MatMul(Dense.MatTMul(b.T, g), b.T);
        int n = b.Rows.Length;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++) d[i, j] -= g[i, j];
        return d;
    }

    private static double[,] Sub(double[,] m, int[] idx)
    {
        var r = new double[idx.Length, idx.Length];
        for (int i = 0; i < idx.Length; i++)
            for (int j = 0; j < idx.Length; j++) r[i, j] = m[idx[i], idx[j]];
        return r;
    }

    /// <summary>
    /// Поправка внутренних сил второго порядка режима «только P-δ»: Σ ΔK_e(N(<paramref name="uState"/>))·u_e (полные DOF);
    /// без хорд — нули. <paramref name="abs"/> (не null) — сюда добавляются модули вкладов (масштаб невязки).
    /// </summary>
    public double[] ChordForce(double[] uState, double[] u, bool shellInPlane = true, double[]? abs = null)
    {
        var f = new double[NDof];
        if (_chordBlocks.Length == 0) return f;
        var parts = new double[_chordBlocks.Length][];
        Parallel.For(0, _chordBlocks.Length, new ParallelOptions { MaxDegreeOfParallelism = MaxDegreeOfParallelism },
            k => parts[k] = BlockForce(k, uState, u, shellInPlane));
        Scatter(parts, f, abs);
        return f;
    }

    // ΔK блока k · u по его расширенным DOF.
    private double[] BlockForce(int k, double[] uState, double[] u, bool shellInPlane)
        => Dense.MatVec(ChordCorrection(k, uState, shellInPlane), Gather(u, _chordBlocks[k].Dofs));

    /// <summary>
    /// Поправка вектора несовершенства режима «только P-δ»: Σ [Tᵀ·G·T·u₀ − Pᵀ·G·u₀,e] по КЭ с хордой и своим u₀; u₀ узлов
    /// уровней, не входящих в КЭ, — из поля (поступательные u₀ любого КЭ с этим узлом). Крен (u₀ линеен по высоте участка)
    /// гасится целиком.
    /// </summary>
    public double[] ChordImperfectionForce(ImperfectionField field, double[] uState, bool shellInPlane = true, double[]? abs = null)
    {
        var f = new double[NDof];
        if (_chordBlocks.Length == 0) return f;
        var nodal = NodalImperfection(field);
        var parts = new double[_chordBlocks.Length][];
        Parallel.For(0, _chordBlocks.Length, new ParallelOptions { MaxDegreeOfParallelism = MaxDegreeOfParallelism },
            k => parts[k] = BlockImperfectionForce(k, field, nodal, uState, shellInPlane));
        Scatter(parts, f, abs);
        return f;
    }

    // Tᵀ·G·T·u₀ − Pᵀ·G·u₀,e блока k по его расширенным DOF; null — у КЭ нет u₀.
    private double[]? BlockImperfectionForce(int k, ImperfectionField field, double[] nodal, double[] uState, bool shellInPlane)
    {
        var b = _chordBlocks[k];
        int e = b.Element;
        double[]? own = e < Shells.Count ? ShellU0(field, e) : field.Beam[e - Shells.Count];
        if (own == null) return null;
        var u0 = new double[b.Dofs.Length];
        for (int r = 0; r < b.Rows.Length; r++) u0[r] = own[b.Rows[r]];
        for (int c = b.Rows.Length; c < u0.Length; c++) u0[c] = nodal[b.Dofs[c]];
        var g = Sub(ElementKGeometric(e, uState, shellInPlane), b.Rows);
        var res = Dense.MatTVec(b.T, Dense.MatVec(g, Dense.MatVec(b.T, u0)));
        var gp = Dense.MatVec(g, u0[..b.Rows.Length]);
        for (int r = 0; r < gp.Length; r++) res[r] -= gp[r];
        return res;
    }

    private Dictionary<int, int>? _chordBlockOf;
    private Tuple<ImperfectionField, double[]>? _nodalImperfection;

    // Узловые u₀ поля (кэш по ссылке на поле: поблочные силы зовутся по КЭ подряд).
    private double[] NodalImperfectionCached(ImperfectionField field)
    {
        var c = _nodalImperfection;
        if (c != null && ReferenceEquals(c.Item1, field)) return c.Item2;
        var u = NodalImperfection(field);
        _nodalImperfection = Tuple.Create(field, u);
        return u;
    }

    // Номер блока хорды КЭ e; −1 — у КЭ нет хорды.
    private int ChordBlockOf(int e)
    {
        var map = _chordBlockOf;
        if (map == null || map.Count != _chordBlocks.Length)
        {
            map = new Dictionary<int, int>(_chordBlocks.Length);
            for (int k = 0; k < _chordBlocks.Length; k++) map[_chordBlocks[k].Element] = k;
            _chordBlockOf = map;
        }
        return map.TryGetValue(e, out int kk) ? kk : -1;
    }

    /// <summary>
    /// Узлы сетки блока хорды КЭ <paramref name="e"/> (единая нумерация) вне самого КЭ — узлы уровней, в которые
    /// приходит его сила «только P-δ»; пусто — у КЭ нет хорды.
    /// </summary>
    public IReadOnlyList<int> ChordNodes(int e)
    {
        int k = ChordBlockOf(e);
        if (k < 0) return [];
        var own = new HashSet<int>(ElementDofs(e).Select(d => d / 6));
        return _chordBlocks[k].Dofs.Select(d => d / 6).Distinct().Where(n => !own.Contains(n)).ToArray();
    }

    /// <summary>
    /// Поблочная поправка внутренних сил «только P-δ» КЭ <paramref name="e"/>: ΔK_e(N(<paramref name="uState"/>))·u
    /// (+ поправка несовершенства блока, как в <see cref="ChordImperfectionForce"/>, если <paramref name="field"/> задано)
    /// по расширенным DOF блока (полные DOF сетки, в т. ч. узлов уровней вне КЭ). Пусто — у КЭ нет хорды. Сумма по КЭ
    /// равна <see cref="ChordForce"/> (+ <see cref="ChordImperfectionForce"/>).
    /// </summary>
    public IReadOnlyList<(int Dof, double F)> ElementChordForces(int e, double[] uState, double[] u, bool shellInPlane,
        ImperfectionField? field = null)
    {
        int k = ChordBlockOf(e);
        if (k < 0) return [];
        var f = BlockForce(k, uState, u, shellInPlane);
        if (field != null && BlockImperfectionForce(k, field, NodalImperfectionCached(field), uState, shellInPlane) is { } g)
            for (int i = 0; i < f.Length; i++) f[i] += g[i];
        var dofs = _chordBlocks[k].Dofs;
        var r = new (int, double)[dofs.Length];
        for (int i = 0; i < dofs.Length; i++) r[i] = (dofs[i], f[i]);
        return r;
    }

    // u₀ оболочки по 6 DOF узлов (повороты — 0).
    private double[]? ShellU0(ImperfectionField field, int e)
    {
        if (field.Shell[e] is not { } s0) return null;
        int n = Shells[e].Nodes.Length;
        var u0 = new double[6 * n];
        for (int i = 0; i < n; i++)
            for (int c = 0; c < 3; c++) u0[6 * i + c] = s0[3 * i + c];
        return u0;
    }

    // Поступательные u₀ узлов (полные DOF) по полю КЭ: оболочки, затем стержни.
    private double[] NodalImperfection(ImperfectionField field)
    {
        var u = new double[NDof];
        for (int e = 0; e < Shells.Count; e++)
        {
            if (field.Shell[e] is not { } s0) continue;
            var nodes = Shells[e].Nodes;
            for (int i = 0; i < nodes.Length; i++)
                for (int c = 0; c < 3; c++) u[6 * nodes[i] + c] = s0[3 * i + c];
        }
        for (int b = 0; b < Beams.Count; b++)
        {
            if (field.Beam[b] is not { } b0) continue;
            for (int c = 0; c < 3; c++)
            {
                u[6 * Beams[b].I + c] = b0[c];
                u[6 * Beams[b].J + c] = b0[6 + c];
            }
        }
        return u;
    }

    private void Scatter(double[]?[] parts, double[] f, double[]? abs)
    {
        for (int k = 0; k < parts.Length; k++)
        {
            if (parts[k] is not { } p) continue;
            var dofs = _chordBlocks[k].Dofs;
            for (int i = 0; i < dofs.Length; i++)
            {
                f[dofs[i]] += p[i];
                if (abs != null) abs[dofs[i]] += Math.Abs(p[i]);
            }
        }
    }
}
