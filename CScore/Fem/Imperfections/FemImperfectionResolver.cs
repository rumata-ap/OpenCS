using System.Globalization;
using CScore.En1993;
using CScore.Fem.Loads;
using CScore.Planar;

namespace CScore.Fem.Imperfections;

/// <summary>Несовершенство, привязанное к КЭ сетки (теги), с числами для расчёта; коэффициент загружения учтён.</summary>
public abstract record FemResolvedImperfection(string Label);

/// <summary>Крен: наклон <paramref name="Phi"/> (со знаком коэффициента) вдоль единичного горизонтального
/// <paramref name="Direction"/>; <paramref name="ElemTags"/> — КЭ охвата (стержни и пластины), null — вся схема.</summary>
public sealed record FemResolvedSway(string Label, double Phi, PlanarVector3 Direction, IReadOnlyList<string>? ElemTags)
    : FemResolvedImperfection(Label)
{
    /// <summary>Ход вычисления φ по (5.5); null — 1/φ задан вручную.</summary>
    public En1993SwayPhi? Norm { get; init; }
    /// <summary>Источник h для (5.5): «вручную» или «размах z охвата».</summary>
    public string HeightSource { get; init; } = "";
    /// <summary>Размах z охвата, м (для max|u₀| отчёта).</summary>
    public double SpanZ { get; init; }
}

/// <summary>Погибь-парабола цепочки стержневых КЭ <paramref name="ElemTags"/>: стрелка <paramref name="E0"/>, м (со знаком и
/// коэффициентом), вдоль единичного <paramref name="Direction"/> (местная y или z первого КЭ, глобальные оси), длина
/// хорды <paramref name="LengthM"/>.</summary>
public sealed record FemResolvedBow(string Label, IReadOnlyList<string> ElemTags, double E0, PlanarVector3 Direction, double LengthM)
    : FemResolvedImperfection(Label)
{
    /// <summary>Погибь вдоль местной y (иначе z).</summary>
    public bool AlongLocalY { get; init; } = true;
    /// <summary>Знак «авто» — по прогибу первого порядка (<see cref="FemImperfectionResolver.Decide"/>); до решения E0 — «+».</summary>
    public bool SignAuto { get; init; }
    /// <summary>Активность по п. 5.3.2(6) — по предварительному расчёту.</summary>
    public bool ActivityEn1993 { get; init; }
    /// <summary>Источник e₀ для отчёта (табл. 5.1 с кривой, L/e₀ или мм вручную).</summary>
    public string E0Source { get; init; } = "";
    /// <summary>Кривая потери устойчивости (табл. 5.1), null — e₀ не по таблице.</summary>
    public En1993Curve? Curve { get; init; }
    /// <summary>Концы цепочки — теги узлов сетки (начало хорды, конец хорды).</summary>
    public string StartNode { get; init; } = "";
    public string StopNode { get; init; } = "";
}

/// <summary>Итог привязки: несовершенства и диагностики (ошибки — расчёт невозможен).</summary>
public sealed record FemImperfectionResolution(IReadOnlyList<FemResolvedImperfection> Items, IReadOnlyList<FemValidationDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.IsError);
}

/// <summary>Данные схемы для нормативных значений: сечение CScore стержневого КЭ по тегу (кривая табл. 6.2).</summary>
public sealed record FemImperfectionContext
{
    /// <summary>Подготовленное сечение CScore стержневого КЭ (с материалами); null — нет.</summary>
    public Func<string, CrossSection?>? BeamCross { get; init; }
}

