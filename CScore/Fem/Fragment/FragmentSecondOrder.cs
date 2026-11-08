using System.Globalization;
using CScore.Fem.Fragment.Masks;
using CScore.Fem.Loads;
using CScore.Planar;

namespace CScore.Fem.Fragment;

/// <summary>
/// Сведения о расчёте фрагмента (или схемы в режиме «только P-δ») для признака второго порядка его наборов усилий
/// (спека 2026-11-06, срез 3).
/// </summary>
public sealed record FragmentSecondOrderFacts
{
    /// <summary>Расчёт учитывает геометрическую нелинейность (секущий с геомнелином или линейный с P-Δ источника).</summary>
    public bool Geometric { get; init; }
    /// <summary>Режим «только P-δ» (LocalOnly).</summary>
    public bool LocalOnly { get; init; }
    /// <summary>Наименьший коэффициент критической нагрузки каркаса; null — не посчитан.</summary>
    public double? LambdaCr { get; init; }
    /// <summary>Схема — фрагмент этажа (условия масок и торцов проверяются только у него).</summary>
    public bool IsFragment { get; init; }
    /// <summary>Хотя бы один разрез берёт перемещения или силы из масок.</summary>
    public bool MasksUsed { get; init; }
    /// <summary>Маски из источника второго порядка: true/false; null — неизвестно (масок нет, устарели, источник удалён).</summary>
    public bool? SourceSecondOrder { get; init; }
    /// <summary>Подпись источника масок для сообщений.</summary>
    public string SourceLabel { get; init; } = "";
    /// <summary>Пользователь разрешил считать второй порядок фрагмента и на масках первого порядка.</summary>
    public bool AllowFirstOrderMasks { get; init; }
    /// <summary>КонЭ торцевых разрезов (низ/верх), у которых Ux или Uy — не перемещение из маски; пусто — торцы кинематические.</summary>
    public IReadOnlyList<string> StaticEndMembers { get; init; } = [];
    /// <summary>Вертикальные КонЭ с грубой сеткой по высоте (<see cref="FragmentSecondOrder.Coarse"/>).</summary>
    public IReadOnlyList<FragmentCoarseMember> CoarseMembers { get; init; } = [];
}

/// <summary>Вертикальная часть с грубой сеткой: подпись КонЭ цепочки или стены <see cref="MemberTag"/> («C0a+C0b»), высота
/// <see cref="HeightM"/>, стержневых КЭ <see cref="Beams"/> (у стены 0), наибольший размах КЭ пластины по высоте
/// <see cref="ShellStepM"/> (у стержня 0).</summary>
public sealed record FragmentCoarseMember(string MemberTag, double HeightM, int Beams, double ShellStepM);

