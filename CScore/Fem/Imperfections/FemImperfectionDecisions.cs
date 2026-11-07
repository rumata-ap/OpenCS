using System.Globalization;
using CScore.En1993;
using CScore.Planar;

namespace CScore.Fem.Imperfections;

/// <summary>
/// Состояние предварительного упругого расчёта первого порядка (спека 5.4): упругие сечения, без несовершенств, нагрузка
/// конца последней стадии. По нему решаются знак «авто» и активность погибей, подсказка m и H_Ed/V_Ed крена.
/// </summary>
public interface IFemFirstOrderState
{
    /// <summary>Продольная сила стержневого КЭ, Н (растяжение — плюс); null — КЭ нет в модели.</summary>
    double? AxialForce(string elemTag);

    /// <summary>Перемещения узла сетки (ux, uy, uz, θx, θy, θz), м и рад, глобальные оси; null — узла нет в модели.</summary>
    double[]? NodeDisplacement(string nodeTag);

    /// <summary>Изгибная жёсткость КЭ, Н·м², для прогиба вдоль местной y (EI_z) или z (EI_y); null — нет КЭ.</summary>
    double? BendingStiffness(string elemTag, bool alongLocalY);

    /// <summary>Конец КЭ у узла <paramref name="nodeTag"/> воспринимает момент изгиба с прогибом вдоль местной y/z (п. 5.3.2(6)):
    /// нет шарнира КЭ и узел защемлён по повороту (опора, пружина, жёсткое тело, другой стержень, оболочка); null — нет КЭ
    /// или узел не его конец.</summary>
    bool? MomentEnd(string elemTag, string nodeTag, bool alongLocalY);

    /// <summary>Модуль равнодействующей горизонтальных реакций опор |ΣH| и |ΣV|, Н.</summary>
    (double H, double V) ReactionSums { get; }
}

/// <summary>Строка отчёта крена (спека 5.5).</summary>
public sealed class FemSwayReportRow
{
    public string Label { get; set; } = "";
    /// <summary>φ со знаком коэффициента загружения.</summary>
    public double Phi { get; set; }
    /// <summary>Направление в плане, град от +X.</summary>
    public double AngleDeg { get; set; }
    public double? H { get; set; }
    public double? AlphaH { get; set; }
    public int? M { get; set; }
    public double? AlphaM { get; set; }
    /// <summary>Ход вычисления φ или «1/φ вручную».</summary>
    public string Source { get; set; } = "";
    /// <summary>Подсказка m по предварительному расчёту (стойки с N &gt; 50 % средней); null — не считалась.</summary>
    public int? MHint { get; set; }
    /// <summary>Стоек охвата со сжатием (для подсказки m).</summary>
    public int? Columns { get; set; }
    /// <summary>H_Ed/V_Ed реакций (п. 5.3.2(4)B, справочно); null — без предварительного расчёта.</summary>
    public double? HOverV { get; set; }
    /// <summary>θᵢ по EN 1992-1-1 (5.1) — подсказка m: все сжатые стойки (без правила 50 %).</summary>
    public bool En1992 { get; set; }
}

/// <summary>Строка отчёта погиби стены — один ярус.</summary>
public sealed class FemWallBowReportRow
{
    public string Label { get; set; } = "";
    /// <summary>Стенка и ярус («КонЭ 12, z 0…3 м»).</summary>
    public string Tier { get; set; } = "";
    public double ZBottom { get; set; }
    public double ZTop { get; set; }
    /// <summary>Толщина стены, м.</summary>
    public double Thickness { get; set; }
    public string E0Source { get; set; } = "";
    /// <summary>Итоговая стрелка, м, со знаком (вдоль нормали стенки).</summary>
    public double E0 { get; set; }
    /// <summary>Нормаль стенки (для подписи знака), глобальные оси.</summary>
    public double NormalX { get; set; }
    public double NormalY { get; set; }
    public string Sign { get; set; } = "+";
    public string SignSource { get; set; } = "";
}

