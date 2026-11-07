using System.Globalization;
using CScore.Fem.Loads;
using CScore.Planar;

namespace CScore.Fem.Imperfections;

/// <summary>Несовершенство, привязанное к КЭ сетки (теги), с числами для расчёта; коэффициент загружения учтён.</summary>
public abstract record FemResolvedImperfection(string Label);

/// <summary>Крен: наклон <paramref name="Phi"/> (со знаком коэффициента) вдоль единичного горизонтального
/// <paramref name="Direction"/>; <paramref name="ElemTags"/> — КЭ охвата (стержни и пластины), null — вся схема.</summary>
public sealed record FemResolvedSway(string Label, double Phi, PlanarVector3 Direction, IReadOnlyList<string>? ElemTags)
    : FemResolvedImperfection(Label);

/// <summary>Погибь-парабола цепочки стержневых КЭ <paramref name="ElemTags"/>: стрелка <paramref name="E0"/>, м (со знаком и
/// коэффициентом), вдоль единичного <paramref name="Direction"/> (местная y или z первого КЭ, глобальные оси), длина
/// хорды <paramref name="LengthM"/>.</summary>
public sealed record FemResolvedBow(string Label, IReadOnlyList<string> ElemTags, double E0, PlanarVector3 Direction, double LengthM)
    : FemResolvedImperfection(Label);

/// <summary>Итог привязки: несовершенства и диагностики (ошибки — расчёт невозможен).</summary>
public sealed record FemImperfectionResolution(IReadOnlyList<FemResolvedImperfection> Items, IReadOnlyList<FemValidationDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.IsError);
}

