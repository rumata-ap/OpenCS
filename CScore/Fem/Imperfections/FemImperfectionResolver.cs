using System.Globalization;
using CScore.En1992;
using CScore.En1993;
using CScore.Fem.Loads;
using CScore.Sp63;
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
    /// <summary>Ход вычисления θᵢ по EN 1992-1-1 (5.1); null — не по EN 1992.</summary>
    public En1992Theta? Theta { get; init; }
    /// <summary>Источник h для (5.5) / l для (5.1): «вручную» или «размах z охвата».</summary>
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
    /// <summary>e₀ по ЖБ-норме (EN 1992-1-1 / СП 63) — учитывается всегда (EN 1992-1-1 п. 5.2(1)P).</summary>
    public bool Concrete { get; init; }
    /// <summary>Источник e₀ для отчёта (табл. 5.1 с кривой, L/e₀ или мм вручную).</summary>
    public string E0Source { get; init; } = "";
    /// <summary>Кривая потери устойчивости (табл. 5.1), null — e₀ не по таблице.</summary>
    public En1993Curve? Curve { get; init; }
    /// <summary>Концы цепочки — теги узлов сетки (начало хорды, конец хорды).</summary>
    public string StartNode { get; init; } = "";
    public string StopNode { get; init; } = "";
}

/// <summary>
/// Ярус погиби стены: КЭ пластин <paramref name="ElemTags"/> между уровнями <paramref name="ZBottom"/>–<paramref name="ZTop"/>,
/// м; парабола w₀ = 4·<paramref name="E0"/>·ζ(1 − ζ) вдоль единичной горизонтальной нормали <paramref name="Normal"/>
/// (глобальные оси); <paramref name="E0"/> — м, со знаком и коэффициентом.
/// </summary>
public sealed record FemResolvedWallTier(string Panel, IReadOnlyList<string> ElemTags, double ZBottom, double ZTop,
    PlanarVector3 Normal, double E0)
{
    public double Height => ZTop - ZBottom;
    /// <summary>Толщина стены, м (для отчёта).</summary>
    public double Thickness { get; init; }
    /// <summary>Источник e₀ с ходом вычисления.</summary>
    public string E0Source { get; init; } = "";
}

/// <summary>Погибь стен по ярусам (<see cref="FemImperfectionKinds.WallBow"/>).</summary>
public sealed record FemResolvedWallBow(string Label, IReadOnlyList<FemResolvedWallTier> Tiers) : FemResolvedImperfection(Label)
{
    /// <summary>Знак «авто» — для каждого яруса по прогибу первого порядка (<see cref="FemImperfectionResolver.Decide"/>).</summary>
    public bool SignAuto { get; init; }
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

    /// <summary>
    /// Формы постановки устойчивости для несовершенства «Формы» (сводка, записи форм, сверка отпечатка геометрии с
    /// текущей схемой); null — формы недоступны (ошибка <c>imperfection_modes_source</c>).
    /// </summary>
    public Func<FemModesParams, FemModesLookup>? Modes { get; init; }
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
        FemBracedLength? braced = null;

