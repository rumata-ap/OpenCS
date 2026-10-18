namespace CScore.Fem;

/// <summary>
/// Расчётный элемент для расчётной длины стержня: КЭ сетки, для которых lef общая.
/// </summary>
/// <param name="MemberTag">Тег конструктивного элемента; null — цепочка КЭ между раскреплениями (схема без КонЭ).</param>
/// <param name="ElemTags">Теги стержневых КЭ элемента.</param>
public sealed record FemBucklingUnit(string? MemberTag, IReadOnlyList<string> ElemTags)
{
    /// <summary>Подпись для отчёта: «КонЭ 12» или «КЭ 101–104».</summary>
    public string Label => MemberTag != null ? $"КонЭ {MemberTag}"
        : ElemTags.Count == 1 ? $"КЭ {ElemTags[0]}" : $"КЭ {ElemTags[0]}–{ElemTags[^1]}";
}

/// <summary>
/// Расчётные элементы схемы для автоматических расчётных длин (спека 2026-10-18, п. 1): стержневые КЭ конструктивного
/// элемента (<see cref="FemElement.SourceMemberTag"/>) — один элемент; КЭ без КонЭ (импортированные схемы «только
/// сетка») — цепочки соосных КЭ между раскреплениями (<see cref="FemBracedLength.BarChain"/>).
/// </summary>
public static class FemBucklingUnits
{
    /// <param name="mesh">КЭ сетки схемы (все — для раскреплений цепочек).</param>
    /// <param name="nodes">Узлы сетки.</param>
    public static List<FemBucklingUnit> Build(IReadOnlyList<FemElement> mesh, IReadOnlyList<FemMeshNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(nodes);
        var bars = mesh.Where(e => e.ElemType != "shell" && FemMeshTopology.ReadNodeTags(e) is { Count: 2 }).ToList();
        var result = new List<FemBucklingUnit>();
        foreach (var g in bars.Where(e => e.SourceMemberTag != null).GroupBy(e => e.SourceMemberTag!, StringComparer.Ordinal))
            result.Add(new FemBucklingUnit(g.Key, g.Select(e => e.ElemTag).ToList()));

        var free = bars.Where(e => e.SourceMemberTag == null).ToList();
        if (free.Count == 0) return result;
        var braced = new FemBracedLength(mesh, nodes);
        var done = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in free)
        {
            if (done.Contains(e.ElemTag)) continue;
            var chain = (braced.BarChain(e) ?? [e]).Where(c => c.SourceMemberTag == null && done.Add(c.ElemTag))
                .Select(c => c.ElemTag).ToList();
            if (chain.Count > 0) result.Add(new FemBucklingUnit(null, chain));
        }
        return result;
    }
}
