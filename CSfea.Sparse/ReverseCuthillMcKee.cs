namespace CSfea.Sparse;

/// <summary>
/// Обратное упорядочивание Катхилла–Макки (RCM) для снижения ширины ленты
/// симметричной разрежённой матрицы. Возвращает перестановку perm:
/// perm[newIndex] = oldIndex.
/// </summary>
public static class ReverseCuthillMcKee
{
    /// <summary>Вычислить RCM-перестановку по CSC-паттерну (симметризуется внутри).</summary>
    public static int[] ComputeOrdering(int n, int[] colPtr, int[] rowIdx)
    {
        // Списки смежности (без петель), симметризованные.
        var adj = new List<int>[n];
        for (int i = 0; i < n; i++) adj[i] = new List<int>();
        var seen = new HashSet<(int, int)>();
        for (int c = 0; c < n; c++)
        {
            for (int p = colPtr[c]; p < colPtr[c + 1]; p++)
            {
                int r = rowIdx[p];
                if (r == c) continue;
                if (seen.Add((r, c))) { adj[r].Add(c); }
                if (seen.Add((c, r))) { adj[c].Add(r); }
            }
        }

        var degree = new int[n];
        for (int i = 0; i < n; i++) degree[i] = adj[i].Count;

        var order = new int[n];
        int pos = 0;
        var visited = new bool[n];

        // Обработка по компонентам связности; стартовый узел — минимальной степени.
        while (pos < n)
        {
            int start = -1;
            for (int i = 0; i < n; i++)
                if (!visited[i] && (start == -1 || degree[i] < degree[start]))
                    start = i;
            if (start == -1) break;

            var queue = new Queue<int>();
            visited[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                order[pos++] = v;
                foreach (int w in adj[v].Where(w => !visited[w]).OrderBy(w => degree[w]))
                {
                    visited[w] = true;
                    queue.Enqueue(w);
                }
            }
        }

        // Реверс — собственно RCM.
        Array.Reverse(order, 0, pos);
        return order;
    }
}