/// <summary>Строка отчёта погиби (спека 5.5).</summary>
public sealed class FemBowReportRow
{
    public string Label { get; set; } = "";
    public double LengthM { get; set; }
    /// <summary>«y» или «z» — местная ось прогиба.</summary>
    public string Plane { get; set; } = "y";
    /// <summary>Кривая табл. 6.2 (a0…d), пусто — e₀ не по таблице.</summary>
    public string Curve { get; set; } = "";
    public string E0Source { get; set; } = "";
    /// <summary>Итоговая стрелка, м, со знаком (0 — неактивна).</summary>
    public double E0 { get; set; }
    /// <summary>Наибольшее сжатие по цепочке, Н (плюс — сжатие); null — нет предварительного расчёта.</summary>
    public double? NEd { get; set; }
    /// <summary>N_cr шарнирного стержня в плоскости, Н.</summary>
    public double? Ncr { get; set; }
    public bool Active { get; set; } = true;
    public string ActiveReason { get; set; } = "";
    /// <summary>«+» или «−».</summary>
    public string Sign { get; set; } = "+";
    public string SignSource { get; set; } = "";
}

/// <summary>Строка отчёта форм потери устойчивости (спека 5.5): элемент, постановка и задача, форма, λ, амплитуда.</summary>
public sealed class FemModeReportRow
{
    public string Label { get; set; } = "";
    public string Analysis { get; set; } = "";
    public string Case { get; set; } = "";
    public int Mode { get; set; }
    public double Lambda { get; set; }
    /// <summary>Амплитуда, м, со знаком и коэффициентом загружения.</summary>
    public double Amplitude { get; set; }
    /// <summary>Ход амплитуды по EN 1993-1-1 (5.9)–(5.11); пусто — задана вручную.</summary>
    public string Source { get; set; } = "";
}

/// <summary>Отчёт несовершенств варианта расчёта (<c>FemCsfeaResultSummary.Imperfections</c>).</summary>
public sealed class FemImperfectionReport
{
    public List<FemSwayReportRow> Sways { get; set; } = [];
    public List<FemBowReportRow> Bows { get; set; } = [];
    public List<FemModeReportRow> Modes { get; set; } = [];
    /// <summary>Ярусы погибей стен.</summary>
    public List<FemWallBowReportRow> WallBows { get; set; } = [];
    /// <summary>Предварительный упругий расчёт выполнен.</summary>
    public bool FirstOrder { get; set; }
    /// <summary>Наибольшее |u₀| (крен — |φ|·размах z охвата, погибь — |e₀| активных, формы — max поступательного суммы), м.</summary>
    public double MaxU0 { get; set; }
    /// <summary>Сведения об α_cr (п. 5.2.1(3)).</summary>
    public string AlphaCr { get; set; } = "";

