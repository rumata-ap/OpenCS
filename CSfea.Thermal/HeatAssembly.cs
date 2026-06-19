using CSfea.Sparse;
using CSfea.Thermal.Bc;
using CSfea.Thermal.Elements;
using CSfea.Thermal.Materials;

namespace CSfea.Thermal;

/// <summary>
/// Постоянный CSC-паттерн тепловой сетки и scatter-сборка матриц K/C
/// без промежуточного COO. Паттерн включает блоки элементов и пары граничных рёбер.
/// </summary>
public sealed class HeatAssembly
{
    /// <summary>Указатели столбцов CSC, длина NDof+1.</summary>
    public int[] ColPtr { get; private init; } = [];

    /// <summary>Индексы строк CSC, длина nnz.</summary>
    public int[] RowIdx { get; private init; } = [];

    private int _n;
    private HeatMesh _mesh = null!;
    private int[][] _elemSlots = [];           // _elemSlots[e][a*m+b] = слот (el[a], el[b])
    private Dictionary<long, int> _slotIndex = null!;

    /// <summary>Построить паттерн один раз для заданной сетки и набора граничных рёбер.</summary>
    public static HeatAssembly Build(HeatMesh mesh, IEnumerable<HeatBoundaryEdge> edges)
    {
        int n = mesh.NDof;
        var colSets = new SortedSet<int>[n];
        for (int i = 0; i < n; i++) colSets[i] = new SortedSet<int>();

        foreach (var el in mesh.Elements)
            foreach (int a in el)
                foreach (int b in el)
                    colSets[b].Add(a); // столбец b, строка a

        foreach (var e in edges)
        {
            AddPair(colSets, e.NodeA, e.NodeA);
            AddPair(colSets, e.NodeA, e.NodeB);
            AddPair(colSets, e.NodeB, e.NodeA);
            AddPair(colSets, e.NodeB, e.NodeB);
            if (e.NodeMid is int mid)
            {
                int[] tri = [e.NodeA, mid, e.NodeB];
                foreach (int a in tri)
                    foreach (int b in tri)
                        AddPair(colSets, a, b);
            }
        }

        var colPtr = new int[n + 1];
        for (int c = 0; c < n; c++) colPtr[c + 1] = colPtr[c] + colSets[c].Count;
        var rowIdx = new int[colPtr[n]];
        var slotIndex = new Dictionary<long, int>(colPtr[n]);
        int pos = 0;
        for (int c = 0; c < n; c++)
            foreach (int r in colSets[c])
            {
                rowIdx[pos] = r;
                slotIndex[Key(r, c)] = pos;
                pos++;
            }

        int Slot(int r, int c) => slotIndex[Key(r, c)];

        var elemSlots = new int[mesh.Elements.Length][];
        for (int e = 0; e < mesh.Elements.Length; e++)
        {
            var el = mesh.Elements[e];
            int m = el.Length;
            var slots = new int[m * m];
            for (int a = 0; a < m; a++)
                for (int b = 0; b < m; b++)
                    slots[a * m + b] = Slot(el[a], el[b]); // (row=el[a], col=el[b])
            elemSlots[e] = slots;
        }

        return new HeatAssembly
        {
            ColPtr = colPtr,
            RowIdx = rowIdx,
            _n = n,
            _mesh = mesh,
            _elemSlots = elemSlots,
            _slotIndex = slotIndex
        };
    }

    /// <summary>Слот (row, col) в массиве значений; -1, если пары нет в паттерне.</summary>
    public int SlotOf(int row, int col)
        => _slotIndex.TryGetValue(Key(row, col), out int s) ? s : -1;

    /// <summary>Приёмник вкладов в заданный массив значений (для Robin).</summary>
    public IMatrixSink SinkFor(double[] values) => new ValuesSink(this, values);

    /// <summary>Собрать K: обнулить values и заполнить вкладами элементов.</summary>
    public void AssembleK(IHeatMaterial mat, double[] nodalT, double[] values)
    {
        Array.Clear(values, 0, values.Length);
        for (int e = 0; e < _mesh.Elements.Length; e++)
        {
            var el = _mesh.Elements[e];
            double T = MeanT(el, nodalT);
            double lambda = mat.Conductivity(T);
            double[,] ke = el.Length == 6
                ? HeatTri6.ElementK(lambda, ElementCoords(el))
                : HeatTri3.ElementK(lambda, ElementCoords(el));
            Scatter(_elemSlots[e], el.Length, ke, values);
        }
    }

    /// <summary>Собрать C: обнулить values и заполнить вкладами элементов.</summary>
    public void AssembleC(IHeatMaterial mat, double[] nodalT, double[] values)
    {
        Array.Clear(values, 0, values.Length);
        for (int e = 0; e < _mesh.Elements.Length; e++)
        {
            var el = _mesh.Elements[e];
            double T = MeanT(el, nodalT);
            double rhocp = mat.VolumetricHeatCapacity(T);
            double[,] me = el.Length == 6
                ? HeatTri6.ElementM(rhocp, ElementCoords(el))
                : HeatTri3.ElementM(rhocp, ElementCoords(el));
            Scatter(_elemSlots[e], el.Length, me, values);
        }
    }

    /// <summary>Обернуть массив значений в CSC по постоянному паттерну (для SpMV/решателя).</summary>
    public CscMatrix ToCsc(double[] values)
        => new(_n, _n, ColPtr, RowIdx, values);

    private static void Scatter(int[] slots, int m, double[,] block, double[] values)
    {
        for (int a = 0; a < m; a++)
            for (int b = 0; b < m; b++)
                values[slots[a * m + b]] += block[a, b];
    }

    private double MeanT(int[] el, double[] nodalT)
    {
        double sum = 0;
        for (int i = 0; i < el.Length; i++) sum += nodalT[el[i]];
        return sum / el.Length;
    }

    private double[] ElementCoords(int[] el)
    {
        if (el.Length == 3)
            return [_mesh.X[el[0]], _mesh.Y[el[0]], _mesh.X[el[1]], _mesh.Y[el[1]], _mesh.X[el[2]], _mesh.Y[el[2]]];
        var coords = new double[12];
        for (int i = 0; i < 6; i++)
        {
            coords[2 * i] = _mesh.X[el[i]];
            coords[2 * i + 1] = _mesh.Y[el[i]];
        }
        return coords;
    }

    private static void AddPair(SortedSet<int>[] colSets, int row, int col) => colSets[col].Add(row);
    private static long Key(int row, int col) => ((long)col << 32) | (uint)row;

    private sealed class ValuesSink(HeatAssembly asm, double[] values) : IMatrixSink
    {
        public void Add(int i, int j, double value)
        {
            int s = asm.SlotOf(i, j);
            if (s < 0)
                throw new InvalidOperationException($"Слот ({i},{j}) отсутствует в паттерне сборки.");
            values[s] += value;
        }
    }
}
