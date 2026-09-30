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
public sealed record BarDiagramPoint(int ElemNum, int? SectionNum, double S, double? Max, double? Min, int? FailureCode = null);

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
        var rowsByElem = set.Items.Where(i => i.SourceElementNum != null).ToLookup(i => i.SourceElementNum!.Value);

        var upper = new List<BarDiagramSegment>();
        var lower = new List<BarDiagramSegment>();
        var points = new List<BarDiagramPoint>();
        bool envelope = false;
        foreach (var e in chain.Elements)
        {
            var sections = Sections(rowsByElem[e.ElemNum], component)
                .Select(x => (x.Num, x.T, Max: x.Values.Max(), Min: x.Values.Min()))
                .ToList();
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
        var result = new Dictionary<int, IReadOnlyList<(double T, double V)>>();
        foreach (var rows in set.Items.Where(i => i.SourceElementNum != null).GroupBy(i => i.SourceElementNum!.Value))
        {
            var sections = Sections(rows, component)
                .Select(s => (s.T, V: aggregate switch
                {
                    ForceRowAggregate.Max => s.Values.Max(),
                    ForceRowAggregate.Min => s.Values.Min(),
                    _ => s.Values.MaxBy(Math.Abs),
                }))
                .ToList();
            if (sections.Count == 1) result[rows.Key] = [(0, sections[0].V), (1, sections[0].V)];
            else if (sections.Count > 1) result[rows.Key] = sections;
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

    /// <summary>Границы ступени сечения <paramref name="k"/> (с нуля) из <paramref name="n"/> в долях длины КЭ.</summary>
    static (double T0, double T1) StepBounds(int k, int n) =>
        n > 1 ? (Math.Max(0, (k - 0.5) / (n - 1)), Math.Min(1, (k + 0.5) / (n - 1))) : (0, 1);

    /// <summary>
    /// Сечения КЭ по его строкам усилий: номер, положение в долях длины и значения компоненты всех строк
    /// сечения. Строки без номера сечения — одно «сечение» посередине КЭ (номер null).
    /// </summary>
    static List<(int? Num, double T, List<double> Values)> Sections(IEnumerable<LoadItem> elementRows, BarForceComponent component)
    {
        var rows = elementRows.ToList();
        if (rows.Count == 0) return [];
        bool numbered = rows.All(r => r.SourceSectionNum is >= 1);
        int count = numbered ? rows.Max(r => r.SourceSectionNum!.Value) : 1;
        return rows
            .GroupBy(r => numbered ? r.SourceSectionNum!.Value : 1)
            .OrderBy(g => g.Key)
            .Select(g => (
                Num: numbered ? g.Key : (int?)null,
                T: count > 1 ? (g.Key - 1) / (double)(count - 1) : 0.5,
                Values: g.Select(r => ElementForceField.BarValue(r, component)).Where(double.IsFinite).ToList()))
            .Where(x => x.Values.Count > 0)
            .ToList();
    }

    static BarDiagramSegment Ordered(double s0, double s1, double v0, double v1) =>
        s0 <= s1 ? new BarDiagramSegment(s0, s1, v0, v1) : new BarDiagramSegment(s1, s0, v1, v0);

    static List<BarDiagramSegment> Sort(List<BarDiagramSegment> segments) => segments.OrderBy(s => s.S0).ToList();
}
