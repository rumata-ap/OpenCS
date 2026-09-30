namespace CScore.Fem;

/// <summary>Стержневой КЭ для сборки цепочек: номер, узлы и их координаты.</summary>
/// <param name="ElemNum">Номер КЭ.</param>
/// <param name="NodeI">Начальный узел (сечение 1).</param>
/// <param name="NodeJ">Конечный узел (последнее сечение).</param>
/// <param name="PI">Координаты начального узла, м.</param>
/// <param name="PJ">Координаты конечного узла, м.</param>
public readonly record struct BarChainBar(
    int ElemNum, int NodeI, int NodeJ, (double X, double Y, double Z) PI, (double X, double Y, double Z) PJ);

/// <summary>КЭ в цепочке стержней.</summary>
/// <param name="ElemNum">Номер КЭ.</param>
/// <param name="SStart">Дуговая координата начального узла КЭ (сечение 1), м.</param>
/// <param name="SEnd">Дуговая координата конечного узла КЭ, м. Меньше <paramref name="SStart"/>,
/// если КЭ направлен против обхода цепочки.</param>
public sealed record BarChainElement(int ElemNum, double SStart, double SEnd)
{
    /// <summary>Дуговая координата точки КЭ: <paramref name="t"/> = 0 — начальный узел, 1 — конечный.</summary>
    public double At(double t) => SStart + (SEnd - SStart) * t;
}

/// <summary>Цепочка стержневых КЭ, идущих друг за другом без ветвлений и изломов (балка, колонна).</summary>
/// <param name="Elements">КЭ в порядке обхода.</param>
/// <param name="Length">Длина цепочки, м.</param>
public sealed record BarChain(IReadOnlyList<BarChainElement> Elements, double Length);

/// <summary>Разбор набора стержневых КЭ на цепочки — оси эпюр.</summary>
public static class BarChains
{
    /// <summary>Излом оси в узле, с которого начинается новая цепочка (косинус угла между соседними КЭ).</summary>
    const double KinkCos = 0.866; // 30°

    /// <summary>
    /// Собрать цепочки. Цепочка обрывается в узле, где сходится не два КЭ набора, и в узле с изломом
    /// оси больше 30° (колонна — ригель). Обход — от конца с меньшими Z, X, Y (снизу вверх, слева направо);
    /// цепочки упорядочены по наименьшему номеру КЭ. КЭ нулевой длины пропускаются.
    /// </summary>
    public static IReadOnlyList<BarChain> Build(IEnumerable<BarChainBar> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);
        var all = bars.Where(b => b.NodeI != b.NodeJ && Length(b) > 1e-9).ToList();
        var byNode = new Dictionary<int, List<int>>();
        var pointOf = new Dictionary<int, (double X, double Y, double Z)>();
        for (int i = 0; i < all.Count; i++)
        {
            Add(byNode, all[i].NodeI, i);
            Add(byNode, all[i].NodeJ, i);
            pointOf.TryAdd(all[i].NodeI, all[i].PI);
            pointOf.TryAdd(all[i].NodeJ, all[i].PJ);
        }

        bool IsBreak(int node)
        {
            var list = byNode[node];
            if (list.Count != 2) return true;
            var (a, b) = (Direction(all[list[0]], node), Direction(all[list[1]], node));
            // Оба направления — от узла; у прямой оси они противоположны.
            return -(a.X * b.X + a.Y * b.Y + a.Z * b.Z) < KinkCos;
        }

        var visited = new bool[all.Count];
        var chains = new List<BarChain>();

        BarChain Walk(int startNode, int firstBar)
        {
            var elements = new List<BarChainElement>();
            double s = 0;
            int node = startNode, bar = firstBar;
            while (true)
            {
                visited[bar] = true;
                var b = all[bar];
                double len = Length(b);
                bool forward = b.NodeI == node;
                elements.Add(forward ? new BarChainElement(b.ElemNum, s, s + len) : new BarChainElement(b.ElemNum, s + len, s));
                s += len;
                node = forward ? b.NodeJ : b.NodeI;
                if (IsBreak(node)) break;
                int next = byNode[node].First(i => i != bar);
                if (visited[next]) break; // замкнутый контур
                bar = next;
            }
            return new BarChain(elements, s);
        }

        var starts = byNode.Keys.Where(IsBreak)
            .OrderBy(n => Math.Round(pointOf[n].Z, 6)).ThenBy(n => Math.Round(pointOf[n].X, 6))
            .ThenBy(n => Math.Round(pointOf[n].Y, 6)).ThenBy(n => n);
        foreach (int node in starts)
            foreach (int bar in byNode[node].OrderBy(i => all[i].ElemNum))
                if (!visited[bar])
                    chains.Add(Walk(node, bar));

        // Остались только замкнутые контуры без изломов и ветвлений.
        foreach (int bar in Enumerable.Range(0, all.Count).OrderBy(i => all[i].ElemNum))
            if (!visited[bar])
                chains.Add(Walk(all[bar].NodeI, bar));

        return chains.OrderBy(c => c.Elements.Min(e => e.ElemNum)).ToList();
    }

    static void Add(Dictionary<int, List<int>> map, int node, int bar)
    {
        if (!map.TryGetValue(node, out var list)) map[node] = list = [];
        list.Add(bar);
    }

    static double Length(BarChainBar b)
    {
        double dx = b.PJ.X - b.PI.X, dy = b.PJ.Y - b.PI.Y, dz = b.PJ.Z - b.PI.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>Единичный вектор вдоль КЭ от узла <paramref name="fromNode"/>.</summary>
    static (double X, double Y, double Z) Direction(BarChainBar b, int fromNode)
    {
        var (p, q) = b.NodeI == fromNode ? (b.PI, b.PJ) : (b.PJ, b.PI);
        double len = Length(b);
        return ((q.X - p.X) / len, (q.Y - p.Y) / len, (q.Z - p.Z) / len);
    }
}