        foreach (var (imp, factor) in imperfections)
        {
            string label = imp.Label;
            switch (imp.Kind)
            {
                case FemImperfectionKinds.Sway:
                {
                    var p = FemSwayParams.Parse(imp.ParamsJson);
                    bool en1992 = p.PhiMode == FemSwayParams.PhiEn1992;
                    bool norm = p.PhiMode == FemSwayParams.PhiEn1993 || en1992;
                    string formula = en1992 ? "(5.1)" : "(5.5)";
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
                        Error("imperfection_phi_invalid", $"Крен «{label}»: число {(en1992 ? "вертикальных элементов" : "колонн")} m по {formula} — целое не меньше 1.");
                        continue;
                    }
                    if (norm && p.HeightM is { } hm && !(double.IsFinite(hm) && hm > 0))
                    {
                        Error("imperfection_phi_invalid", $"Крен «{label}»: {(en1992 ? "длина или высота l" : "высота h")} по {formula} должна быть положительной.");
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
                    En1992Theta? theta = null;
                    string heightSource = "";
                    double phi;
                    if (norm)
                    {
                        double h = p.HeightM ?? span;
                        string hName = en1992 ? "l" : "h";
                        if (!(h > 1e-9))
                        {
                            Error("imperfection_phi_height", $"Крен «{label}»: {hName} по {formula} не определяется (размах z охвата нулевой) — задайте {hName} вручную.");
                            continue;
                        }
                        heightSource = p.HeightM != null ? $"{hName} вручную" : $"{hName} — размах z охвата";
                        if (en1992)
                        {
                            theta = En1992Imperfections.Theta(h, p.M);
                            phi = theta.Theta;
                        }
                        else
                        {
                            phiNorm = En1993Imperfections.Phi(h, p.M);
                            phi = phiNorm.Phi;
                        }
                    }
                    else phi = 1 / p.PhiInv!.Value;
                    double a = p.AngleDeg * Math.PI / 180;
                    items.Add(new FemResolvedSway(label, factor * phi, new PlanarVector3(Math.Cos(a), Math.Sin(a), 0), scope)
                    {
                        Norm = phiNorm, Theta = theta, HeightSource = heightSource, SpanZ = span,
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
                    bool concrete = FemBowParams.IsConcreteMode(p.E0Mode);
                    if (p.E0Mode is not (FemBowParams.E0Table or FemBowParams.E0Relative or FemBowParams.E0Absolute) && !concrete)
                    {
                        Error("imperfection_e0_invalid", $"Погибь «{label}»: неизвестный способ задания e₀ «{p.E0Mode}».");
                        continue;
                    }
                    double mu = p.Mu ?? 1.0;
                    if (p.E0Mode is FemBowParams.E0En1992 or FemBowParams.E0En1992Simplified)
                    {
                        if (!double.IsFinite(mu) || mu <= 0)
                        {
                            Error("imperfection_mu_invalid", $"Погибь «{label}»: μ расчётной длины l₀ = μ·L должно быть положительным числом.");
                            continue;
                        }
                        // П. 5.2(7)a: eᵢ — погибь раскреплённого элемента (рис. 5.1 a2); нераскреплённый — наклон, задаётся креном.
                        if (mu >= 2 - 1e-9)
                            diag.Add(new("imperfection_bow_unbraced",
                                $"Погибь «{label}»: μ = {mu.ToString("0.###", CultureInfo.CurrentCulture)} — нераскреплённый элемент (рис. 5.1 a1): " +
                                "несовершенство такого элемента — наклон θᵢ, задайте его креном; погибь eᵢ = θᵢ·l₀/2 принята по хорде цепочки.", false));
                    }
                    if (p.E0Mode == FemBowParams.E0Sp63 && p.SectionHeightMm is { } shm && !(double.IsFinite(shm) && shm > 0))
                    {
                        Error("imperfection_section_height", $"Погибь «{label}»: высота сечения h (мм) должна быть положительной.");
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
                        else if (p.E0Mode == FemBowParams.E0En1992)
                            e0 = En1992Imperfections.BowEi(len, mu, out source);
                        else if (p.E0Mode == FemBowParams.E0En1992Simplified)
                            e0 = En1992Imperfections.BowSimplified(len, mu, out source);
                        else if (p.E0Mode == FemBowParams.E0Sp63)
                        {
                            double h;
                            string hWhy;
                            if (p.SectionHeightMm is { } hmm) { h = hmm / 1000.0; hWhy = "h вручную"; }
                            else if (SectionHeight(tags, elements, alongY, context, out hWhy) is { } hs) h = hs;
                            else
                            {
                                Error("imperfection_section_height",
                                    $"Погибь «{label}», {what}: высота сечения для п. 8.1.7 не определена ({hWhy}) — задайте h вручную.");
                                continue;
                            }
                            e0 = Sp63AccidentalEccentricity.Ea(len, h, out string ea);
                            source = $"{ea} ({hWhy}; l — длина цепочки между концами; как погибь складывается с e₀ расчёта — " +
                                     "для статически неопределимых в запас против max(e₀, e_a))";
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
                            ActivityEn1993 = !concrete && p.Activity == FemBowParams.ActivityEn1993, Concrete = concrete,
                            E0Source = source, Curve = curve,
                            StartNode = start, StopNode = stop,
                        });
                    }
                    break;
                }
                case FemImperfectionKinds.Modes:
                    if (ResolveModes(label, FemModesParams.Parse(imp.ParamsJson), factor, context, Error) is { } modes) items.Add(modes);
                    break;
                case FemImperfectionKinds.WallBow:
                    braced ??= new FemBracedLength(meshElements, meshNodes);
                    if (ResolveWallBow(label, FemWallBowParams.Parse(imp.ParamsJson), factor, byMember, elements, nodes, braced,
                            Error, d => diag.Add(d)) is { } wall)
                        items.Add(wall);
                    break;
                default:
                    Error("imperfection_kind_invalid", $"Несовершенство «{label}»: неизвестный вид «{imp.Kind}».");
                    break;
            }
        }
        // П. 5.3.2(11): форма — «взамен» крена (3) и погибей (6); в одном варианте не сочетаются.
        if (items.Any(i => i is FemResolvedModes) && items.Any(i => i is FemResolvedSway or FemResolvedBow or FemResolvedWallBow))
            Error("imperfection_modes_mixed",
                $"Несовершенства по формам ({string.Join(", ", items.OfType<FemResolvedModes>().Select(m => $"«{m.Label}»"))}) не сочетаются " +
                "с креном и погибями в одном варианте расчёта (EN 1993-1-1 п. 5.3.2(11) — взамен п. (3) и (6)).");
        return new FemImperfectionResolution(items, diag);
    }