/// <summary>
/// Привязка элементов несовершенства (<see cref="FemImperfection"/>) к сетке схемы: охват крена и цепочки погибей —
/// теги КЭ, направление погиби — местная ось первого КЭ (<see cref="BeamLocalAxisConvention"/> с поворотом КЭ или КонЭ),
/// e₀ и φ — по заданию «вручную» (L/e₀, мм, 1/φ) с коэффициентом загружения. Нормативные значения (табл. 5.1, (5.5)),
/// знак «авто» и критерий активности по п. 5.3.2(6) требуют предварительного расчёта и здесь не вычисляются.
/// </summary>
public static class FemImperfectionResolver
{
    public static FemImperfectionResolution Resolve(IReadOnlyList<(FemImperfection Item, double Factor)> imperfections,
        IReadOnlyList<FemElement> meshElements, IReadOnlyList<FemMeshNode> meshNodes, IReadOnlyList<FemMember> members)
    {
        ArgumentNullException.ThrowIfNull(imperfections);
        ArgumentNullException.ThrowIfNull(meshElements);
        ArgumentNullException.ThrowIfNull(meshNodes);
        ArgumentNullException.ThrowIfNull(members);
        var items = new List<FemResolvedImperfection>();
        var diag = new List<FemValidationDiagnostic>();
        var elements = new Dictionary<string, FemElement>(StringComparer.Ordinal);
        foreach (var e in meshElements) elements.TryAdd(e.ElemTag, e);
        var nodes = new Dictionary<string, PlanarVector3>(StringComparer.Ordinal);
        foreach (var n in meshNodes)
            if (FemMeshTopology.CanonicalNodeTag(n.NodeTag) is { } tag) nodes.TryAdd(tag, new PlanarVector3(n.X, n.Y, n.Z));
        var memberRotation = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var m in members) memberRotation.TryAdd(m.ElemTag, m.RotationDeg);
        var byMember = meshElements.Where(e => e.SourceMemberTag != null)
            .GroupBy(e => e.SourceMemberTag!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        void Error(string code, string message) => diag.Add(new(code, message, true));

        foreach (var (imp, factor) in imperfections)
        {
            string label = imp.Label;
            switch (imp.Kind)
            {
                case FemImperfectionKinds.Sway:
                {
                    var p = FemSwayParams.Parse(imp.ParamsJson);
                    if (p.PhiMode == FemSwayParams.PhiEn1993)
                    {
                        Error("imperfection_phi_norm", $"Крен «{label}»: φ по EN 1993-1-1 (5.5) пока не вычисляется — задайте 1/φ вручную.");
                        continue;
                    }
                    if (p.PhiInv is not { } inv || !double.IsFinite(inv) || inv <= 0)
                    {
                        Error("imperfection_phi_invalid", $"Крен «{label}»: 1/φ должно быть положительным числом.");
                        continue;
                    }
                    if (!double.IsFinite(p.AngleDeg))
                    {
                        Error("imperfection_angle_invalid", $"Крен «{label}»: угол направления должен быть числом.");
                        continue;
                    }
                    List<string>? scope = null;
                    if (p.Scope == FemSwayParams.ScopeList)
                    {
                        scope = [];
                        var missing = new List<string>();
                        foreach (var tag in p.MemberTags)
                            if (byMember.TryGetValue(tag, out var list)) scope.AddRange(list.Select(e => e.ElemTag));
                            else missing.Add(tag);
                        foreach (var tag in p.ElementTags)
                            if (elements.ContainsKey(tag)) scope.Add(tag);
                            else missing.Add(tag);
                        if (missing.Count > 0)
                            Error("imperfection_target_missing", $"Крен «{label}»: нет КонЭ или КЭ сетки {FemLoadTargets.Sample(missing)}.");
                        scope = scope.Distinct(StringComparer.Ordinal).ToList();
                        if (scope.Count == 0)
                        {
                            Error("imperfection_scope_empty", $"Крен «{label}»: охват не содержит КЭ.");
                            continue;
                        }
                    }
                    double a = p.AngleDeg * Math.PI / 180;
                    items.Add(new FemResolvedSway(label, factor / inv, new PlanarVector3(Math.Cos(a), Math.Sin(a), 0), scope));
                    break;
                }
                case FemImperfectionKinds.Bow:
                {
                    var p = FemBowParams.Parse(imp.ParamsJson);
                    if (p.Plane is not (FemBowParams.PlaneY or FemBowParams.PlaneZ))
                    {
                        Error("imperfection_plane_invalid", $"Погибь «{label}»: плоскость должна быть y или z.");
                        continue;
                    }
                    if (p.E0Mode == FemBowParams.E0Table)
                    {
                        Error("imperfection_e0_norm", $"Погибь «{label}»: e₀ по табл. 5.1 EN 1993-1-1 пока не вычисляется — задайте L/e₀ или e₀ в мм.");
                        continue;
                    }
                    if (p.E0Mode == FemBowParams.E0Relative && (p.LOverE0 is not { } r || !double.IsFinite(r) || r <= 0))
                    {
                        Error("imperfection_e0_invalid", $"Погибь «{label}»: L/e₀ должно быть положительным числом.");
                        continue;
                    }
                    if (p.E0Mode == FemBowParams.E0Absolute && (p.E0Mm is not { } mm || !double.IsFinite(mm)))
                    {
                        Error("imperfection_e0_invalid", $"Погибь «{label}»: e₀ (мм) должно быть числом.");
                        continue;
                    }
                    if (p.E0Mode is not (FemBowParams.E0Relative or FemBowParams.E0Absolute))
                    {
                        Error("imperfection_e0_invalid", $"Погибь «{label}»: неизвестный способ задания e₀ «{p.E0Mode}».");
                        continue;
                    }
                    if (p.Sign == FemBowParams.SignAuto)
                        diag.Add(new("imperfection_sign_auto",
                            $"Погибь «{label}»: знак «авто» по прогибу первого порядка пока не определяется — принят «+» местной оси.", false));
                    if (p.Activity == FemBowParams.ActivityEn1993)
                        diag.Add(new("imperfection_activity",
                            $"Погибь «{label}»: критерий п. 5.3.2(6) пока не проверяется — погибь учитывается всегда.", false));
                    double sign = p.Sign == FemBowParams.SignMinus ? -1 : 1;

                    var chains = new List<(string What, List<string> Tags)>();
                    foreach (var tag in p.MemberTags)
                    {
                        if (!byMember.TryGetValue(tag, out var list) || !list.Any(e => e.ElemType == "beam"))
                        {
                            Error("imperfection_target_missing", $"Погибь «{label}»: у КонЭ {tag} нет стержневых КЭ сетки.");
                            continue;
                        }
                        chains.Add(($"КонЭ {tag}", list.Where(e => e.ElemType == "beam").Select(e => e.ElemTag).ToList()));
                    }
                    foreach (var chain in p.ElementChains)
                        chains.Add((chain.Count == 1 ? $"КЭ {chain[0]}" : $"КЭ {string.Join(", ", chain.Take(3))}{(chain.Count > 3 ? "…" : "")}", chain));
                    if (chains.Count == 0)
                    {
                        Error("imperfection_scope_empty", $"Погибь «{label}»: не заданы элементы.");
                        continue;
                    }
                    foreach (var (what, tags) in chains)
                    {
                        if (Chain(tags, elements, nodes, out var start, out var stop, out string? why) is not { } first)
                        {
                            Error("imperfection_chain_invalid", $"Погибь «{label}», {what}: {why}");
                            continue;
                        }
                        double len = (nodes[stop] - nodes[start]).Length;
                        var ft = FemMeshTopology.ReadNodeTags(first, 2)!;
                        double rot = first.BeamRotationDeg
                            ?? (first.SourceMemberTag is { } mt && memberRotation.TryGetValue(mt, out double mr) ? mr : 0);
                        PlanarVector3 dir;
                        try
                        {
                            var f = BeamLocalAxisConvention.Frame(nodes[FemMeshTopology.CanonicalNodeTag(ft[0])!],
                                nodes[FemMeshTopology.CanonicalNodeTag(ft[1])!], rot);
                            dir = p.Plane == FemBowParams.PlaneY ? f.Y : f.Z;
                        }
                        catch (InvalidOperationException ex)
                        {
                            Error("imperfection_chain_invalid", $"Погибь «{label}», {what}: {ex.Message}");
                            continue;
                        }
                        double e0 = p.E0Mode == FemBowParams.E0Relative ? len / p.LOverE0!.Value : p.E0Mm!.Value / 1000.0;
                        items.Add(new FemResolvedBow($"{label}, {what}", tags, factor * sign * e0, dir, len));
                    }
                    break;
                }
                case FemImperfectionKinds.Modes:
                    Error("imperfection_modes", $"Несовершенство «{label}»: формы потери устойчивости пока не поддержаны.");
                    break;
                default:
                    Error("imperfection_kind_invalid", $"Несовершенство «{label}»: неизвестный вид «{imp.Kind}».");
                    break;
            }
        }
        return new FemImperfectionResolution(items, diag);
    }

