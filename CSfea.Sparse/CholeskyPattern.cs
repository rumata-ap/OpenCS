namespace CSfea.Sparse;

/// <summary>Общие операции символического анализа Холецкого над портретом симметричной матрицы (CSC, полный портрет).</summary>
internal static class CholeskyPattern
{
    /// <summary>
    /// Переставленный портрет: элемент (r, c) исходной A переходит в (iperm[r], iperm[c]); строки внутри столбца
    /// отсортированы. <paramref name="srcs"/>[q] — индекс элемента q в a.Values. Подсчётом, без списков.
    /// </summary>
    public static void Permute(CscMatrix a, int[] iperm, out int[] colPtr, out int[] rows, out int[] srcs)
    {
        int n = a.Cols;
        colPtr = new int[n + 1];
        for (int col = 0; col < n; col++)
            colPtr[iperm[col] + 1] += a.ColPtr[col + 1] - a.ColPtr[col];
        for (int j = 0; j < n; j++) colPtr[j + 1] += colPtr[j];
        int nnz = colPtr[n];
        rows = new int[nnz];
        srcs = new int[nnz];
        for (int col = 0; col < n; col++)
        {
            int q = colPtr[iperm[col]];
            for (int p = a.ColPtr[col]; p < a.ColPtr[col + 1]; p++, q++)
            {
                rows[q] = iperm[a.RowIdx[p]];
                srcs[q] = p;
            }
        }
        for (int j = 0; j < n; j++)
            Array.Sort(rows, srcs, colPtr[j], colPtr[j + 1] - colPtr[j]);
    }

    /// <summary>Дерево исключений по верхнему треугольнику портрета (parent[k] = −1 у корня).</summary>
    public static int[] EliminationTree(int n, int[] ap, int[] ai)
    {
        var parent = new int[n];
        var ancestor = new int[n];
        for (int k = 0; k < n; k++)
        {
            parent[k] = -1;
            ancestor[k] = -1;
            for (int p = ap[k]; p < ap[k + 1]; p++)
            {
                int i = ai[p];
                while (i != -1 && i < k)
                {
                    int inext = ancestor[i];
                    ancestor[i] = k;
                    if (inext == -1) parent[i] = k;
                    i = inext;
                }
            }
        }
        return parent;
    }

    /// <summary>Обратный обход дерева в глубину: post[k] — узел на k-м месте; потомки узла идут подряд перед ним.</summary>
    public static int[] Postorder(int[] parent)
    {
        int n = parent.Length;
        var head = new int[n];
        var next = new int[n];
        Array.Fill(head, -1);
        // Дети в порядке возрастания номера: вставка в голову списка в обратном порядке.
        for (int j = n - 1; j >= 0; j--)
        {
            int p = parent[j];
            if (p == -1) continue;
            next[j] = head[p];
            head[p] = j;
        }
        var post = new int[n];
        var stack = new int[n];
        int k = 0;
        for (int root = 0; root < n; root++)
        {
            if (parent[root] != -1) continue;
            int top = 0;
            stack[0] = root;
            while (top >= 0)
            {
                int p = stack[top];
                int child = head[p];
                if (child == -1)
                {
                    top--;
                    post[k++] = p;
                }
                else
                {
                    head[p] = next[child];
                    stack[++top] = child;
                }
            }
        }
        return post;
    }

    /// <summary>Число ненулевых в столбцах L с диагональю — по подеревьям строк (O(nnz(L))).</summary>
    public static int[] ColumnCounts(int n, int[] ap, int[] ai, int[] parent)
    {
        var s = new int[n];
        var st = new int[n];
        var marked = new int[n];
        Array.Fill(marked, -1);
        var count = new int[n];
        for (int k = 0; k < n; k++)
        {
            int top = Ereach(k, ap, ai, parent, s, st, marked);
            for (int t = top; t < n; t++) count[s[t]]++; // L(k, i) в столбце i
            count[k]++;                                  // диагональ
        }
        return count;
    }

    /// <summary>Reach по дереву исключений: s[top..n-1] — паттерн строки k (топологически).</summary>
    public static int Ereach(int k, int[] ap, int[] ai, int[] parent, int[] s, int[] st, int[] marked)
    {
        int n = marked.Length;
        int top = n;
        marked[k] = k;
        for (int p = ap[k]; p < ap[k + 1]; p++)
        {
            int i = ai[p];
            if (i > k) continue;
            int len = 0;
            while (marked[i] != k)
            {
                st[len++] = i;
                marked[i] = k;
                i = parent[i];
                if (i == -1) break;
            }
            while (len > 0) s[--top] = st[--len];
        }
        return top;
    }
}
