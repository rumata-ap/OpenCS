using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>
/// Сборка K_ff (свободные DOF пространства решения — после жёстких связей и закреплений) сразу в CSC по постоянному
/// портрету. Символика — один раз на сетку и набор закреплений: портрет (строки по возрастанию, явные нули
/// сохраняются — портрет не зависит от значений) и ячейка CSC для каждого коэффициента каждого КЭ. Каждая сборка —
/// матрицы КЭ параллельно пачками и последовательная раскладка в фиксированном порядке (результат не зависит от числа
/// потоков), без промежуточных COO. Пружины — тем же способом. Только при нулевых заданных перемещениях.
/// КЭ с линейными сечениями (<see cref="StructuralMesh.ElementHasConstantStiffness"/>) складываются один раз в
/// отдельный массив; каждая сборка копирует его и добавляет только переменные КЭ (в секущем расчёте — ЖБ-сечения).
/// </summary>
internal sealed class KffAssembler
{
    private const int Batch = 2048;   // КЭ в пачке параллельного расчёта матриц

    private readonly StructuralMesh _mesh;
    private readonly BoundaryConditions _bc;
    private readonly int[] _fixedSys;
    private readonly int _springCount;
    private readonly int[] _colPtr;
    private readonly int[] _rowIdx;
    private readonly double[] _values;

    // Вклады КЭ: для КЭ без ведомых DOF — n² ячеек подряд (порядок (a, b) по строкам ke), −1 — закреплённая.
    // Для КЭ с ведомыми DOF — тройки (ячейка, индекс a·n + b, коэффициент tₐ·t_b).
    private readonly long[] _elemOffset;
    private readonly long[] _linkedStart;   // начало троек КЭ с ведомыми DOF в _linkedAb/_linkedCoef
    private readonly bool[] _elemLinked;

    // Вклад КЭ с постоянной матрицей (линейные сечения) — суммируется один раз; _constRecords — записи КЭ, по которым
    // он собран (null — КЭ переменный). Сменилась запись или признак постоянства — вклад пересобирается.
    private double[]? _constValues;
    private object?[]? _constRecords;
    private int[] _variable = [];
    private readonly int[] _slots;
    private readonly int[] _linkedAb;
    private readonly double[] _linkedCoef;
    private readonly int[] _springSlots;     // по элементам CSC пружин: ячейка или −1
    private readonly int[] _springLinkPtr;   // разбивка ячеек пружин по элементам (с учётом связей)
    private readonly double[] _springCoef;

    /// <summary>Свободные DOF пространства решения по порядку неизвестных K_ff.</summary>
    public int[] Free { get; }