    /// <summary>
    /// Погибь стен: КЭ стен охвата по ярусам стенок (<see cref="FemBracedLength.WallTier"/> — уровни раскрепления
    /// перекрытиями и ригелями), стрелка по режиму для высоты яруса h_яр; null — ошибка (в <paramref name="error"/>).
    /// Ярус без узлов строго между уровнями (один КЭ по высоте) — пропуск с предупреждением: парабола в узлах — нули.
    /// </summary>
    static FemResolvedWallBow? ResolveWallBow(string label, FemWallBowParams p, double factor,
        IReadOnlyDictionary<string, List<FemElement>> byMember, IReadOnlyDictionary<string, FemElement> elements,
        IReadOnlyDictionary<string, PlanarVector3> nodes, FemBracedLength braced, Action<string, string> error,
        Action<FemValidationDiagnostic> warn)
    {
        var c = CultureInfo.CurrentCulture;
        if (p.E0Mode is not (FemBowParams.E0En1992 or FemBowParams.E0En1992Simplified or FemBowParams.E0Sp63
            or FemBowParams.E0Relative or FemBowParams.E0Absolute))
        {
            error("imperfection_e0_invalid", $"Погибь стены «{label}»: неизвестный способ задания e₀ «{p.E0Mode}».");
            return null;
        }
        double mu = p.Mu ?? 1.0;
        if (p.E0Mode is FemBowParams.E0En1992 or FemBowParams.E0En1992Simplified && (!double.IsFinite(mu) || mu <= 0))
        {
            error("imperfection_mu_invalid", $"Погибь стены «{label}»: μ расчётной длины l₀ = μ·h_яр должно быть положительным числом.");
            return null;
        }
        if (p.E0Mode == FemBowParams.E0Relative && (p.LOverE0 is not { } r || !double.IsFinite(r) || r <= 0))
        {
            error("imperfection_e0_invalid", $"Погибь стены «{label}»: h_яр/e₀ должно быть положительным числом.");
            return null;
        }
        if (p.E0Mode == FemBowParams.E0Absolute && (p.E0Mm is not { } mm || !double.IsFinite(mm)))
        {
            error("imperfection_e0_invalid", $"Погибь стены «{label}»: e₀ (мм) должно быть числом.");
            return null;
        }
        if (p.ThicknessMm is { } tmm && !(double.IsFinite(tmm) && tmm > 0))
        {
            error("imperfection_section_height", $"Погибь стены «{label}»: толщина стены (мм) должна быть положительной.");
            return null;
        }
        if (p.Sign is not (FemBowParams.SignAuto or FemBowParams.SignPlus or FemBowParams.SignMinus))
        {
            error("imperfection_sign_invalid", $"Погибь стены «{label}»: неизвестный знак «{p.Sign}».");
            return null;
        }

        // Охват: КЭ пластин КонЭ и КЭ из списка.
        var targets = new List<FemElement>();
        var missing = new List<string>();
        foreach (var tag in p.MemberTags)
            if (byMember.TryGetValue(tag, out var list) && list.Any(e => e.ElemType == "shell"))
                targets.AddRange(list.Where(e => e.ElemType == "shell"));
            else missing.Add(tag);
        foreach (var tag in p.ElementTags)
            if (elements.TryGetValue(tag, out var e)) targets.Add(e);
            else missing.Add(tag);
        if (missing.Count > 0)
            error("imperfection_target_missing", $"Погибь стены «{label}»: нет КонЭ-пластин или КЭ сетки {FemLoadTargets.Sample(missing)}.");
        targets = targets.DistinctBy(e => e.ElemTag).ToList();
        if (targets.Count == 0)
        {
            error("imperfection_scope_empty", $"Погибь стены «{label}»: не заданы стены.");
            return null;
        }
        var notWalls = new List<string>();
        var groups = new Dictionary<(int Panel, double Bottom, double Top), (FemWallTier Tier, List<FemElement> Elements)>();
        foreach (var e in targets)
        {
            if (e.ElemType != "shell" || braced.WallTier(e) is not { } tier) { notWalls.Add(e.ElemTag); continue; }
            var key = (tier.Panel, Math.Round(tier.ZBottom, 6), Math.Round(tier.ZTop, 6));
            if (!groups.TryGetValue(key, out var g)) groups[key] = g = (tier, []);
            g.Elements.Add(e);
        }
        if (notWalls.Count > 0)
        {
            error("imperfection_wall_not_wall",
                $"Погибь стены «{label}»: КЭ {FemLoadTargets.Sample(notWalls)} — не пластины-стены (нормаль не горизонтальна или нет узлов).");
            return null;
        }

        double sign = p.Sign == FemBowParams.SignMinus ? -1 : 1;
        var tiers = new List<FemResolvedWallTier>();
        var coarse = new List<string>();
        foreach (var ((panel, _, _), (tier, list)) in groups.OrderBy(kv => kv.Key.Panel).ThenBy(kv => kv.Key.Item2))
        {
            double h = tier.ZTop - tier.ZBottom;
            string panelName = list[0].SourceMemberTag is { } mt ? $"КонЭ {mt}" : $"стенка КЭ {list[0].ElemTag}";
            string tierName = string.Create(c, $"{panelName}, z {tier.ZBottom:0.###}…{tier.ZTop:0.###} м");
            // Узлы строго внутри яруса: иначе парабола в узлах — нули.
            const double tol = 1e-3;
            bool inner = list.SelectMany(e => FemMeshTopology.ReadNodeTags(e) ?? [])
                .Select(FemMeshTopology.CanonicalNodeTag)
                .Any(t => t != null && nodes.TryGetValue(t, out var q) && q.Z > tier.ZBottom + tol && q.Z < tier.ZTop - tol);
            if (!inner) { coarse.Add(tierName); continue; }
            double t;
            string tWhy;
            if (p.ThicknessMm is { } tm) { t = tm / 1000.0; tWhy = "t вручную"; }
            else
            {
                t = list.Select(e => e.ThicknessM ?? 0).Max();
                tWhy = "t — толщина КЭ";
            }
            double e0;
            string source;
            switch (p.E0Mode)
            {
                case FemBowParams.E0En1992:
                    e0 = En1992Imperfections.BowEi(h, mu, out source);
                    break;
                case FemBowParams.E0En1992Simplified:
                    e0 = En1992Imperfections.BowSimplified(h, mu, out source);
                    break;
                case FemBowParams.E0Sp63:
                    if (!(t > 0))
                    {
                        error("imperfection_section_height", $"Погибь стены «{label}», {tierName}: толщина стены не задана — задайте её вручную.");
                        return null;
                    }
                    e0 = Sp63AccidentalEccentricity.Ea(h, t, out string ea);
                    source = $"{ea} ({tWhy}; l — высота яруса)";
                    break;
                case FemBowParams.E0Relative:
                    e0 = h / p.LOverE0!.Value;
                    source = string.Create(c, $"h_яр/e₀ = {p.LOverE0!.Value:0.###} вручную");
                    break;
                default:
                    e0 = p.E0Mm!.Value / 1000.0;
                    source = string.Create(c, $"e₀ = {p.E0Mm!.Value:0.###} мм вручную");
                    break;
            }
            // Нормаль — детерминированно: n_x > 0, иначе n_y > 0.
            var n = new PlanarVector3(tier.Normal.X, tier.Normal.Y, 0);
            double nl = n.Length;
            if (!(nl > 1e-9)) continue;
            n = n * (1.0 / nl);
            if (n.X < -1e-9 || (Math.Abs(n.X) <= 1e-9 && n.Y < 0)) n = n * -1.0;
            tiers.Add(new FemResolvedWallTier(tierName, list.Select(e => e.ElemTag).ToList(), tier.ZBottom, tier.ZTop, n,
                factor * sign * e0) { Thickness = t, E0Source = source });
        }
        if (coarse.Count > 0)
            warn(new("imperfection_wall_coarse",
                $"Погибь стены «{label}»: у ярусов {string.Join("; ", coarse.Take(5))}{(coarse.Count > 5 ? "…" : "")} нет узлов сетки между " +
                "уровнями — погибь не представима (нужно не меньше 2 КЭ по высоте яруса, лучше 4) и не учтена.", false));
        if (tiers.Count == 0)
        {
            error("imperfection_scope_empty", $"Погибь стены «{label}»: нет ярусов, в которых погибь представима.");
            return null;
        }
        return new FemResolvedWallBow(label, tiers) { SignAuto = p.Sign == FemBowParams.SignAuto };
    }

