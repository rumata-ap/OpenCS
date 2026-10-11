namespace CScore.Fem;

/// <summary>
/// Итог оценки доли длительной части усилий схемы (<see cref="FemLongTermShare"/>).
/// </summary>
/// <param name="Share">Осреднённая доля, 0…1.</param>
/// <param name="Groups">Групп «КЭ — сечение», вошедших в осреднение.</param>
/// <param name="Rows">Строк полного набора в этих группах.</param>
/// <param name="GroupsWithoutLong">Групп полного набора без строк длительного (в осреднение не входят).</param>
/// <param name="GroupsSkipped">Групп без данных для метрики (нет толщины, радиусов инерции) или с нулевыми усилиями.</param>
/// <param name="MinGroupShare">Наименьшая доля группы.</param>
/// <param name="MaxGroupShare">Наибольшая доля группы.</param>
public sealed record FemLongTermShareResult(double Share, int Groups, int Rows, int GroupsWithoutLong, int GroupsSkipped,
    double MinGroupShare, double MaxGroupShare);

/// <summary>
/// Доля длительной части усилий схемы одним числом по парам наборов «полный — длительный» (N — NL, C — CL): РСН и РСУ.
/// <para>
/// Строки сопоставляются не по номерам и не по меткам, а по группам «КЭ — сечение»: у РСУ число строк КЭ в полном и
/// длительном наборах может не совпадать, а строка одного критерия в двух наборах может принадлежать разным сочетаниям.
/// </para>
/// <para>
/// <b>Доля строки.</b> Длительная часть сочетания строки F неизвестна, но принадлежит множеству длительных сочетаний,
/// огибающую которого дают строки G длительного набора того же КЭ. Доля — коэффициент наилучшего пропорционального
/// приближения длительной части полной, s·F: s = ⟨F, L⟩/⟨F, F⟩. Для любой длительной части L верно
/// ⟨F, L⟩ ≤ max ⟨F, L′⟩ по всем длительным сочетаниям L′; этот максимум оценивается по строкам длительного набора:
/// s = max_j ⟨F, G_j⟩/⟨F, F⟩, 0 ≤ s ≤ 1. Строки РСУ — экстремумы по критериям РСУ, а не по ⟨F, ·⟩, поэтому оценка
/// сверху строгая лишь настолько, насколько критерии покрывают направление F. У РСН в группе по одной строке в
/// каждом наборе — это точная проекция пары. Оценка сверху — в запас: ширина раскрытия по п. 8.2.7 СП 63 растёт с
/// долей.
/// </para>
/// <para>
/// <b>Скалярное произведение</b> — дополнительная упругая энергия (ν = 0), не зависит от поворота осей в плоскости
/// КЭ и сводит силы и моменты к одной размерности: пластина ⟨F, G⟩ = N:N′ + (12/h²)·M:M′, где
/// N:N′ = Nx·Nx′ + Ny·Ny′ + 2·Nxy·Nxy′; стержень ⟨F, G⟩ = N·N′ + Mx·Mx′/ix² + My·My′/iy².
/// </para>
/// <para>
/// <b>Осреднение.</b> Одно число s̄ минимизирует взвешенную по энергии ошибку Σ w·‖s̄·F − s·F‖², откуда
/// s̄ = Σ w·⟨F, F⟩·s / Σ w·⟨F, F⟩. Вес группы w (у пластины — площадь/толщина, у стержня — длина/площадь сечения; не
/// задан — 1) делится на число строк группы, чтобы КЭ с большим числом строк РСУ не получал лишнего веса.
/// </para>
/// </summary>
public static class FemLongTermShare
{
    /// <summary>Группа строк одного КЭ и сечения: вектора усилий в метрике ‖v‖² = ⟨F, F⟩ и вес группы.</summary>
    public sealed record Group(double Weight, IReadOnlyList<double[]> Total, IReadOnlyList<double[]> Long);

    /// <summary>Доля строки F по строкам длительного набора; null — усилия строки нулевые.</summary>
    public static double? RowShare(double[] total, IReadOnlyList<double[]> longRows)
    {
        double ff = Dot(total, total);
        if (!(ff > 0) || longRows.Count == 0) return null;
        double best = double.NegativeInfinity;
        foreach (var g in longRows) best = Math.Max(best, Dot(total, g));
        return Math.Clamp(best / ff, 0.0, 1.0);
    }

    /// <summary>Осреднение по группам; null — нет ни одной группы с ненулевыми усилиями и строками длительного набора.</summary>
    public static FemLongTermShareResult? Compute(IEnumerable<Group> groups, int skipped = 0)
    {
        ArgumentNullException.ThrowIfNull(groups);
        double sum = 0, weight = 0, min = double.PositiveInfinity, max = double.NegativeInfinity;
        int used = 0, rows = 0, withoutLong = 0;
        foreach (var g in groups)
        {
            if (g.Total.Count == 0) continue;
            if (g.Long.Count == 0) { withoutLong++; continue; }
            double gs = 0, gw = 0;
            foreach (var f in g.Total)
            {
                if (RowShare(f, g.Long) is not double s) continue;
                double w = Dot(f, f);
                gs += w * s;
                gw += w;
            }
            if (!(gw > 0) || !(g.Weight > 0)) { skipped++; continue; }
            double k = g.Weight / g.Total.Count;
            sum += k * gs;
            weight += k * gw;
            double share = gs / gw;
            min = Math.Min(min, share);
            max = Math.Max(max, share);
            used++;
            rows += g.Total.Count;
        }
        return weight > 0 ? new FemLongTermShareResult(sum / weight, used, rows, withoutLong, skipped, min, max) : null;
    }