    /// <summary>Строки отчёта для журнала и сводки.</summary>
    public IEnumerable<string> Lines()
    {
        var c = CultureInfo.CurrentCulture;
        foreach (var s in Sways)
        {
            string line = string.Create(c, $"Крен «{s.Label}»: φ = {Ratio(s.Phi)} по направлению {s.AngleDeg:0.#}° от +X; {s.Source}");
            if (s.MHint is { } m)
                line += s.En1992
                    ? string.Create(c, $"; подсказка m = {m} (сжатых вертикальных элементов охвата, EN 1992-1-1 п. 5.2(6))")
                    : string.Create(c, $"; подсказка m = {m} (стоек со сжатием {s.Columns}, N_Ed > 50 % средней)");
            if (s.HOverV is { } hv)
                line += string.Create(c, $"; H_Ed/V_Ed = {hv:0.###}{(hv >= 0.15 ? " ≥ 0,15 — по п. 5.3.2(4)B креном можно пренебречь (справочно)" : "")}");
            yield return line + ".";
        }
        foreach (var b in Bows)
        {
            string n = b.NEd is { } ned && b.Ncr is { } ncr
                ? string.Create(c, $"; N_Ed = {ned / 1e3:0.##} кН, N_cr = π²EI/L² = {ncr / 1e3:0.##} кН ({ned / ncr:0.###}·N_cr)") : "";
            yield return string.Create(c,
                $"Погибь «{b.Label}»: L = {b.LengthM:0.###} м, плоскость {b.Plane}, {b.E0Source}; e₀ = {b.E0 * 1000:0.##} мм"
                + $"{n}; {(b.Active ? "активна" : "не учитывается")} — {b.ActiveReason}; знак {b.Sign} — {b.SignSource}.");
        }
        foreach (var m in Modes)
            yield return string.Create(c,
                $"Формы «{m.Label}»: постановка «{m.Analysis}», задача «{m.Case}», форма {m.Mode} (λ = {m.Lambda:0.###}), амплитуда {m.Amplitude * 1000:0.##} мм")
                + (m.Source.Length > 0 ? $" — по EN 1993-1-1 п. 5.3.2(11): {m.Source}." : " (вручную).");
        foreach (var w in WallBows)
            yield return string.Create(c,
                $"Погибь стены «{w.Label}», {w.Tier}: h_яр = {w.ZTop - w.ZBottom:0.###} м, t = {w.Thickness * 1000:0.#} мм, {w.E0Source}; "
                + $"e₀ = {w.E0 * 1000:0.##} мм вдоль нормали ({w.NormalX:0.###}; {w.NormalY:0.###}); знак {w.Sign} — {w.SignSource}.");
        if (Sways.Count + Bows.Count + Modes.Count + WallBows.Count > 0)
        {
            yield return string.Create(c, $"Несовершенства: max|u₀| = {MaxU0 * 1000:0.##} мм.");
            if (!string.IsNullOrEmpty(AlphaCr)) yield return AlphaCr;
        }
    }

    /// <summary>
    /// Решённые несовершенства по отчёту (для показа u₀ результата без повторного предварительного расчёта): строки
    /// отчёта идут в порядке элементов первого этапа (<see cref="FemImperfectionResolver.Resolve"/>, крены и погиби —
    /// каждые по порядку); неактивные погиби убираются, стрелка со знаком — из отчёта. null — состав элементов
    /// (число и подписи) не совпадает с отчётом (схема изменилась после расчёта).
    /// </summary>
    public IReadOnlyList<FemResolvedImperfection>? Apply(IReadOnlyList<FemResolvedImperfection> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var sways = items.OfType<FemResolvedSway>().ToList();
        var bows = items.OfType<FemResolvedBow>().ToList();
        if (sways.Count != Sways.Count || bows.Count != Bows.Count) return null;
        var modes = items.OfType<FemResolvedModes>().ToList();
        if (modes.Select(m => m.Label).Distinct().Count() != Modes.Select(m => m.Label).Distinct().Count()) return null;
        if (sways.Where((s, i) => s.Label != Sways[i].Label).Any() || bows.Where((b, i) => b.Label != Bows[i].Label).Any()) return null;
        var walls = items.OfType<FemResolvedWallBow>().ToList();
        if (walls.Sum(w => w.Tiers.Count) != WallBows.Count) return null;
        var result = new List<FemResolvedImperfection>(items.Count);
        int k = 0, kw = 0;
        foreach (var item in items)
        {
            if (item is FemResolvedWallBow wall)
            {
                var tiers = new List<FemResolvedWallTier>(wall.Tiers.Count);
                foreach (var tier in wall.Tiers)
                {
                    var row = WallBows[kw++];
                    if (row.Label != wall.Label || row.Tier != tier.Panel) return null;
                    tiers.Add(tier with { E0 = row.E0 });
                }
                result.Add(wall with { Tiers = tiers });
                continue;
            }
            if (item is not FemResolvedBow bow) { result.Add(item); continue; }
            var brow = Bows[k++];
            if (brow.Active) result.Add(bow with { E0 = brow.E0 });
        }
        return result;
    }

    static string Ratio(double phi) => phi == 0 ? "0" : string.Create(CultureInfo.CurrentCulture, $"{(phi < 0 ? "−" : "")}1/{1 / Math.Abs(phi):0.#}");
}

/// <summary>Итог второго этапа: несовершенства для расчёта (неактивные погиби убраны, знаки решены), отчёт и диагностики.</summary>
public sealed record FemImperfectionDecision(IReadOnlyList<FemResolvedImperfection> Items, FemImperfectionReport Report,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics);