    /// <summary>
    /// Высота сечения в плоскости погиби по сечениям CScore КЭ цепочки (<see cref="FemImperfectionContext.BeamCross"/>):
    /// размах контуров областей вдоль оси сечения, совпадающей с направлением погиби (у КЭ своей схемы ось Y сечения —
    /// по местной y, у импортного — ось X); разные по цепочке — наибольшая (в запас). null — не определена.
    /// </summary>
    static double? SectionHeight(IReadOnlyList<string> tags, IReadOnlyDictionary<string, FemElement> elements, bool alongLocalY,
        FemImperfectionContext? context, out string why)
    {
        if (context?.BeamCross == null) { why = "нет сечений стержней"; return null; }
        double best = 0;
        var distinct = new HashSet<double>();
        foreach (var tag in tags)
        {
            var e = elements[tag];
            if (context.BeamCross(tag) is not { } cs) { why = $"у КЭ {tag} нет сечения CScore"; return null; }
            bool yAlongY = e.Origin != FemMember.MeshSourceImported;
            bool alongSectionY = alongLocalY == yAlongY;
            double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            foreach (var area in cs.Areas)
                foreach (var pt in area.Hull?.Points ?? [])
                {
                    double v = alongSectionY ? pt.Y : pt.X;
                    lo = Math.Min(lo, v);
                    hi = Math.Max(hi, v);
                }
            if (!(hi > lo)) { why = $"у сечения «{cs.Tag}» КЭ {tag} нет контура"; return null; }
            distinct.Add(Math.Round(hi - lo, 6));
            best = Math.Max(best, hi - lo);
        }
        why = string.Create(CultureInfo.CurrentCulture, $"h — по сечению{(distinct.Count > 1 ? " (наибольшая по КЭ цепочки)" : "")}, {best * 1000:0.#} мм");
        return best;
    }