    /// <summary>
    /// Доля по пластинчатым наборам: пары «строки полного набора — строки длительного»; <paramref name="thickness"/> —
    /// толщина пластины по номеру КЭ, м (null — КЭ пропускается); <paramref name="weight"/> — вес КЭ (площадь/толщина).
    /// Строки без номера КЭ группируются по метке и считаются при толщине <paramref name="defaultThickness"/>.
    /// </summary>
    public static FemLongTermShareResult? Shells(
        IEnumerable<(IReadOnlyList<ShellLoadItem> Total, IReadOnlyList<ShellLoadItem> Long)> pairs,
        Func<int, double?> thickness, Func<int, double>? weight = null, double? defaultThickness = null)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentNullException.ThrowIfNull(thickness);
        var groups = new List<Group>();
        int skipped = 0;
        foreach (var (total, lng) in pairs)
        {
            var longByKey = lng.GroupBy(Key).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var g in total.GroupBy(Key))
            {
                double? h = g.Key.Element is int e ? thickness(e) : defaultThickness;
                if (h is not > 0) { skipped++; continue; }
                double w = g.Key.Element is int we && weight != null ? weight(we) : 1.0;
                var longRows = longByKey.GetValueOrDefault(g.Key) ?? [];
                groups.Add(new Group(w, g.Select(r => Vector(r, h.Value)).ToList(), longRows.Select(r => Vector(r, h.Value)).ToList()));
            }
        }
        return Compute(groups, skipped);

        static (int? Element, int? Section, string? Label) Key(ShellLoadItem r) =>
            (r.SourceElementNum, r.SourceSectionNum, r.SourceElementNum == null ? r.Label : null);
    }

    /// <summary>
    /// Доля по стержневым наборам; <paramref name="radii"/> — радиусы инерции сечения по номеру КЭ, м: ix — в плоскости
    /// момента Mx (по оси y сечения), iy — в плоскости My (null — КЭ пропускается); <paramref name="weight"/> — вес КЭ
    /// (длина/площадь сечения). Строки без номера КЭ группируются по метке и считаются при <paramref name="defaultRadii"/>.
    /// </summary>
    public static FemLongTermShareResult? Bars(
        IEnumerable<(IReadOnlyList<LoadItem> Total, IReadOnlyList<LoadItem> Long)> pairs,
        Func<int, (double Ix, double Iy)?> radii, Func<int, double>? weight = null, (double Ix, double Iy)? defaultRadii = null)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentNullException.ThrowIfNull(radii);
        var groups = new List<Group>();
        int skipped = 0;
        foreach (var (total, lng) in pairs)
        {
            var longByKey = lng.GroupBy(Key).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var g in total.GroupBy(Key))
            {
                var r = g.Key.Element is int e ? radii(e) : defaultRadii;
                if (r is not { Ix: > 0, Iy: > 0 } i) { skipped++; continue; }
                double w = g.Key.Element is int we && weight != null ? weight(we) : 1.0;
                var longRows = longByKey.GetValueOrDefault(g.Key) ?? [];
                groups.Add(new Group(w, g.Select(x => Vector(x, i.Ix, i.Iy)).ToList(), longRows.Select(x => Vector(x, i.Ix, i.Iy)).ToList()));
            }
        }
        return Compute(groups, skipped);

        static (int? Element, int? Section, string? Label) Key(LoadItem r) =>
            (r.SourceElementNum, r.SourceSectionNum, r.SourceElementNum == null ? r.Label : null);
    }

    /// <summary>Вектор усилий пластины в энергетической метрике: ‖v‖² = N:N + (12/h²)·M:M.</summary>
    public static double[] Vector(ShellLoadItem r, double h)
    {
        ArgumentNullException.ThrowIfNull(r);
        var (nx, ny, nxy) = r.ResolveN(h);
        double c = Math.Sqrt(12.0) / h, q = Math.Sqrt(2.0);
        return [nx, ny, q * nxy, c * r.Mx, c * r.My, q * c * r.Mxy];
    }

    /// <summary>Вектор усилий стержня в энергетической метрике: ‖v‖² = N² + Mx²/ix² + My²/iy².</summary>
    public static double[] Vector(LoadItem r, double ix, double iy)
    {
        ArgumentNullException.ThrowIfNull(r);
        return [r.N, r.Mx / ix, r.My / iy];
    }

    static double Dot(double[] a, double[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }
}
