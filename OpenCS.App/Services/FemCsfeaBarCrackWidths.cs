using CScore;
using CScore.Sp63.CrackWidth;
using CScore.Sp63.Normal;
using CSfea.CScoreBridge.Structural;

namespace OpenCS.Services;

/// <summary>
/// ЖБ-стержень расчётной модели для ширины раскрытия трещин: сечение CScore постановки и оси сечения в осях КЭ
/// (<see cref="RcBeamSection.CrossYAlongLocalY"/>).
/// </summary>
public sealed record FemCsfeaBarCrackElement(int Id, CrossSection Section, bool YAlongLocalY);

/// <summary>
/// Ширина раскрытия трещин стержня в точках ξ = 0, ½, 1, мм: продолжительное a_crc,1 и непродолжительное a_crc —
/// наибольшие по плоскостям изгиба; null в точке — проверка неприменима (нет изгиба, форма или армирование вне
/// упрощённой проверки).
/// </summary>
public readonly record struct FemCsfeaBarCrackWidth(double?[] Long, double?[] Short)
{
    /// <summary>Наибольшее по точкам КЭ (мозаика стержней); null — ни в одной точке нет значения.</summary>
    public static double? Max(double?[] values) => values.Where(v => v != null).Select(v => v!.Value).DefaultIfEmpty(double.NaN).Max()
        is double m && !double.IsNaN(m) ? m : null;
}

/// <summary>
/// Ширина раскрытия трещин стержней по усилиям шага секущего расчёта (спека НДС сечений, п. 2.5): упрощённая проверка
/// стержней (<see cref="Sp63CrackWidthChecker"/>, пп. 8.2.15, 8.2.16 СП 63; прямоугольник и тавр/двутавр с двумя рядами
/// арматуры) в точках i, ½, j. Упрощённая проверка одноосная — двухосный изгиб считается по каждой плоскости отдельно
/// (N с My, N с Mz сечения) и берётся наибольшее. Длительная часть — доля от полных или усилия пары NL варианта пакета;
/// a_crc,1 — по (8.119), a_crc — по (8.120) (п. 8.2.7). Характеристики материалов — вида расчёта N, как у пластин
/// (<see cref="FemCsfeaCrackWidths"/>). Усилия в точках — по усилиям концов КЭ (в середине — среднее, без пролётной
/// нагрузки), как в окне НДС сечения (<see cref="FemCsfeaBarSectionState.ElementForces"/>).
/// </summary>
public static class FemCsfeaBarCrackWidths
{
    /// <summary>ЖБ-стержни адаптированной модели: сечение CScore с бетоном (характеристики N) и арматурой.</summary>
    public static List<FemCsfeaBarCrackElement> Elements(FemRcModelResult adapted)
    {
        ArgumentNullException.ThrowIfNull(adapted);
        var result = new List<FemCsfeaBarCrackElement>();
        foreach (var beam in adapted.Model.Beams)
        {
            if (beam.Section.Cross is not { } cross) continue;
            bool concrete = cross.Areas.Any(a => a.Category == AreaCategory.Region && a.Material?.Type == MatType.Concrete
                && a.Material.GetChars(CalcType.N) != null);
            bool rebar = cross.Areas.Any(a => a.Category == AreaCategory.RebarGroup && a.Material?.GetChars(CalcType.N) != null);
            if (concrete && rebar) result.Add(new FemCsfeaBarCrackElement(beam.Id, cross, beam.Section.CrossYAlongLocalY));
        }
        return result;
    }

    /// <summary>
    /// Ширина раскрытия в одной точке по усилиям КЭ (Н, Н·м: N, My, Mz в осях КЭ); <paramref name="longShare"/> — доля
    /// длительной части 0…1 либо <paramref name="longForces"/> — длительная часть своим расчётом.
    /// </summary>
    public static (double? Long, double? Short) Compute(FemCsfeaBarCrackElement e, (double N, double My, double Mz) forces,
        double longShare, (double N, double My, double Mz)? longForces = null)
    {
        var (n, mx, my) = FemCsfeaBarSectionState.ToSection(forces, e.YAlongLocalY);
        var l = longForces is { } lf ? FemCsfeaBarSectionState.ToSection(lf, e.YAlongLocalY) : ((double, double, double)?)null;
        double? aLong = null, aShort = null;
        foreach (var axis in new[] { Sp63NormalAxis.Mx, Sp63NormalAxis.My })
        {
            bool alongX = axis == Sp63NormalAxis.Mx;
            if (Math.Abs(alongX ? mx : my) <= 1e-6) continue;
            var load = new LoadItem { N = n, Mx = alongX ? mx : 0, My = alongX ? 0 : my };
            LoadItem? longLoad = l is { } t ? new LoadItem { N = t.Item1, Mx = alongX ? t.Item2 : 0, My = alongX ? 0 : t.Item3 } : null;
            var r = Check(e.Section, load, axis, longShare, longLoad);
            if (r == null) continue;
            aLong = Math.Max(aLong ?? 0, r.Value.Long);
            aShort = Math.Max(aShort ?? 0, r.Value.Short);
        }
        return (aLong, aShort);
    }