public static partial class FemImperfectionResolver
{
    /// <summary>Сведения об α_cr без посчитанной постановки устойчивости с совпадающим отпечатком геометрии.</summary>
    public const string AlphaCrNotComputed =
        "α_cr не посчитан (нет посчитанной постановки «Расчётные длины — CSfea» с той же геометрией схемы); при α_cr ≥ 10 расчёт второго порядка не обязателен (EN 1993-1-1 п. 5.2.1(3)).";

    /// <summary>
    /// Строка α_cr отчёта по λ₁ задачи постановки устойчивости (п. 5.2.1(3): ≥ 10 — второй порядок при упругом расчёте
    /// не обязателен; п. 5.2.2(5)B — множитель 1/(1 − 1/α_cr) при α_cr ≥ 3).
    /// </summary>
    public static string AlphaCrLine(double alphaCr, string analysis, string caseTag)
    {
        var c = CultureInfo.CurrentCulture;
        string verdict = alphaCr >= 10 ? "≥ 10 — расчёт второго порядка не обязателен (EN 1993-1-1 п. 5.2.1(3), упругий расчёт)"
            : alphaCr > 1 ? string.Create(c, $"< 10 — эффекты второго порядка учитываются (п. 5.2.1(3)); 1/(1 − 1/α_cr) = {1 / (1 - 1 / alphaCr):0.###}")
            : "≤ 1 — схема неустойчива при нагрузке задачи";
        return string.Create(c, $"α_cr = λ₁ = {alphaCr:0.###} (постановка «{analysis}», задача «{caseTag}»): {verdict}.");
    }