/// <summary>Признак второго порядка наборов и диагностики согласованности.</summary>
public sealed record FragmentSecondOrderDecision(bool SecondOrder, IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>
/// Согласованность второго порядка фрагмента (спека 2026-11-06, срез 3; решения пользователя 08.11). Признак
/// <see cref="ForceSet.SecondOrder"/> наборов (η в проверке по КЭ не применяется) — только когда эффекты второго порядка
/// учтены полностью:
/// <list type="bullet">
/// <item>полный охват: геомнелин, маски из источника второго порядка (или разрешено пользователем) и торцы низ/верх по Ux, Uy
/// в перемещениях из маски (P-Δ этажа передан кинематикой);</item>
/// <item>«только P-δ»: геомнелин и λ_cr каркаса ≥ 10 (P-Δ пренебрежимо мал).</item>
/// </list>
/// Иначе false — проверка учитывает η как при первом порядке (консервативно).
/// </summary>
public static class FragmentSecondOrder
{
    /// <summary>Порог λ_cr каркаса для режима «только P-δ» (решение 06.11).</summary>
    public const double LambdaMin = 10.0;
    /// <summary>Наименьшее число стержневых КЭ на высоту вертикального КонЭ.</summary>
    public const int MinBeamsPerHeight = 4;
    /// <summary>Вертикальный КЭ — ось стержня или плоскость пластины в пределах 10° от вертикали (как у <c>RcLocalPDelta</c>).</summary>
    public static readonly double VerticalCos = Math.Cos(10 * Math.PI / 180);

    /// <summary>Решение по сведениям: признак наборов и предупреждения <c>fragment_second_order_*</c>.</summary>
    public static FragmentSecondOrderDecision Decide(FragmentSecondOrderFacts f)
    {
        ArgumentNullException.ThrowIfNull(f);
        var diag = new List<FemValidationDiagnostic>();
        if (!f.Geometric) return new(false, diag);
        var c = CultureInfo.CurrentCulture;
        if (f.CoarseMembers.Count > 0)
        {
            var tags = f.CoarseMembers.Select(m => m.MemberTag).ToList();
            var sample = f.CoarseMembers.Take(3).Select(m => m.Beams > 0
                ? string.Format(c, "«{0}» — {1} КЭ на {2:0.##} м", m.MemberTag, m.Beams, m.HeightM)
                : string.Format(c, "«{0}» — шаг пластин {1:0.##} м при высоте {2:0.##} м", m.MemberTag, m.ShellStepM, m.HeightM));
            diag.Add(new("fragment_second_order_coarse",
                $"Второй порядок: грубая сетка вертикальных КонЭ ({string.Join("; ", sample)}" +
                (f.CoarseMembers.Count > 3 ? $"; всего {f.CoarseMembers.Count}" : "") +
                $") — нужно не меньше {MinBeamsPerHeight} стержневых КЭ на высоту или шаг пластин по высоте не больше l/4, " +
                "иначе P-δ и N_cr завышены (один КЭ на высоту — +22 %).", false, tags));
        }

        bool secondOrder;
        if (f.LocalOnly)
        {
            secondOrder = f.LambdaCr is double l && l >= LambdaMin;
        }
        else if (!f.IsFragment)
        {
            secondOrder = true;
        }
        else
        {
            bool sourceOk = f.SourceSecondOrder == true || !f.MasksUsed;
            if (f.MasksUsed && f.SourceSecondOrder != true)
            {
                string what = f.SourceSecondOrder == null
                    ? "порядок источника масок не определён (маски не сняты, устарели или источник удалён)"
                    : $"маски сняты из источника первого порядка{(f.SourceLabel.Length > 0 ? $" «{f.SourceLabel}»" : "")}";
                diag.Add(new("fragment_second_order_source_first_order", f.AllowFirstOrderMasks
                    ? $"Второй порядок фрагмента: {what} — сдвиг этажа не усилен; по выбору пользователя результат считается " +
                      "вторым порядком (η в проверке по КЭ не применяется)."
                    : $"Второй порядок фрагмента: {what} — сдвиг этажа не усилен: посчитайте родителя по второму порядку и " +
                      "снимите маски заново.", false));
            }
            if (f.StaticEndMembers.Count > 0)
                diag.Add(new("fragment_second_order_static_ends",
                    $"Второй порядок фрагмента: торцы (низ/верх) {FemLoadTargets.Sample(f.StaticEndMembers)} не в перемещениях из " +
                    "маски по Ux/Uy — P-Δ этажа не передан, учтён только P-δ.", false, f.StaticEndMembers));
            secondOrder = (sourceOk || f.AllowFirstOrderMasks) && f.StaticEndMembers.Count == 0;
        }
        if (!secondOrder)
            diag.Add(new("fragment_second_order_eta",
                "Наборы усилий этого расчёта в проверке по КЭ — первого порядка: η (п. 8.1.15 СП 63) применяется, а не " +
                "пропускается" + (f.LocalOnly ? " (λ_cr каркаса < 10 или не посчитан)." : " (см. предупреждения второго порядка)."),
                false));
        return new(secondOrder, diag);
    }

    /// <summary>
    /// Торцы фрагмента: КонЭ разрезов низа и верха, у которых Ux или Uy (общие оси, пресет и правки) — не перемещение из
    /// маски; <paramref name="masksUsed"/> — хотя бы один разрез (и боковой) берёт маску.
    /// </summary>
    public static IReadOnlyList<string> StaticEnds(IReadOnlyList<(int CutId, FragmentCut Cut)> cuts, FragmentBcPreset preset,
        FragmentBcPreset lateral, IReadOnlyDictionary<int, byte[]> overrides, out bool masksUsed)
    {
        ArgumentNullException.ThrowIfNull(cuts);
        ArgumentNullException.ThrowIfNull(overrides);
        var result = new List<string>();
        masksUsed = false;
        foreach (var (id, cut) in cuts)
        {
            // Нормаль стены влияет только на сверлильный поворот — Ux и Uy от неё не зависят.
            var modes = FragmentCutBc.Resolve(FragmentCutBc.PresetOf(cut, preset, lateral), cut, overrides.GetValueOrDefault(id),
                null, out _);
            masksUsed |= modes.Any(FragmentCutBc.NeedsMask);
            if (cut.Side != FragmentCutSide.Lateral
                && (modes[0] != FragmentDofMode.MaskDisplacement || modes[1] != FragmentDofMode.MaskDisplacement)
                && !result.Contains(cut.MemberTag))
                result.Add(cut.MemberTag);
        }
        return result;
    }

    /// <summary>
    /// Вертикальные части с грубой сеткой по высоте. Стержни — цепочки вертикальных (ось ≤ 10° от вертикали) КЭ, связанных
    /// общими узлами (колонна фрагмента из двух КонЭ по полэтажа — одна цепочка): меньше <see cref="MinBeamsPerHeight"/> КЭ
    /// на её высоту. Стены — связные группы компланарных вертикальных (плоскость ≤ 10° от вертикали) КЭ пластин: наибольший
    /// медианный размах КЭ по z больше l/4, l — размах группы по z. Подпись — КонЭ (<see cref="FemElement.SourceMemberTag"/>, нет —
    /// тег КЭ).
    /// </summary>
    public static IReadOnlyList<FragmentCoarseMember> Coarse(IReadOnlyList<FemElement> mesh, IReadOnlyList<FemMeshNode> meshNodes)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(meshNodes);
        var nodes = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        foreach (var n in meshNodes) nodes.TryAdd(n.NodeTag, n);
        var bars = new List<(FemElement E, FemElementGeometry G)>();
        var shells = new List<(FemElement E, FemElementGeometry G)>();
        foreach (var e in mesh)
            if (FemElementGeometry.Of(e, nodes) is { } g)
            {
                if (g.IsBar && IsVerticalBar(g)) bars.Add((e, g));
                else if (g.IsShell && IsVerticalShell(g)) shells.Add((e, g));
            }

        var result = new List<FragmentCoarseMember>();
        foreach (var part in Components(bars, (a, b) => true))
        {
            double h = Span(part.Select(x => x.G).ToList());
            if (h > 0 && part.Count < MinBeamsPerHeight) result.Add(new(Label(part), h, part.Count, 0));
        }
        foreach (var part in Components(shells, Coplanar))
        {
            double h = Span(part.Select(x => x.G).ToList());
            // Шаг — медиана размаха КЭ по z: у свободной сетки отдельные КЭ у линий сопряжения вытянуты.
            var extents = part.Select(x => x.G.Points.Max(p => p.Z) - x.G.Points.Min(p => p.Z)).Order().ToList();
            double step = extents[(extents.Count - 1) / 2];
            if (h > 0 && step > h / 4 + 1e-9) result.Add(new(Label(part), h, 0, step));
        }
        return result;
    }

    /// <summary>Связные по общим узлам группы КЭ; соседство — только при <paramref name="joins"/>.</summary>
    static List<List<(FemElement E, FemElementGeometry G)>> Components(List<(FemElement E, FemElementGeometry G)> items,
        Func<FemElementGeometry, FemElementGeometry, bool> joins)
    {
        var parent = Enumerable.Range(0, items.Count).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
        var byNode = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (int i = 0; i < items.Count; i++)
            foreach (var tag in items[i].G.NodeTags)
            {
                if (!byNode.TryGetValue(tag, out var list)) byNode[tag] = list = [];
                foreach (int j in list)
                    if (joins(items[i].G, items[j].G)) parent[Find(i)] = Find(j);
                list.Add(i);
            }
        return Enumerable.Range(0, items.Count).GroupBy(Find).Select(g => g.Select(i => items[i]).ToList()).ToList();
    }

    /// <summary>Подпись группы: КонЭ по порядку (нет — теги КЭ), не больше трёх.</summary>
    static string Label(List<(FemElement E, FemElementGeometry G)> part)
    {
        var tags = part.Select(x => string.IsNullOrEmpty(x.E.SourceMemberTag) ? x.E.ElemTag : x.E.SourceMemberTag!)
            .Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToList();
        return string.Join("+", tags.Take(3)) + (tags.Count > 3 ? "+…" : "");
    }

    /// <summary>Пластины в одной плоскости: нормали параллельны (1°), расстояние центра второй от плоскости первой ≤ 1 см.</summary>
    static bool Coplanar(FemElementGeometry a, FemElementGeometry b)
    {
        var na = Normal(a);
        var nb = Normal(b);
        return Math.Abs(na.Dot(nb)) >= 0.99985 && Math.Abs(na.Dot(b.Centroid - a.Points[0])) <= 0.01;
    }

    static PlanarVector3 Normal(FemElementGeometry g) => (g.Points[1] - g.Points[0]).Cross(g.Points[2] - g.Points[0]).Normalize();
    static double Span(IReadOnlyList<FemElementGeometry> g) =>
        g.Max(x => x.Points.Max(p => p.Z)) - g.Min(x => x.Points.Min(p => p.Z));

    static bool IsVerticalBar(FemElementGeometry g)
    {
        var d = g.Points[1] - g.Points[0];
        double len = d.Length;
        return len > 0 && Math.Abs(d.Z) / len >= VerticalCos;
    }

    static bool IsVerticalShell(FemElementGeometry g)
    {
        var n = (g.Points[1] - g.Points[0]).Cross(g.Points[2] - g.Points[0]);
        double len = n.Length;
        // Плоскость вертикальна в пределах 10° ⇔ нормаль отклонена от горизонтали не более чем на 10°.
        return len > 0 && Math.Abs(n.Z) / len <= Math.Sin(10 * Math.PI / 180);
    }
}