    static (double Long, double Short)? Check(CrossSection section, LoadItem load, Sp63NormalAxis axis, double share, LoadItem? longLoad)
    {
        foreach (var shape in new[] { Sp63NormalShapeKind.Rectangular, Sp63NormalShapeKind.Tee })
        {
            var options = new Sp63CrackWidthOptions(shape, axis, Phi1: 1.0, Phi2: 0.5, AcrcLimMm: 0.3,
                Mode: Sp63CrackWidthMode.LongAndShort, LongTermShare: Math.Clamp(share, 0, 1));
            var r = Sp63CrackWidthChecker.Check(section, load, CalcType.N, options, longLoad);
            if (r.Status != Sp63CrackWidthStatus.Calculated) continue;
            double aLong = r.Variables.GetValueOrDefault("acrc1");
            double aShort = r.Details.Count > 1 ? r.Details[1].Applied : aLong;
            if (!double.IsFinite(aLong) || !double.IsFinite(aShort)) return null;
            return (Math.Max(0, aLong), Math.Max(0, aShort));
        }
        return null;
    }

    /// <summary>Ширина раскрытия всех ЖБ-стержней шага в точках i, ½, j при доле длительной части (параллельно).</summary>
    public static Dictionary<int, FemCsfeaBarCrackWidth> Compute(IReadOnlyList<FemCsfeaBarCrackElement> elements,
        RcSecantStepFields fields, double longShare, CancellationToken ct = default) =>
        ComputeAll(elements, fields, ct, (e, i, p) => Compute(e, FemCsfeaBarSectionState.ElementForces(fields, i, p), longShare));

    /// <summary>
    /// Ширина раскрытия всех ЖБ-стержней по полям полной нагрузки и её длительной части (пара NL варианта пакета).
    /// </summary>
    public static Dictionary<int, FemCsfeaBarCrackWidth> Compute(IReadOnlyList<FemCsfeaBarCrackElement> elements,
        RcSecantStepFields fields, RcSecantStepFields longFields, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(longFields);
        if (!longFields.BeamIds.AsSpan().SequenceEqual(fields.BeamIds))
            throw new InvalidOperationException("Поля полной нагрузки и длительной части записаны на разных сетках.");
        return ComputeAll(elements, fields, ct, (e, i, p) => Compute(e, FemCsfeaBarSectionState.ElementForces(fields, i, p), 1.0,
            FemCsfeaBarSectionState.ElementForces(longFields, i, p)));
    }

    static Dictionary<int, FemCsfeaBarCrackWidth> ComputeAll(IReadOnlyList<FemCsfeaBarCrackElement> elements, RcSecantStepFields fields,
        CancellationToken ct, Func<FemCsfeaBarCrackElement, int, int, (double? Long, double? Short)> compute)
    {
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(fields);
        var index = new Dictionary<int, int>(fields.BeamIds.Length);
        for (int i = 0; i < fields.BeamIds.Length; i++) index.TryAdd(fields.BeamIds[i], i);
        var values = new FemCsfeaBarCrackWidth?[elements.Count];
        Parallel.For(0, elements.Count, new ParallelOptions { CancellationToken = ct }, k =>
        {
            if (!index.TryGetValue(elements[k].Id, out int i)) return;
            var aLong = new double?[3];
            var aShort = new double?[3];
            for (int p = 0; p < 3; p++)
            {
                try { (aLong[p], aShort[p]) = compute(elements[k], i, p); }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException) { }
            }
            if (aLong.Any(v => v != null)) values[k] = new FemCsfeaBarCrackWidth(aLong, aShort);
        });
        var result = new Dictionary<int, FemCsfeaBarCrackWidth>(elements.Count);
        for (int k = 0; k < elements.Count; k++)
            if (values[k] is { } v) result[elements[k].Id] = v;
        return result;
    }
}