    /// <summary>
    /// Второй этап (спека 5.4): по предварительному расчёту <paramref name="state"/> — знак «авто» (прогиб середины
    /// цепочки относительно хорды вдоль направления погиби, кубика Эрмита по перемещениям и поворотам концов КЭ; |δ| &lt;
    /// 1e−9·L — «+»), активность по п. 5.3.2(6) (N_Ed — наибольшее сжатие по цепочке, EI — наименьшая по цепочке, конец с
    /// моментом — <see cref="IFemFirstOrderState.MomentEnd"/> концевого КЭ), подсказка m (стойки охвата: вертикальные КЭ, по КонЭ) и H_Ed/V_Ed.
    /// <paramref name="state"/> null (расчёт не выполнен, <paramref name="stateError"/> — почему) — знак «+», погиби
    /// всегда, с предупреждениями.
    /// </summary>
    /// <param name="alphaCr">Строка α_cr по постановке устойчивости схемы с совпадающей геометрией
    /// (<see cref="AlphaCrLine"/>); null — <see cref="AlphaCrNotComputed"/>. У несовершенства по формам — λ₁ его задачи.</param>
    public static FemImperfectionDecision Decide(IReadOnlyList<FemResolvedImperfection> items, IFemFirstOrderState? state,
        IReadOnlyList<FemElement> meshElements, IReadOnlyList<FemMeshNode> meshNodes, string? stateError = null,
        string? alphaCr = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        var c = CultureInfo.CurrentCulture;
        var result = new List<FemResolvedImperfection>();
        var diag = new List<FemValidationDiagnostic>();
        var report = new FemImperfectionReport { FirstOrder = state != null, AlphaCr = alphaCr ?? AlphaCrNotComputed };
        var elements = new Dictionary<string, FemElement>(StringComparer.Ordinal);
        foreach (var e in meshElements) elements.TryAdd(e.ElemTag, e);
        var nodes = new Dictionary<string, PlanarVector3>(StringComparer.Ordinal);
        foreach (var n in meshNodes)
            if (FemMeshTopology.CanonicalNodeTag(n.NodeTag) is { } tag) nodes.TryAdd(tag, new PlanarVector3(n.X, n.Y, n.Z));
        string why = stateError is { Length: > 0 } ? stateError : "предварительный расчёт не выполнен";

        foreach (var item in items)
            switch (item)
            {
                case FemResolvedSway sw:
                {
                    var row = new FemSwayReportRow
                    {
                        Label = sw.Label, Phi = sw.Phi, AngleDeg = Math.Atan2(sw.Direction.Y, sw.Direction.X) * 180 / Math.PI,
                        H = sw.Norm?.H ?? sw.Theta?.L, AlphaH = sw.Norm?.AlphaH ?? sw.Theta?.AlphaH,
                        M = sw.Norm?.M ?? sw.Theta?.M, AlphaM = sw.Norm?.AlphaM ?? sw.Theta?.AlphaM, En1992 = sw.Theta != null,
                        Source = sw.Norm is { } norm ? $"(5.5), {sw.HeightSource}: {norm.Describe()}"
                            : sw.Theta is { } theta
                                ? $"EN 1992-1-1 (5.1), {sw.HeightSource}: {theta.Describe()}; случай п. 5.2(6) — раскрепляющая система " +
                                  "(охват — один элемент: l — его длина, m = 1); силы в диафрагмах (5.5), (5.6) не моделируются"
                            : string.Create(c, $"1/φ = {1 / Math.Abs(sw.Phi):0.#} вручную"),
                    };
                    if (state != null)
                    {
                        (row.MHint, row.Columns) = ColumnHint(sw.ElemTags, meshElements, elements, nodes, state);
                        if (row.En1992 && row.Columns is { } cols) row.MHint = cols;
                        var (h, v) = state.ReactionSums;
                        if (v > 0) row.HOverV = h / v;
                    }
                    report.Sways.Add(row);
                    report.MaxU0 = Math.Max(report.MaxU0, Math.Abs(sw.Phi) * sw.SpanZ);
                    result.Add(sw);
                    break;
                }
                case FemResolvedBow bow:
                {
                    var row = new FemBowReportRow
                    {
                        Label = bow.Label, LengthM = bow.LengthM, Plane = bow.AlongLocalY ? "y" : "z",
                        Curve = bow.Curve is { } cv ? En1993Imperfections.Name(cv) : "", E0Source = bow.E0Source,
                    };
                    double e0 = bow.E0;
                    // Предварительный расчёт: N_Ed, N_cr, концы с моментом.
                    double? nEd = null, ei = null;
                    bool? momentEnd = null;
                    if (state != null)
                    {
                        nEd = bow.ElemTags.Select(t => state.AxialForce(t)).Aggregate((double?)double.NegativeInfinity,
                            (acc, n) => acc == null || n == null ? null : Math.Max(acc.Value, -n.Value));
                        ei = bow.ElemTags.Select(t => state.BendingStiffness(t, bow.AlongLocalY)).Aggregate((double?)double.PositiveInfinity,
                            (acc, k) => acc == null || k == null ? null : Math.Min(acc.Value, k.Value));
                        momentEnd = EndMoment(bow, bow.StartNode, elements, state) is { } a && EndMoment(bow, bow.StopNode, elements, state) is { } b
                            ? a || b : null;
                        if (nEd is { } ned && ei is { } k0 && k0 > 0)
                        {
                            row.NEd = ned;
                            row.Ncr = En1993Imperfections.NcrPinned(k0, bow.LengthM);
                        }
                    }

                    // Активность.
                    bool active = true;
                    if (bow.Concrete) row.ActiveReason = "ЖБ — учитывается всегда (EN 1992-1-1 п. 5.2(1)P)";
                    else if (!bow.ActivityEn1993) row.ActiveReason = "задано «всегда»";
                    else if (state == null || row.NEd == null || momentEnd == null)
                    {
                        row.ActiveReason = state == null ? $"критерий п. 5.3.2(6) не проверен ({why}) — учитывается всегда"
                            : "критерий п. 5.3.2(6) не проверен (КЭ цепочки нет в предварительном расчёте) — учитывается всегда";
                        diag.Add(new("imperfection_activity", $"Погибь «{bow.Label}»: {row.ActiveReason}.", false));
                    }
                    else
                    {
                        active = En1993Imperfections.BowActive(row.NEd.Value, ei!.Value, bow.LengthM, momentEnd.Value);
                        row.ActiveReason = !momentEnd.Value
                            ? "п. 5.3.2(6): оба конца шарнирные в плоскости погиби"
                            : row.NEd.Value <= 0 ? "п. 5.3.2(6): элемент не сжат"
                            : string.Create(c, $"п. 5.3.2(6): N_Ed {(active ? ">" : "≤")} 0,25·N_cr (λ̄ {(active ? ">" : "≤")} 0,5·√(A·f_y/N_Ed))");
                    }

                    // Знак.
                    double sign = 1;
                    if (!bow.SignAuto) row.SignSource = "задан";
                    else if (state == null || Deflection(bow, elements, nodes, state) is not { } delta)
                    {
                        row.SignSource = state == null ? $"«авто» не определён ({why}) — принят «+» местной оси"
                            : "«авто» не определён (узлы цепочки не найдены) — принят «+» местной оси";
                        diag.Add(new("imperfection_sign_auto", $"Погибь «{bow.Label}»: знак {row.SignSource}.", false));
                    }
                    else if (Math.Abs(delta) < 1e-9 * bow.LengthM)
                        row.SignSource = "«авто»: прогиба первого порядка нет — «+» местной оси";
                    else
                    {
                        sign = Math.Sign(delta);
                        row.SignSource = string.Create(c, $"«авто»: прогиб середины первого порядка δ = {delta * 1000:0.###} мм");
                    }
                    // «Авто» — относительно прогиба первого порядка; знак коэффициента загружения сохраняется (вариант
                    // пакета «−» — против прогиба).
                    if (bow.SignAuto) e0 *= sign;
                    row.Sign = e0 < 0 ? "−" : "+";
                    row.Active = active;
                    row.E0 = active ? e0 : 0;
                    report.Bows.Add(row);
                    if (!active) continue;
                    report.MaxU0 = Math.Max(report.MaxU0, Math.Abs(e0));
                    result.Add(bow with { E0 = e0 });
                    break;
                }
                case FemResolvedModes modes:
                {
                    foreach (var m in modes.Modes)
                        report.Modes.Add(new FemModeReportRow
                        {
                            Label = modes.Label, Analysis = modes.AnalysisTag, Case = modes.CaseTag, Mode = m.Mode, Lambda = m.Lambda,
                            Amplitude = m.AmplitudeM, Source = m.Source,
                        });
                    report.MaxU0 = Math.Max(report.MaxU0, modes.MaxU0);
                    if (modes.AlphaCr is { } a) report.AlphaCr = AlphaCrLine(a, modes.AnalysisTag, modes.CaseTag);
                    result.Add(modes);
                    break;
                }
                case FemResolvedWallBow wall:
                {
                    var tiers = new List<FemResolvedWallTier>(wall.Tiers.Count);
                    foreach (var tier in wall.Tiers)
                    {
                        var row = new FemWallBowReportRow
                        {
                            Label = wall.Label, Tier = tier.Panel, ZBottom = tier.ZBottom, ZTop = tier.ZTop, Thickness = tier.Thickness,
                            E0Source = tier.E0Source, NormalX = tier.Normal.X, NormalY = tier.Normal.Y,
                        };
                        double e0 = tier.E0;
                        double sign = 1;
                        if (!wall.SignAuto) row.SignSource = "задан (по нормали стенки)";
                        else if (state == null || TierDeflection(tier, elements, nodes, state) is not { } s)
                        {
                            row.SignSource = state == null ? $"«авто» не определён ({why}) — принят «+» нормали"
                                : "«авто» не определён (узлы яруса не найдены) — принят «+» нормали";
                            diag.Add(new("imperfection_sign_auto", $"Погибь стены «{wall.Label}», {tier.Panel}: знак {row.SignSource}.", false));
                        }
                        else if (Math.Abs(s) < 1e-9 * tier.Height)
                            row.SignSource = "«авто»: прогиба первого порядка из плоскости нет — «+» нормали";
                        else
                        {
                            sign = Math.Sign(s);
                            row.SignSource = string.Create(c, $"«авто»: прогиб яруса первого порядка из плоскости {s * 1000:0.###} мм");
                        }
                        if (wall.SignAuto) e0 *= sign;
                        row.E0 = e0;
                        row.Sign = e0 < 0 ? "−" : "+";
                        report.WallBows.Add(row);
                        report.MaxU0 = Math.Max(report.MaxU0, Math.Abs(e0));
                        tiers.Add(tier with { E0 = e0 });
                    }
                    result.Add(wall with { Tiers = tiers });
                    break;
                }
                default:
                    result.Add(item);
                    break;
            }
        int off = report.Bows.Count(b => !b.Active);
        if (off > 0)
            diag.Add(new("imperfection_bow_inactive",
                $"Погибей не учитывается по п. 5.3.2(6): {off} из {report.Bows.Count} (см. отчёт несовершенств).", false));
        return new FemImperfectionDecision(result, report, diag);
    }