/// <summary>
/// Привязка элементов несовершенства (<see cref="FemImperfection"/>) к сетке схемы: охват крена и цепочки погибей —
/// теги КЭ, направление погиби — местная ось первого КЭ (<see cref="BeamLocalAxisConvention"/> с поворотом КЭ или КонЭ),
/// φ — по (5.5) EN 1993-1-1 или 1/φ вручную, e₀ — по табл. 5.1 (кривая — вручную или по табл. 6.2 по сечению КЭ), L/e₀
/// или мм вручную; коэффициент загружения — множитель. Знак «авто» и критерий п. 5.3.2(6) требуют предварительного
/// упругого расчёта — решаются вторым этапом (<see cref="Decide"/>).
/// </summary>
public static partial class FemImperfectionResolver
{
    public static FemImperfectionResolution Resolve(IReadOnlyList<(FemImperfection Item, double Factor)> imperfections,
        IReadOnlyList<FemElement> meshElements, IReadOnlyList<FemMeshNode> meshNodes, IReadOnlyList<FemMember> members,
        FemImperfectionContext? context = null)
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
                    bool norm = p.PhiMode == FemSwayParams.PhiEn1993;
                    if (!norm && p.PhiMode != FemSwayParams.PhiManual)
                    {
                        Error("imperfection_phi_invalid", $"Крен «{label}»: неизвестный способ задания φ «{p.PhiMode}».");
                        continue;
                    }
                    if (!norm && (p.PhiInv is not { } inv0 || !double.IsFinite(inv0) || inv0 <= 0))
                    {
                        Error("imperfection_phi_invalid", $"Крен «{label}»: 1/φ должно быть положительным числом.");
                        continue;
                    }
                    if (norm && p.M < 1)
                    {
                        Error("imperfection_phi_invalid", $"Крен «{label}»: число колонн m по (5.5) — целое не меньше 1.");
                        continue;
                    }
                    if (norm && p.HeightM is { } hm && !(double.IsFinite(hm) && hm > 0))
                    {
                        Error("imperfection_phi_invalid", $"Крен «{label}»: высота h по (5.5) должна быть положительной.");
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
                    double span = SpanZ(scope, meshElements, elements, nodes);
                    En1993SwayPhi? phiNorm = null;
                    string heightSource = "";
                    double phi;
                    if (norm)
                    {
                        double h = p.HeightM ?? span;
                        if (!(h > 1e-9))
                        {
                            Error("imperfection_phi_height", $"Крен «{label}»: высота h по (5.5) не определяется (размах z охвата нулевой) — задайте h вручную.");
                            continue;
                        }
                        phiNorm = En1993Imperfections.Phi(h, p.M);
                        heightSource = p.HeightM != null ? "h вручную" : "h — размах z охвата";
                        phi = phiNorm.Phi;
                    }
                    else phi = 1 / p.PhiInv!.Value;
                    double a = p.AngleDeg * Math.PI / 180;
                    items.Add(new FemResolvedSway(label, factor * phi, new PlanarVector3(Math.Cos(a), Math.Sin(a), 0), scope)
                    {
                        Norm = phiNorm, HeightSource = heightSource, SpanZ = span,
                    });
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
                    En1993Curve? manualCurve = null;
                    if (p.E0Mode == FemBowParams.E0Table && p.Curve != FemBowParams.CurveAuto)
                    {
                        manualCurve = En1993Imperfections.ParseCurve(p.Curve);
                        if (manualCurve == null)
                        {
                            Error("imperfection_curve_invalid", $"Погибь «{label}»: неизвестная кривая потери устойчивости «{p.Curve}» (a0, a, b, c, d или «авто»).");
                            continue;
                        }
                    }
                    if (p.Sign is not (FemBowParams.SignAuto or FemBowParams.SignPlus or FemBowParams.SignMinus))
                    {
                        Error("imperfection_sign_invalid", $"Погибь «{label}»: неизвестный знак «{p.Sign}».");
                        continue;
                    }
                    if (p.Activity is not (FemBowParams.ActivityAlways or FemBowParams.ActivityEn1993))
                    {
                        Error("imperfection_activity_invalid", $"Погибь «{label}»: неизвестный критерий активности «{p.Activity}».");
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
                    if (p.E0Mode is not (FemBowParams.E0Table or FemBowParams.E0Relative or FemBowParams.E0Absolute))
                    {
                        Error("imperfection_e0_invalid", $"Погибь «{label}»: неизвестный способ задания e₀ «{p.E0Mode}».");
                        continue;
                    }
                    double sign = p.Sign == FemBowParams.SignMinus ? -1 : 1;
                    bool alongY = p.Plane == FemBowParams.PlaneY;

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
                        double e0;
                        string source;
                        En1993Curve? curve = null;
                        var c = CultureInfo.CurrentCulture;
                        if (p.E0Mode == FemBowParams.E0Table)
                        {
                            string curveWhy;
                            if (manualCurve is { } mc) { curve = mc; curveWhy = "кривая задана вручную"; }
                            else
                            {
                                var choice = AutoCurve(tags, elements, alongY, context, out curveWhy);
                                if (choice == null)
                                {
                                    Error("imperfection_curve_undefined",
                                        $"Погибь «{label}», {what}: кривая потери устойчивости по табл. 6.2 не определена ({curveWhy}) — задайте кривую вручную.");
                                    continue;
                                }
                                curve = choice;
                            }
                            double lOverE0 = En1993Imperfections.LOverE0(curve.Value, p.Plastic);
                            e0 = len / lOverE0;
                            source = string.Create(c, $"табл. 5.1 ({(p.Plastic ? "пластический" : "упругий")} расчёт), кривая {En1993Imperfections.Name(curve.Value)}: e₀ = L/{lOverE0:0} — {curveWhy}");
                        }
                        else if (p.E0Mode == FemBowParams.E0Relative)
                        {
                            e0 = len / p.LOverE0!.Value;
                            source = string.Create(c, $"L/e₀ = {p.LOverE0!.Value:0.###} вручную");
                        }
                        else
                        {
                            e0 = p.E0Mm!.Value / 1000.0;
                            source = string.Create(c, $"e₀ = {p.E0Mm!.Value:0.###} мм вручную");
                        }
                        items.Add(new FemResolvedBow($"{label}, {what}", tags, factor * sign * e0, dir, len)
                        {
                            AlongLocalY = alongY, SignAuto = p.Sign == FemBowParams.SignAuto,
                            ActivityEn1993 = p.Activity == FemBowParams.ActivityEn1993, E0Source = source, Curve = curve,
                            StartNode = start, StopNode = stop,
                        });
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

    /// <summary>Размах z узлов КЭ охвата (null — все стержни и пластины сетки), м; нет узлов — 0.</summary>
    static double SpanZ(IReadOnlyList<string>? scope, IReadOnlyList<FemElement> meshElements,
        IReadOnlyDictionary<string, FemElement> elements, IReadOnlyDictionary<string, PlanarVector3> nodes)
    {
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        var list = scope == null
            ? meshElements.Where(e => e.ElemType is "beam" or "shell")
            : scope.Select(t => elements.GetValueOrDefault(t)).OfType<FemElement>();
        foreach (var e in list)
            foreach (var t in FemMeshTopology.ReadNodeTags(e) ?? [])
                if (FemMeshTopology.CanonicalNodeTag(t) is { } tag && nodes.TryGetValue(tag, out var p))
                {
                    lo = Math.Min(lo, p.Z);
                    hi = Math.Max(hi, p.Z);
                }
        return hi > lo ? hi - lo : 0;
    }

    /// <summary>
    /// Кривая по табл. 6.2 по сечениям CScore КЭ цепочки (<see cref="FemImperfectionContext.BeamCross"/>): оси EN —
    /// по прогибу вдоль местной y/z (у КЭ своей схемы ось Y сечения — по местной y, у импортного — ось X,
    /// <c>RcBeamSection.CrossYAlongLocalY</c>); разные кривые по цепочке — наихудшая (наибольшее e₀/L). null — не определена.
    /// </summary>
    static En1993Curve? AutoCurve(IReadOnlyList<string> tags, IReadOnlyDictionary<string, FemElement> elements, bool alongLocalY,
        FemImperfectionContext? context, out string why)
    {
        if (context?.BeamCross == null) { why = "нет сечений стержней"; return null; }
        var found = new List<En1993CurveChoice>();
        foreach (var tag in tags)
        {
            var e = elements[tag];
            if (context.BeamCross(tag) is not { } cs) { why = $"у КЭ {tag} нет сечения CScore"; return null; }
            bool yAlongY = e.Origin != FemMember.MeshSourceImported;
            var choice = En1993Imperfections.Curve(cs, aboutContourX: alongLocalY == yAlongY);
            if (choice.Curve == null) { why = $"КЭ {tag}: {choice.Reason}"; return null; }
            found.Add(choice);
        }
        var worst = found.MaxBy(f => (int)f.Curve!.Value)!;
        why = worst.Reason + (found.Select(f => f.Curve).Distinct().Count() > 1 ? " (наихудшая по КЭ цепочки)" : "");
        return worst.Curve;
    }

    /// <summary>Номер КЭ по тегу (теги сетки — целые числа).</summary>
    public static int? ElementNumber(string tag) =>
        int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ? id : null;
}