    /// <summary>
    /// Проверка цепочки стержневых КЭ: существуют, не повторяются, соосны (одна прямая), без ветвлений; концы — узлы,
    /// входящие в один КЭ цепочки. Возвращает первый КЭ (направление погиби) или null с причиной.
    /// </summary>
    static FemElement? Chain(IReadOnlyList<string> tags, IReadOnlyDictionary<string, FemElement> elements,
        IReadOnlyDictionary<string, PlanarVector3> nodes, out string start, out string stop, out string? why)
    {
        start = stop = "";
        why = null;
        if (tags.Count == 0) { why = "пустая цепочка."; return null; }
        if (tags.Distinct(StringComparer.Ordinal).Count() != tags.Count) { why = "КЭ повторяются."; return null; }
        var degree = new Dictionary<string, int>(StringComparer.Ordinal);
        var ends = new List<(string I, string J)>();
        foreach (var tag in tags)
        {
            if (!elements.TryGetValue(tag, out var e) || e.ElemType != "beam")
            {
                why = $"КЭ {tag} — не стержень сетки.";
                return null;
            }
            var nt = FemMeshTopology.ReadNodeTags(e, 2);
            string? i = nt == null ? null : FemMeshTopology.CanonicalNodeTag(nt[0]);
            string? j = nt == null ? null : FemMeshTopology.CanonicalNodeTag(nt[1]);
            if (i == null || j == null || !nodes.ContainsKey(i) || !nodes.ContainsKey(j))
            {
                why = $"у КЭ {tag} нет узлов сетки.";
                return null;
            }
            ends.Add((i, j));
            degree[i] = degree.GetValueOrDefault(i) + 1;
            degree[j] = degree.GetValueOrDefault(j) + 1;
        }
        var chainEnds = degree.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();
        if (chainEnds.Count != 2 || degree.Values.Any(v => v > 2))
        {
            why = "КЭ не образуют цепочку без ветвлений.";
            return null;
        }
        var (a, b) = (nodes[chainEnds[0]], nodes[chainEnds[1]]);
        var chord = b - a;
        double len = chord.Length;
        if (!(len > 1e-9)) { why = "нулевая длина хорды."; return null; }
        var t = chord * (1.0 / len);
        foreach (var (i, j) in ends)
            foreach (var p in new[] { nodes[i], nodes[j] })
            {
                var d = p - a;
                if ((d - t * d.Dot(t)).Length > 1e-6 * Math.Max(1.0, len))
                {
                    why = "КЭ не лежат на одной прямой (погибь задаётся по хорде соосной цепочки).";
                    return null;
                }
            }
        start = chainEnds[0];
        stop = chainEnds[1];
        return elements[tags[0]];
    }

    /// <summary>Номер КЭ по тегу (теги сетки — целые числа).</summary>
    public static int? ElementNumber(string tag) =>
        int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ? id : null;
}