    /// <summary>
    /// Прогиб яруса стены первого порядка из плоскости в мере параболы погиби, м: s = Σφ(ζ_k)·(u_k·n − ū(ζ_k)) / Σφ(ζ_k)
    /// по узлам КЭ яруса, φ(ζ) = 4ζ(1 − ζ), ū — линейная интерполяция средних нормальных перемещений узлов нижнего и
    /// верхнего уровней (смещение яруса как целого не влияет). null — нет узлов или перемещений.
    /// </summary>
    static double? TierDeflection(FemResolvedWallTier tier, IReadOnlyDictionary<string, FemElement> elements,
        IReadOnlyDictionary<string, PlanarVector3> nodes, IFemFirstOrderState state)
    {
        const double tol = 1e-3;
        var pts = new List<(double Zeta, double W)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in tier.ElemTags)
        {
            if (!elements.TryGetValue(tag, out var e)) continue;
            foreach (var raw in FemMeshTopology.ReadNodeTags(e) ?? [])
            {
                if (FemMeshTopology.CanonicalNodeTag(raw) is not { } nt || !seen.Add(nt) || !nodes.TryGetValue(nt, out var p)) continue;
                if (state.NodeDisplacement(nt) is not { Length: >= 3 } u) return null;
                double zeta = Math.Clamp((p.Z - tier.ZBottom) / tier.Height, 0, 1);
                pts.Add((zeta, u[0] * tier.Normal.X + u[1] * tier.Normal.Y + u[2] * tier.Normal.Z));
            }
        }
        var bottom = pts.Where(q => q.Zeta * tier.Height < tol).Select(q => q.W).ToList();
        var top = pts.Where(q => (1 - q.Zeta) * tier.Height < tol).Select(q => q.W).ToList();
        double wb = bottom.Count > 0 ? bottom.Average() : 0, wt = top.Count > 0 ? top.Average() : 0;
        double num = 0, den = 0;
        foreach (var (zeta, w) in pts)
        {
            double phi = 4 * zeta * (1 - zeta);
            num += phi * (w - (wb + (wt - wb) * zeta));
            den += phi;
        }
        return den > 0 ? num / den : null;
    }

    /// <summary>Концевой КЭ цепочки у узла <paramref name="node"/> передаёт момент в плоскости погиби.</summary>
    static bool? EndMoment(FemResolvedBow bow, string node, IReadOnlyDictionary<string, FemElement> elements, IFemFirstOrderState state)
    {
        foreach (var tag in bow.ElemTags)
        {
            if (!elements.TryGetValue(tag, out var e) || FemMeshTopology.ReadNodeTags(e, 2) is not { } nt) continue;
            if (nt.Any(t => FemMeshTopology.CanonicalNodeTag(t) == node)) return state.MomentEnd(tag, node, bow.AlongLocalY);
        }
        return null;
    }

    /// <summary>
    /// Прогиб середины цепочки первого порядка относительно хорды вдоль <see cref="FemResolvedBow.Direction"/>, м:
    /// w(L/2) по кубике Эрмита КЭ, содержащего середину (перемещения и наклоны (θ × t)·d его концов), минус среднее
    /// концов цепочки. null — нет узлов.
    /// </summary>
    static double? Deflection(FemResolvedBow bow, IReadOnlyDictionary<string, FemElement> elements,
        IReadOnlyDictionary<string, PlanarVector3> nodes, IFemFirstOrderState state)
    {
        if (!nodes.TryGetValue(bow.StartNode, out var a) || !nodes.TryGetValue(bow.StopNode, out var b)) return null;
        var t = (b - a) * (1.0 / bow.LengthM);
        var d = bow.Direction;
        (double W, double Slope)? At(string node)
        {
            if (state.NodeDisplacement(node) is not { Length: >= 6 } u) return null;
            var disp = new PlanarVector3(u[0], u[1], u[2]);
            var rot = new PlanarVector3(u[3], u[4], u[5]);
            return (disp.Dot(d), rot.Cross(t).Dot(d));
        }
        if (At(bow.StartNode) is not { } w0 || At(bow.StopNode) is not { } w1) return null;
        double half = bow.LengthM / 2;
        foreach (var tag in bow.ElemTags)
        {
            if (!elements.TryGetValue(tag, out var e) || FemMeshTopology.ReadNodeTags(e, 2) is not { } nt) continue;
            string? i = FemMeshTopology.CanonicalNodeTag(nt[0]), j = FemMeshTopology.CanonicalNodeTag(nt[1]);
            if (i == null || j == null || !nodes.TryGetValue(i, out var pi) || !nodes.TryGetValue(j, out var pj)) continue;
            double si = (pi - a).Dot(t), sj = (pj - a).Dot(t);
            if (si > sj) { (si, sj) = (sj, si); (i, j) = (j, i); }
            if (half < si - 1e-12 * bow.LengthM || half > sj + 1e-12 * bow.LengthM) continue;
            if (At(i) is not { } wi || At(j) is not { } wj) return null;
            double le = sj - si, xi = le > 0 ? (half - si) / le : 0;
            double n1 = 1 - 3 * xi * xi + 2 * xi * xi * xi, n2 = le * (xi - 2 * xi * xi + xi * xi * xi);
            double n3 = 3 * xi * xi - 2 * xi * xi * xi, n4 = le * (-xi * xi + xi * xi * xi);
            double wMid = n1 * wi.W + n2 * wi.Slope + n3 * wj.W + n4 * wj.Slope;
            return wMid - 0.5 * (w0.W + w1.W);
        }
        return null;
    }

    /// <summary>
    /// Подсказка m по (5.5): стойки охвата крена — вертикальные стержневые КЭ (|t_z| ≥ 0,9), объединённые по КонЭ; N стойки —
    /// наибольшее сжатие её КЭ; m — число стоек с N больше 50 % средней по сжатым. (null, null) — сжатых стоек нет.
    /// </summary>
    static (int?, int?) ColumnHint(IReadOnlyList<string>? scope, IReadOnlyList<FemElement> meshElements,
        IReadOnlyDictionary<string, FemElement> elements, IReadOnlyDictionary<string, PlanarVector3> nodes, IFemFirstOrderState state)
    {
        var list = scope == null ? meshElements : scope.Select(t => elements.GetValueOrDefault(t)).OfType<FemElement>();
        var columns = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var e in list.Where(e => e.ElemType == "beam"))
        {
            if (FemMeshTopology.ReadNodeTags(e, 2) is not { } nt) continue;
            if (FemMeshTopology.CanonicalNodeTag(nt[0]) is not { } i || FemMeshTopology.CanonicalNodeTag(nt[1]) is not { } j
                || !nodes.TryGetValue(i, out var pi) || !nodes.TryGetValue(j, out var pj)) continue;
            var axis = pj - pi;
            double len = axis.Length;
            if (!(len > 0) || Math.Abs(axis.Z) / len < 0.9) continue;
            if (state.AxialForce(e.ElemTag) is not { } n || !(-n > 0)) continue;
            string key = e.SourceMemberTag ?? "#" + e.ElemTag;
            columns[key] = Math.Max(columns.GetValueOrDefault(key), -n);
        }
        if (columns.Count == 0) return (null, null);
        double avg = columns.Values.Average();
        return (Math.Max(1, columns.Values.Count(v => v > 0.5 * avg)), columns.Count);
    }
}