    private KffAssembler(StructuralMesh mesh, BoundaryConditions bc, int[] fixedSys)
    {
        _mesh = mesh;
        _bc = bc;
        _fixedSys = (int[])fixedSys.Clone();
        var links = mesh.Links;
        int nSys = links?.NReduced ?? mesh.NDof;
        Free = DirichletReducer.FreeDofs(nSys, fixedSys);
        var sysToFree = new int[nSys];
        Array.Fill(sysToFree, -1);
        for (int i = 0; i < Free.Length; i++) sysToFree[Free[i]] = i;
        int nf = Free.Length;

        // Строка T полного DOF в свободных неизвестных: (неизвестная или −1, коэффициент).
        (int Col, double Coef)[] Row(int dof)
        {
            if (links == null) return [(sysToFree[dof], 1.0)];
            var r = links.Row(dof);
            var res = new (int, double)[r.Count];
            for (int i = 0; i < r.Count; i++) res[i] = (sysToFree[r[i].Col], r[i].Coef);
            return res;
        }
        bool IsLinked(int[] dofs) => links != null && dofs.Any(d => links.IsSlave(d));

        int nElem = mesh.ElementCount;
        var springs = bc.AssembleKSpring().ToCsc();
        _springCount = springs.Nnz;

        // Проход 1: число вкладов по столбцам (с повторами) — для портрета.
        var colCount = new int[nf + 1];
        void Visit(int[] dofs, Action<int, int, int, double> each)
        {
            int n = dofs.Length;
            var rows = new (int Col, double Coef)[n][];
            for (int a = 0; a < n; a++) rows[a] = Row(dofs[a]);
            for (int a = 0; a < n; a++)
                for (int b = 0; b < n; b++)
                    foreach (var (ra, ta) in rows[a])
                        foreach (var (rb, tb) in rows[b])
                            each(a * n + b, ra, rb, ta * tb);
        }
        for (int e = 0; e < nElem; e++)
            Visit(mesh.ElementDofs(e), (_, r, c, _) => { if (r >= 0 && c >= 0) colCount[c + 1]++; });
        VisitSprings((r, c, _) => { if (r >= 0 && c >= 0) colCount[c + 1]++; });
        for (int c = 0; c < nf; c++) colCount[c + 1] += colCount[c];

        // Проход 2: строки по столбцам, сортировка и уникальность → портрет.
        var raw = new int[colCount[nf]];
        var next = (int[])colCount.Clone();
        for (int e = 0; e < nElem; e++)
            Visit(mesh.ElementDofs(e), (_, r, c, _) => { if (r >= 0 && c >= 0) raw[next[c]++] = r; });
        VisitSprings((r, c, _) => { if (r >= 0 && c >= 0) raw[next[c]++] = r; });
        _colPtr = new int[nf + 1];
        int nz = 0;
        for (int c = 0; c < nf; c++)
        {
            int s = colCount[c], len = colCount[c + 1] - s;
            Array.Sort(raw, s, len);
            int start = nz;
            for (int p = s; p < s + len; p++)
                if (nz == start || raw[nz - 1] != raw[p]) raw[nz++] = raw[p];
            _colPtr[c + 1] = nz;
        }
        _rowIdx = raw.AsSpan(0, nz).ToArray();
        raw = null!;
        _values = new double[nz];

        int Slot(int r, int c)
        {
            if (r < 0 || c < 0) return -1;
            int i = Array.BinarySearch(_rowIdx, _colPtr[c], _colPtr[c + 1] - _colPtr[c], r);
            if (i < 0) throw new InvalidOperationException("Сборка K_ff: вклад вне портрета (внутренняя ошибка).");
            return i;
        }

        // Проход 3: ячейки вкладов КЭ.
        _elemOffset = new long[nElem + 1];
        _linkedStart = new long[nElem];
        _elemLinked = new bool[nElem];
        long plain = 0, linked = 0;
        for (int e = 0; e < nElem; e++)
        {
            var dofs = mesh.ElementDofs(e);
            _elemLinked[e] = IsLinked(dofs);
            if (!_elemLinked[e]) plain += dofs.Length * dofs.Length;
            else Visit(dofs, (_, _, _, _) => linked++);
        }
        _slots = new int[checked((int)(plain + linked))];
        _linkedAb = new int[linked];
        _linkedCoef = new double[linked];
        long q = 0, lq = 0;
        for (int e = 0; e < nElem; e++)
        {
            _elemOffset[e] = q;
            _linkedStart[e] = lq;
            var dofs = mesh.ElementDofs(e);
            if (!_elemLinked[e])
            {
                int n = dofs.Length;
                var f = new int[n];
                for (int a = 0; a < n; a++) f[a] = Row(dofs[a])[0].Col;   // ведущий DOF: одна строка T, коэффициент 1
                for (int a = 0; a < n; a++)
                    for (int b = 0; b < n; b++)
                        _slots[q++] = Slot(f[a], f[b]);
            }
            else
                Visit(dofs, (ab, r, c, t) =>
                {
                    _slots[q++] = Slot(r, c);
                    _linkedAb[lq] = ab;
                    _linkedCoef[lq++] = t;
                });
        }
        _elemOffset[nElem] = q;

        // Пружины: элементы CSC пружин по порядку, каждый — со своими ячейками (связи — веером).
        var sSlots = new List<int>();
        var sCoef = new List<double>();
        _springLinkPtr = new int[_springCount + 1];
        int k = 0;
        VisitSprings((r, c, t) =>
        {
            sSlots.Add(Slot(r, c));
            sCoef.Add(t);
        }, onEntryEnd: () => _springLinkPtr[++k] = sSlots.Count);
        _springSlots = sSlots.ToArray();
        _springCoef = sCoef.ToArray();

        void VisitSprings(Action<int, int, double> each, Action? onEntryEnd = null)
        {
            for (int c = 0; c < springs.Cols; c++)
                for (int p = springs.ColPtr[c]; p < springs.ColPtr[c + 1]; p++)
                {
                    foreach (var (ra, ta) in Row(springs.RowIdx[p]))
                        foreach (var (rb, tb) in Row(c))
                            each(ra, rb, ta * tb);
                    onEntryEnd?.Invoke();
                }
        }
    }

    /// <summary>Символика для сетки, ГУ и закреплений (в пространстве решения).</summary>
    public static KffAssembler Build(StructuralMesh mesh, BoundaryConditions bc, int[] fixedSys) => new(mesh, bc, fixedSys);

    /// <summary>Подходит ли символика: те же ГУ, закрепления и число элементов пружин.</summary>
    public bool Matches(BoundaryConditions bc, int[] fixedSys) =>
        ReferenceEquals(bc, _bc) && fixedSys.AsSpan().SequenceEqual(_fixedSys) && bc.AssembleKSpring().ToCsc().Nnz == _springCount;