    /// <summary>
    /// «Формы»: проверка параметров, формы постановки (<see cref="FemImperfectionContext.Modes"/>), сумма u₀ = Σ aᵢ·φᵢ
    /// с коэффициентом загружения; null — ошибка (в <paramref name="error"/>).
    /// </summary>
    static FemResolvedModes? ResolveModes(string label, FemModesParams p, double factor, FemImperfectionContext? context,
        Action<string, string> error)
    {
        if (p.AnalysisId <= 0)
        {
            error("imperfection_modes_source", $"Формы «{label}»: не выбрана постановка устойчивости.");
            return null;
        }
        if (p.Modes.Count == 0)
        {
            error("imperfection_modes_empty", $"Формы «{label}»: не заданы формы и амплитуды.");
            return null;
        }
        if (p.Modes.Any(m => m.Mode < 1 || !double.IsFinite(m.AmplitudeMm)))
        {
            error("imperfection_modes_invalid", $"Формы «{label}»: номер формы — целое не меньше 1, амплитуда — число (мм).");
            return null;
        }
        if (p.Modes.Select(m => m.Mode).Distinct().Count() != p.Modes.Count)
        {
            error("imperfection_modes_invalid", $"Формы «{label}»: форма указана дважды.");
            return null;
        }
        if (context?.Modes == null)
        {
            error("imperfection_modes_source", $"Формы «{label}»: формы постановки устойчивости недоступны.");
            return null;
        }
        var found = context.Modes(p);
        if (found.ErrorCode != null)
        {
            error(found.ErrorCode, $"Формы «{label}»: {found.Error}");
            return null;
        }
        var missing = p.Modes.Where(m => m.Mode > found.Lambda.Count || !found.Shapes.ContainsKey(m.Mode)).Select(m => m.Mode).ToList();
        if (missing.Count > 0)
        {
            error("imperfection_modes_missing",
                $"Формы «{label}»: в задаче «{found.CaseTag}» постановки «{found.AnalysisTag}» форм {found.Lambda.Count}, нет форм {string.Join(", ", missing)}.");
            return null;
        }
        var nodes = new Dictionary<int, double[]>();
        var bars = new Dictionary<int, double[][]>();
        var rows = new List<FemResolvedMode>();
        foreach (var m in p.Modes)
        {
            double a = factor * m.AmplitudeMm / 1000.0;
            var shape = found.Shapes[m.Mode];
            rows.Add(new FemResolvedMode(m.Mode, found.Lambda[m.Mode - 1], a));
            foreach (var (id, u) in shape.Nodes)
            {
                var acc = nodes.TryGetValue(id, out var x) ? x : nodes[id] = new double[6];
                for (int k = 0; k < Math.Min(6, u.Length); k++) acc[k] += a * u[k];
            }
            foreach (var (id, points) in shape.BarPoints)
            {
                if (!bars.TryGetValue(id, out var acc)) bars[id] = acc = points.Select(_ => new double[3]).ToArray();
                if (acc.Length != points.Length)
                {
                    error("imperfection_modes_invalid", $"Формы «{label}»: у КЭ {id} формы с разным дроблением — пересчитайте постановку устойчивости.");
                    return null;
                }
                for (int i = 0; i < points.Length; i++)
                    for (int k = 0; k < 3; k++) acc[i][k] += a * points[i][k];
            }
        }
        return new FemResolvedModes(label, found.AnalysisTag, found.CaseTag, rows, nodes, bars)
        {
            AlphaCr = found.Lambda.Count > 0 ? found.Lambda[0] : null,
        };
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

    /// <summary>
    /// Размах z охвата крена <paramref name="p"/> по сетке (h по умолчанию для (5.5), как в <see cref="Resolve"/>): вся схема
    /// — все стержни и пластины, перечень — КЭ КонЭ и КЭ из списка (отсутствующие пропускаются), м; 0 — узлов нет.
    /// </summary>
    public static double SwaySpanZ(FemSwayParams p, IReadOnlyList<FemElement> meshElements, IReadOnlyList<FemMeshNode> meshNodes)
    {
        ArgumentNullException.ThrowIfNull(p);
        var elements = new Dictionary<string, FemElement>(StringComparer.Ordinal);
        foreach (var e in meshElements) elements.TryAdd(e.ElemTag, e);
        var nodes = new Dictionary<string, PlanarVector3>(StringComparer.Ordinal);
        foreach (var n in meshNodes)
            if (FemMeshTopology.CanonicalNodeTag(n.NodeTag) is { } tag) nodes.TryAdd(tag, new PlanarVector3(n.X, n.Y, n.Z));
        List<string>? scope = null;
        if (p.Scope == FemSwayParams.ScopeList)
        {
            var members = p.MemberTags.ToHashSet(StringComparer.Ordinal);
            scope = meshElements.Where(e => e.SourceMemberTag != null && members.Contains(e.SourceMemberTag)).Select(e => e.ElemTag)
                .Concat(p.ElementTags.Where(elements.ContainsKey)).Distinct(StringComparer.Ordinal).ToList();
        }
        return SpanZ(scope, meshElements, elements, nodes);
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
