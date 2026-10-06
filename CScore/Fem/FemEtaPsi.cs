namespace CScore.Fem;

/// <summary>
/// Доля длительной нагрузки ψ = M1l/M1 для φl = 1 + ψ (п. 8.1.15 СП 63) по строкам полного и длительного
/// наборов усилий. M1, M1l — моменты относительно наиболее растянутого (наименее сжатого) стержня арматуры:
/// M1 = M − N·ys, ys — координата крайнего стержня на стороне, которую растягивает момент полной нагрузки
/// (конвенция OpenCS: положительный Mx растягивает грань y &gt; 0, My — грань x &gt; 0).
/// </summary>
public static class FemEtaPsi
{
    const double Epsilon = 1e-9;

    /// <summary>Пары «полный — длительный» по метке типа расчёта в теге набора.</summary>
    static readonly (string Total, string Long)[] Pairs = [("(C)", "(CL)"), ("(N)", "(NL)")];

    /// <summary>
    /// ψ одной плоскости изгиба, клэмп [0; 1]. Момент полной нагрузки ≈ 0 и N ≈ 0 — 1 (запас).
    /// </summary>
    /// <param name="nTotal">N полной нагрузки, кН (сжатие — отрицательное).</param>
    /// <param name="mTotal">M полной нагрузки в этой плоскости, кН·м.</param>
    /// <param name="nLong">N постоянной и длительной нагрузки, кН.</param>
    /// <param name="mLong">M постоянной и длительной нагрузки, кН·м.</param>
    /// <param name="rebarMin">Наименьшая координата стержней арматуры поперёк оси изгиба, м.</param>
    /// <param name="rebarMax">Наибольшая координата стержней, м.</param>
    public static double Compute(double nTotal, double mTotal, double nLong, double mLong,
                                 double rebarMin, double rebarMax)
    {
        // Сторона растяжения — по моменту полной нагрузки; без момента — любая (стержни симметричны по смыслу).
        double ys = mTotal >= 0 ? rebarMax : rebarMin;
        double m1 = mTotal - nTotal * ys;
        double m1l = mLong - nLong * ys;
        if (Math.Abs(m1) < Epsilon) return 1.0;
        return Math.Clamp(Math.Abs(m1l) / Math.Abs(m1), 0.0, 1.0);
    }

    /// <summary>
    /// Крайние координаты стержней арматуры сечения (x — для My, y — для Mx); без стержней — габарит,
    /// без геометрии — нули.
    /// </summary>
    public static (double MinX, double MaxX, double MinY, double MaxY) RebarExtents(CrossSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        foreach (var area in section.Areas)
            foreach (var f in area.Fibers)
            {
                if (f.TypeFiber != FiberType.point) continue;
                minX = Math.Min(minX, f.X); maxX = Math.Max(maxX, f.X);
                minY = Math.Min(minY, f.Y); maxY = Math.Max(maxY, f.Y);
            }
        if (double.IsFinite(minX)) return (minX, maxX, minY, maxY);
        // Без геометрии — моменты относительно начала координат сечения (ошибку сечения покажет сама проверка).
        if (!section.Areas.Any(a => a.Hull != null)) return (0, 0, 0, 0);
        var (bx0, bx1, by0, by1) = section.SectionBoundingBox();
        return (bx0, bx1, by0, by1);
    }

    /// <summary>
    /// Парный длительный набор: «… (C)» → «… (CL)», «… (N)» → «… (NL)» (пробел перед меткой не важен);
    /// null — набор не полный (длительный или без метки типа расчёта) либо пары нет.
    /// </summary>
    public static ForceSet? LongTermSet(ForceSet set, IEnumerable<ForceSet> sets)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(sets);
        string tag = set.Tag ?? "";
        foreach (var (total, lng) in Pairs)
        {
            if (!tag.EndsWith(total, StringComparison.Ordinal)) continue;
            string stem = tag[..^total.Length].TrimEnd();
            return sets.FirstOrDefault(s => s.Tag is { } t && t.EndsWith(lng, StringComparison.Ordinal)
                                            && t[..^lng.Length].TrimEnd() == stem);
        }
        return null;
    }

    /// <summary>Набор сам длительный (CL/NL): ψ = 1.</summary>
    public static bool IsLongTerm(CalcType calcType) => calcType is CalcType.CL or CalcType.NL;
}