    /// <summary>
    /// Числовая сборка K_ff. Возвращаемая матрица делит портрет и массив значений со сборщиком — действительна до
    /// следующего вызова.
    /// </summary>
    public CscMatrix Assemble(int maxDegreeOfParallelism)
    {
        var po = new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism };
        if (!ConstantPartValid()) BuildConstantPart(po);
        Array.Copy(_constValues!, _values, _values.Length);
        AddElements(_variable, _values, po, _mesh.ElementK);
        AddSprings();
        return new CscMatrix(Free.Length, Free.Length, _colPtr, _rowIdx, _values);
    }

    /// <summary>
    /// Числовая сборка K_ff по заданным матрицам КЭ единой нумерации (например, касательным K_T(u); все КЭ — без
    /// кэша постоянного вклада) и линейным пружинам ГУ. Матрицы КЭ должны быть симметричны, если K_ff идёт в Холецкий;
    /// <paramref name="elementMatrix"/> вызывается из нескольких потоков. Возвращаемая матрица — как у <see cref="Assemble"/>.
    /// </summary>
    public CscMatrix AssembleWith(Func<int, double[,]> elementMatrix, int maxDegreeOfParallelism)
    {
        var po = new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism };
        Array.Clear(_values);
        AddElements(Enumerable.Range(0, _mesh.ElementCount).ToArray(), _values, po, elementMatrix);
        AddSprings();
        return new CscMatrix(Free.Length, Free.Length, _colPtr, _rowIdx, _values);
    }

    private void AddSprings()
    {
        var springs = _bc.AssembleKSpring().ToCsc();
        for (int p = 0; p < springs.Nnz; p++)
            for (int t = _springLinkPtr[p]; t < _springLinkPtr[p + 1]; t++)
            {
                int s = _springSlots[t];
                if (s >= 0) _values[s] += _springCoef[t] * springs.Values[p];
            }
    }

    /// <summary>Число КЭ, пересобираемых при каждой сборке (переменные матрицы); остальные — из кэша.</summary>
    public int VariableElements => _variable.Length;

    private bool ConstantPartValid()
    {
        if (_constValues == null) return false;
        for (int e = 0; e < _constRecords!.Length; e++)
        {
            bool constant = _mesh.ElementHasConstantStiffness(e);
            if (constant != (_constRecords[e] != null)) return false;
            if (constant && !ReferenceEquals(_constRecords[e], _mesh.ElementRecord(e))) return false;
        }
        return true;
    }

    private void BuildConstantPart(ParallelOptions po)
    {
        int nElem = _mesh.ElementCount;
        var records = new object?[nElem];
        var constant = new List<int>();
        var variable = new List<int>();
        for (int e = 0; e < nElem; e++)
        {
            if (_mesh.ElementHasConstantStiffness(e))
            {
                records[e] = _mesh.ElementRecord(e);
                constant.Add(e);
            }
            else variable.Add(e);
        }
        _constValues = null;
        var values = new double[_values.Length];
        AddElements(constant.ToArray(), values, po, _mesh.ElementK);
        (_constValues, _constRecords, _variable) = (values, records, variable.ToArray());
    }

    // Матрицы КЭ из списка — параллельно пачками, раскладка последовательно в порядке списка.
    private void AddElements(int[] elems, double[] values, ParallelOptions po, Func<int, double[,]> elementMatrix)
    {
        var ke = new double[Math.Min(Batch, elems.Length)][,];
        for (int i0 = 0; i0 < elems.Length; i0 += Batch)
        {
            int count = Math.Min(Batch, elems.Length - i0);
            Parallel.For(0, count, po, i => ke[i] = elementMatrix(elems[i0 + i]));
            for (int i = 0; i < count; i++)
            {
                Scatter(elems[i0 + i], ke[i], values);
                ke[i] = null!;
            }
        }
    }

    private void Scatter(int e, double[,] m, double[] values)
    {
        int n = m.GetLength(0);
        long q = _elemOffset[e];
        if (!_elemLinked[e])
        {
            for (int a = 0; a < n; a++)
                for (int b = 0; b < n; b++, q++)
                {
                    int s = _slots[q];
                    if (s >= 0) values[s] += m[a, b];
                }
            return;
        }
        for (long lq = _linkedStart[e]; q < _elemOffset[e + 1]; q++, lq++)
        {
            int s = _slots[q];
            if (s < 0) continue;
            int ab = _linkedAb[lq];
            values[s] += _linkedCoef[lq] * m[ab / n, ab % n];
        }
    }
}
