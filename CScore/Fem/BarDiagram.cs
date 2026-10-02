using System.Globalization;

namespace CScore.Fem;

/// <summary>Участок эпюры: дуговые координаты концов (<c>S0 ≤ S1</c>), м, и значения на них.</summary>
public readonly record struct BarDiagramSegment(double S0, double S1, double V0, double V1);

/// <summary>Значение эпюры в сечении КЭ — строка таблицы под эпюрой.</summary>
/// <param name="ElemNum">Номер КЭ.</param>
/// <param name="SectionNum">Номер сечения КЭ; null — у строк усилий его нет.</param>
/// <param name="S">Дуговая координата сечения, м.</param>
/// <param name="Max">Наибольшее значение в сечении; null — значения нет.</param>
/// <param name="Min">Наименьшее значение в сечении (при одной строке усилий равно <paramref name="Max"/>).</param>
/// <param name="FailureCode">Код отказа подбора арматуры программы-источника; null — отказа нет.</param>
public sealed record BarDiagramPoint(int ElemNum, int? SectionNum, double S, double? Max, double? Min, int? FailureCode = null,
    bool Failed = false);

/// <summary>Эпюра вдоль цепочки стержневых КЭ.</summary>
/// <param name="Upper">Эпюра наибольших значений (при одной строке на сечение — сама эпюра).</param>
/// <param name="Lower">Эпюра наименьших значений; пусто, если в каждом сечении одно значение.</param>
/// <param name="Points">Значения по сечениям в порядке обхода цепочки.</param>
public sealed record BarDiagramSeries(
    IReadOnlyList<BarDiagramSegment> Upper, IReadOnlyList<BarDiagramSegment> Lower, IReadOnlyList<BarDiagramPoint> Points)
{
    /// <summary>Пустая эпюра.</summary>
    public static BarDiagramSeries Empty { get; } = new([], [], []);
}

/// <summary>Построение эпюр импортированных усилий и подобранной арматуры вдоль цепочки стержневых КЭ.</summary>
public static class BarDiagram
{
    /// <summary>
    /// Эпюра компоненты усилий набора. Сечения КЭ (<see cref="LoadItem.SourceSectionNum"/>: 1 … n) стоят
    /// равномерно от начального узла к конечному; между сечениями эпюра линейна, между КЭ — с разрывом.
    /// При нескольких строках в сечении (РСУ, РСН) строятся две эпюры — наибольших и наименьших значений.
    /// </summary>
    public static BarDiagramSeries Forces(BarChain chain, ForceSet set, BarForceComponent component)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(set);
        int c = (int)component;
        var byElem = set.Envelope(shell: false).ByElement();

