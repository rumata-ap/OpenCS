namespace CScore.Fem.Fragment;

/// <summary>
/// Перенос пользовательских настроек при пересборке фрагмента (тот же Id схемы, новый состав из родителя): ГУ отдельных
/// разрезов сопоставляются по устойчивому ключу — КонЭ, вид, сторона, узел и грань бокового разреза (как ключи
/// <c>cutBc.overrides</c> CLI), а не по Id разреза, который выдаётся заново.
/// </summary>
public static class FragmentRebuildTransfer
{
    /// <summary>Ключ разреза: КонЭ, вид, сторона, узел, грань бокового разреза («+x», азимут нормали).</summary>
    public static string CutKey(FragmentCut cut)
    {
        ArgumentNullException.ThrowIfNull(cut);
        string face = cut.Side == FragmentCutSide.Lateral && cut.Normal is { } n ? FragmentCutLabel.FaceCode(n, out _) : "";
        return string.Join("|", cut.MemberTag, cut.Kind, cut.Side, cut.NodeTag ?? "", face);
    }

    /// <summary>
    /// ГУ старых разрезов (<paramref name="old"/>: разрез и байты режимов) → индексы новых разрезов. Одинаковые ключи
    /// сопоставляются по порядку. Разрезы без пары — в <c>Lost</c>.
    /// </summary>
    public static (Dictionary<int, byte[]> ByNewIndex, List<FragmentCut> Lost) MapCutBc(
        IReadOnlyList<(FragmentCut Cut, byte[] Modes)> old, IReadOnlyList<FragmentCut> fresh)
    {
        ArgumentNullException.ThrowIfNull(old);
        ArgumentNullException.ThrowIfNull(fresh);
        var free = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
        for (int i = 0; i < fresh.Count; i++)
        {
            string key = CutKey(fresh[i]);
            if (!free.TryGetValue(key, out var q)) free[key] = q = new Queue<int>();
            q.Enqueue(i);
        }
        var mapped = new Dictionary<int, byte[]>();
        var lost = new List<FragmentCut>();
        foreach (var (cut, modes) in old)
        {
            if (free.TryGetValue(CutKey(cut), out var q) && q.Count > 0) mapped[q.Dequeue()] = modes;
            else lost.Add(cut);
        }
        return (mapped, lost);
    }
}