        var upper = new List<BarDiagramSegment>();
        var lower = new List<BarDiagramSegment>();
        var points = new List<BarDiagramPoint>();
        bool envelope = false;
        foreach (var e in chain.Elements)
        {
            var sections = Sections(byElem[e.ElemNum], c);
            if (sections.Count == 0) continue;

            foreach (var s in sections)
            {
                points.Add(new BarDiagramPoint(e.ElemNum, s.Num, e.At(s.T), s.Max, s.Min));
                if (s.Max != s.Min) envelope = true;
            }
            if (sections.Count == 1)
            {
                // Одно сечение — постоянное значение по длине КЭ.
                upper.Add(Ordered(e.SStart, e.SEnd, sections[0].Max, sections[0].Max));
                lower.Add(Ordered(e.SStart, e.SEnd, sections[0].Min, sections[0].Min));
                continue;
            }
            for (int i = 0; i + 1 < sections.Count; i++)
            {
                var (a, b) = (sections[i], sections[i + 1]);
                upper.Add(Ordered(e.At(a.T), e.At(b.T), a.Max, b.Max));
                lower.Add(Ordered(e.At(a.T), e.At(b.T), a.Min, b.Min));
            }
        }
        return new BarDiagramSeries(Sort(upper), envelope ? Sort(lower) : [], points.OrderBy(p => p.S).ToList());
    }

    /// <summary>
    /// Эпюра подобранной арматуры: ступенчатая, каждое сечение КЭ занимает свою долю его длины
    /// (границы ступеней — посередине между сечениями). Сечения с отказом подбора в эпюру не входят,
    /// но остаются в <see cref="BarDiagramSeries.Points"/> с кодом отказа.
    /// </summary>
    public static BarDiagramSeries Rebar(BarChain chain, IBarRebarFieldSource source, BarRebarComponent component)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(source);
        var upper = new List<BarDiagramSegment>();
        var points = new List<BarDiagramPoint>();
        foreach (var e in chain.Elements)
        {
            var sections = source.GetSections(e.ElemNum.ToString(CultureInfo.InvariantCulture), component);
            int n = sections.Count;
            for (int k = 0; k < n; k++)
            {
                double t = n > 1 ? k / (double)(n - 1) : 0.5;
                var v = sections[k];
                points.Add(new BarDiagramPoint(e.ElemNum, k + 1, e.At(t), v.Value, v.Value, v.FailureCode));
                if (v.Value is not double value) continue;
                var (t0, t1) = StepBounds(k, n);
                upper.Add(Ordered(e.At(t0), e.At(t1), value, value));
            }
        }
        return new BarDiagramSeries(Sort(upper), [], points.OrderBy(p => p.S).ToList());
    }

    /// <summary>
    /// Профили усилий по КЭ для эпюр на схеме: точки (t, значение) вдоль КЭ, t = 0 — начальный узел,
    /// 1 — конечный. КЭ с одним сечением (или строками без номера сечения) — постоянное значение.
    /// </summary>
    /// <param name="aggregate">Выбор значения при нескольких строках в сечении.</param>
    public static Dictionary<int, IReadOnlyList<(double T, double V)>> ForceProfiles(
        ForceSet set, BarForceComponent component, ForceRowAggregate aggregate)
    {
        ArgumentNullException.ThrowIfNull(set);
        int c = (int)component;
        var result = new Dictionary<int, IReadOnlyList<(double T, double V)>>();
        foreach (var entries in set.Envelope(shell: false).ByElement())
        {
            var sections = Sections(entries, c)
                .Select(s => (s.T, V: ForceSetEnvelope.Entry.Pick(s.Min, s.Max, aggregate)!.Value))
                .ToList();
            if (sections.Count == 1) result[entries.Key] = [(0, sections[0].V), (1, sections[0].V)];
            else if (sections.Count > 1) result[entries.Key] = sections;
        }
        return result;
    }

    /// <summary>
    /// Профиль подобранной арматуры КЭ для эпюры на схеме: ступени (две точки на ступень), как в
    /// <see cref="Rebar"/>; сечения с отказом подбора пропущены.
    /// </summary>
    public static IReadOnlyList<(double T, double V)> RebarProfile(IReadOnlyList<PlateRebar.PlateRebarValue> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        var result = new List<(double, double)>();
        int n = sections.Count;
        for (int k = 0; k < n; k++)
        {
            if (sections[k].Value is not double value) continue;
            var (t0, t1) = StepBounds(k, n);
            result.Add((t0, value));
            result.Add((t1, value));
        }
        return result;
    }

    /// <summary>
    /// Эпюра коэффициента использования по сечениям КЭ из строк результата проверки по КЭ: ступенчатая,
    /// как у арматуры (каждое сечение проверено отдельно), значение сечения — наибольший Кисп его строк.
    /// Сечение без проверенных строк в эпюру не входит, но остаётся в <see cref="BarDiagramSeries.Points"/>
    /// с пустым значением. Так же — сечение со строкой, не прошедшей без коэффициента (НДС не найден):
    /// наибольший Кисп остальных строк его не характеризует; у точки <see cref="BarDiagramPoint.Failed"/>.
    /// </summary>
    /// <param name="rebarSource">Источник армирования, строки которого берутся.</param>
    public static BarDiagramSeries Utilization(BarChain chain, IEnumerable<FemCheckRowResult> rows, string rebarSource)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(rows);
        var byElem = rows.Where(r => r.RebarSource == rebarSource).ToLookup(r => r.ElemNum);
        var upper = new List<BarDiagramSegment>();
        var points = new List<BarDiagramPoint>();
        foreach (var e in chain.Elements)
        {
            var sections = UtilizationSections(byElem[e.ElemNum]);
            foreach (var (num, k, n, value, failed) in sections)
            {
                double t = n > 1 ? k / (double)(n - 1) : 0.5;
                points.Add(new BarDiagramPoint(e.ElemNum, num, e.At(t), value, value, Failed: failed));
                if (value is not double v) continue;
                var (t0, t1) = StepBounds(k, n);
                upper.Add(Ordered(e.At(t0), e.At(t1), v, v));
            }
        }
        return new BarDiagramSeries(Sort(upper), [], points.OrderBy(p => p.S).ToList());
    }

    /// <summary>
    /// Профили коэффициента использования по КЭ для эпюр на схеме: ступени, как в <see cref="Utilization"/>.
    /// </summary>
    public static Dictionary<int, IReadOnlyList<(double T, double V)>> UtilizationProfiles(
        IEnumerable<FemCheckRowResult> rows, string rebarSource)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var result = new Dictionary<int, IReadOnlyList<(double T, double V)>>();
        foreach (var g in rows.Where(r => r.RebarSource == rebarSource).GroupBy(r => r.ElemNum))
        {
            var profile = new List<(double, double)>();
            foreach (var (_, k, n, value, _) in UtilizationSections(g))
            {
                if (value is not double v) continue;
                var (t0, t1) = StepBounds(k, n);
                profile.Add((t0, v));
                profile.Add((t1, v));
            }
            if (profile.Count > 0) result[g.Key] = profile;
        }
        return result;
    }

    /// <summary>
    /// Сечения КЭ по строкам результата: номер, индекс с нуля, число сечений, наибольший Кисп проверенных
    /// строк (null — проверенных нет или сечение не прошло без коэффициента) и признак такого отказа.
    /// Строки без номера сечения — одно сечение на весь КЭ.
    /// </summary>
    public static List<(int? Num, int K, int N, double? Value, bool Failed)> UtilizationSections(
        IEnumerable<FemCheckRowResult> elementRows)
    {
        var rows = elementRows.ToList();
        if (rows.Count == 0) return [];
        bool numbered = rows.All(r => r.SectionNum is >= 1);
        int count = numbered ? rows.Max(r => r.SectionNum!.Value) : 1;
        return rows
            .GroupBy(r => numbered ? r.SectionNum!.Value : 1)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var values = g.Where(r => !r.NotChecked && r.Utilization is double u && double.IsFinite(u))
                    .Select(r => r.Utilization!.Value).ToList();
                // Строка не прошла, а коэффициента нет (НДС не найден) — сечение не проходит при любом Кисп остальных.
                bool failed = g.Any(r => !r.NotChecked && !r.Passed && !(r.Utilization is double u && double.IsFinite(u)));
                return (numbered ? g.Key : (int?)null, g.Key - 1, count,
                    failed || values.Count == 0 ? (double?)null : values.Max(), failed);
            })
            .ToList();
    }

    /// <summary>Границы ступени сечения <paramref name="k"/> (с нуля) из <paramref name="n"/> в долях длины КЭ.</summary>
    static (double T0, double T1) StepBounds(int k, int n) =>
        n > 1 ? (Math.Max(0, (k - 0.5) / (n - 1)), Math.Min(1, (k + 0.5) / (n - 1))) : (0, 1);

    /// <summary>
    /// Сечения КЭ по огибающей его строк усилий: номер, положение в долях длины, наибольшее и наименьшее
    /// значение канала <paramref name="channel"/>. Если хоть у одной строки КЭ нет номера сечения — одно
    /// «сечение» посередине КЭ (номер null) по всем строкам.
    /// </summary>
    static List<(int? Num, double T, double Max, double Min)> Sections(IEnumerable<ForceSetEnvelope.Entry> elementEntries, int channel)
    {
        var entries = elementEntries.ToList();
        if (entries.Count == 0) return [];
        bool numbered = entries.All(e => e.Section is >= 1);
        int count = numbered ? entries.Max(e => e.Section!.Value) : 1;
        return entries
            .GroupBy(e => numbered ? e.Section!.Value : 1)
            .OrderBy(g => g.Key)
            .Select(g => (
                Num: numbered ? g.Key : (int?)null,
                T: count > 1 ? (g.Key - 1) / (double)(count - 1) : 0.5,
                Max: g.Select(e => e.Max[channel]).Where(v => !double.IsNaN(v)).DefaultIfEmpty(double.NaN).Max(),
                Min: g.Select(e => e.Min[channel]).Where(v => !double.IsNaN(v)).DefaultIfEmpty(double.NaN).Min()))
            .Where(x => !double.IsNaN(x.Max))
            .ToList();
    }

    static BarDiagramSegment Ordered(double s0, double s1, double v0, double v1) =>
        s0 <= s1 ? new BarDiagramSegment(s0, s1, v0, v1) : new BarDiagramSegment(s1, s0, v1, v0);

    static List<BarDiagramSegment> Sort(List<BarDiagramSegment> segments) => segments.OrderBy(s => s.S0).ToList();
}
