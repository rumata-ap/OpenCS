using CScore;
using CScore.Sp63.CrackWidth;
using CScore.Sp63.Normal;
using CSfea.CScoreBridge;
using CSfea.CScoreBridge.Structural;

namespace OpenCS.Services;

/// <summary>
/// ЖБ-стержень расчётной модели для ширины раскрытия трещин: сечение CScore постановки и оси сечения в осях КЭ
/// (<see cref="RcBeamSection.CrossYAlongLocalY"/>).
/// </summary>
public sealed record FemCsfeaBarCrackElement(int Id, CrossSection Section, bool YAlongLocalY);

/// <summary>
/// Ширина раскрытия трещин стержня в точках ξ = 0, ½, 1, мм: продолжительное a_crc,1 и непродолжительное a_crc —
/// наибольшие по растянутым стержням; null в точке — значения нет (упрощённая проверка неприменима).
/// </summary>
public readonly record struct FemCsfeaBarCrackWidth(double?[] Long, double?[] Short)
{
    /// <summary>Наибольшее по точкам КЭ (мозаика стержней); null — ни в одной точке нет значения.</summary>
    public static double? Max(double?[] values) => values.Where(v => v != null).Select(v => v!.Value).DefaultIfEmpty(double.NaN).Max()
        is double m && !double.IsNaN(m) ? m : null;
}

/// <summary>
/// Ширина раскрытия трещин стержней по результату секущего расчёта CSfea (спека НДС сечений, п. 2.5).
/// <para>
/// Основной путь — по записанной плоскости деформаций точки (<see cref="RcSecantStepFields.HasBeamStrains"/>): у каждого
/// растянутого стержня a = φ1·φ2·φ3·εs,m·ls (ф. (8.128) СП 63: ψs·σs/Es = εs,m). С ψs в расчёте плоскость — средних
/// деформаций (п. 8.2.32 и (8.138) дают одно и то же εs,m = εs − 0,8·εs,crc); без ψs — деформация в трещине, ψs = 1 (в
/// запас, п. 8.2.15). ls — по п. 8.2.17: Abt — бетон у растянутой грани по нормали к нейтральной оси показанной
/// плоскости, высотой в пределах 2a…0,5h по этому направлению; As и ds — растянутые стержни с весом εi/εmax (как в
/// <see cref="CrackWidthSolver"/>). Трещина в точке — по плоскости (<see cref="SecantCrossSectionBuilder.IsCracked"/>)
/// или по флагу трещины КЭ. a_crc,1 = a(длит., φ1 = 1,4), a_crc = a_crc,1 + a(полн., φ1 = 1,0) − a(длит., φ1 = 1,0) —
/// по каждому стержню, затем наибольшее (п. 8.2.7). Длительная часть: пара NL варианта пакета — её записанная
/// плоскость; доля ψ — сечение досчитывается по НДМ на ψ·усилиях точки (бетон без растяжения, ψs с εs,crc при
/// соотношении полных усилий, как в окне НДС сечения) — допущение: память трещин от полной нагрузки.
/// </para>
/// <para>
/// Запасной путь (записи до 18.10.2026 без деформаций стержней, комбинации линейного расчёта) — упрощённая проверка
/// стержней по усилиям (<see cref="Sp63CrackWidthChecker"/>, пп. 8.2.15, 8.2.16; прямоугольник и тавр, двухосный изгиб —
/// по плоскостям, наибольшее). Характеристики материалов упрощённой проверки — вида N; усилия в точках — по усилиям
/// концов КЭ (<see cref="FemCsfeaBarSectionState.ElementForces"/>).
/// </para>
/// </summary>
public static class FemCsfeaBarCrackWidths
{
    const double Phi2 = 0.5;

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

    // ------------------------------------------------------------ по записанной плоскости деформаций

    /// <summary>Состояние точки для ширины раскрытия: деформации растянутых стержней (средние при ψs) и ls, м; трещины нет — пусто.</summary>
    sealed record PlaneState(IReadOnlyDictionary<Fiber, double> Eps, double Ls)
    {
        public static readonly PlaneState None = new(new Dictionary<Fiber, double>(), 0.0);
    }

    /// <summary>
    /// Растянутые стержни и ls по плоскости <paramref name="k"/> сечения; трещины нет (по плоскости и флагу) или нет
    /// растянутой арматуры — <see cref="PlaneState.None"/>.
    /// </summary>
    static PlaneState State(CrossSection s, Kurvature k, CalcType calc, bool elementCracked)
    {
        if (!elementCracked && !SecantCrossSectionBuilder.IsCracked(s, k, calc)) return PlaneState.None;
        var eps = TensileRebar(s, k);
        if (eps.Count == 0) return PlaneState.None;
        double ls = CrackSpacing(s, k, eps, eps.Values.Max());
        return ls > 0 ? new PlaneState(eps, ls) : PlaneState.None;
    }

    /// <summary>Деформации растянутых точечных стержней по плоскости <paramref name="k"/>.</summary>
    static Dictionary<Fiber, double> TensileRebar(CrossSection s, Kurvature k)
    {
        var eps = new Dictionary<Fiber, double>(ReferenceEqualityComparer.Instance);
        foreach (var area in s.Areas.Where(a => a.Material?.Type is MatType.ReSteelF or MatType.ReSteelU))
            foreach (var f in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
                if (k.e0 + k.ky * f.Y + k.kz * f.X is double e && e > 0) eps[f] = e;
        return eps;
    }

    /// <summary>ls, м, по плоскости <paramref name="k"/> (см. закрытую перегрузку); нет растянутой арматуры — 0.</summary>
    public static double CrackSpacing(CrossSection s, Kurvature k)
    {
        var eps = TensileRebar(s, k);
        return eps.Count == 0 ? 0 : CrackSpacing(s, k, eps, eps.Values.Max());
    }

    /// <summary>
    /// Базовое расстояние между трещинами ls, м (п. 8.2.17, ф. (8.136)): ls = 0,5·Abt/As·ds в пределах 10ds…40ds и
    /// 100…400 мм. Направление — нормаль n к нейтральной оси в сторону роста ε (ε = e0 + ky·Y + kz·X); высота
    /// растянутого бетона — от крайней по n точки бетона до нуля деформаций, не меньше 2a (a — от грани до центра
    /// тяжести растянутой арматуры по n) и не больше 0,5h (h — размер бетона по n). Равномерное растяжение — весь бетон.
    /// </summary>
    static double CrackSpacing(CrossSection s, Kurvature k, Dictionary<Fiber, double> eps, double epsMax)
    {
        double asW = 0, asDW = 0, asPlain = 0, xPlain = 0, yPlain = 0;
        foreach (var (f, e) in eps)
        {
            double w = e / epsMax;
            asW += f.Area * w;
            asDW += f.Area * w * f.Diameter;
            asPlain += f.Area;
            xPlain += f.Area * f.X;
            yPlain += f.Area * f.Y;
        }
        if (asW < 1e-15) return 0;
        double ds = asDW / asW;

        var hulls = s.Areas.Where(a => a.Material?.Type == MatType.Concrete && a.Hull is { X.Count: >= 3 })
            .Select(a => Enumerable.Range(0, a.Hull!.X.Count).Select(i => (a.Hull.X[i], a.Hull.Y[i])).ToList()).ToList();
        if (hulls.Count == 0) return 0;

        double grad = Math.Sqrt(k.ky * k.ky + k.kz * k.kz);
        double abt = 0;
        if (grad < 1e-12)
            abt = hulls.Sum(Area);
        else
        {
            double nx = k.kz / grad, ny = k.ky / grad;
            var proj = hulls.SelectMany(h => h).Select(p => nx * p.Item1 + ny * p.Item2).ToList();
            double sMax = proj.Max(), sMin = proj.Min();
            double sZero = -k.e0 / grad;   // ε = e0 + grad·s
            double a = sMax - (nx * xPlain + ny * yPlain) / asPlain;
            double xt = Math.Clamp(sMax - sZero, 2 * a, 0.5 * (sMax - sMin));
            if (xt <= 1e-12) return 0;
            double s0 = sMax - xt;
            foreach (var h in hulls)
                if (GridSplit.ClipByHalfPlane(h, nx * s0, ny * s0, nx, ny) is { Count: >= 3 } clipped) abt += Area(clipped);
        }
        double ls = 0.5 * abt / asW * ds;
        double lo = Math.Max(10 * ds, 0.10), hi = Math.Max(lo, Math.Min(40 * ds, 0.40));
        return Math.Clamp(ls, lo, hi);
    }

    static double Area(List<(double X, double Y)> p) =>
        WktHelper.PolygonArea(p.Select(q => q.X).ToList(), p.Select(q => q.Y).ToList());

    /// <summary>
    /// a_crc,1 и a_crc по состояниям полной нагрузки и длительной части (мм, наибольшие по стержням), φ3 — 1,2 при
    /// растяжении (N &gt; 0, кН), иначе 1,0. Трещины от полной нагрузки нет — 0.
    /// </summary>
    static (double Long, double Short) Widths(PlaneState total, PlaneState longPart, double n)
    {
        if (total.Eps.Count == 0) return (0, 0);
        double phi = Phi2 * (n > 1e-3 ? 1.2 : 1.0) * 1e3;
        double aLong = 0, aShort = 0;
        foreach (var (f, et) in total.Eps)
        {
            double el = longPart.Eps.GetValueOrDefault(f);
            double a1 = 1.4 * phi * el * longPart.Ls, a3 = phi * el * longPart.Ls, a2 = phi * et * total.Ls;
            aLong = Math.Max(aLong, a1);
            aShort = Math.Max(aShort, a1 + a2 - a3);
        }
        // Стержень, растянутый только длительной частью (при полной сжат), — своё продолжительное раскрытие.
        foreach (var (f, el) in longPart.Eps)
            if (!total.Eps.ContainsKey(f)) aLong = Math.Max(aLong, 1.4 * phi * el * longPart.Ls);
        return (aLong, Math.Max(aShort, aLong));
    }

    /// <summary>Плоскость деформаций точки <paramref name="point"/> стержня с индексом <paramref name="i"/> полей.</summary>
    static Kurvature Plane(RcSecantStepFields f, int i, int point, bool yAlongLocalY)
    {
        int o = i * RcSecantStepFields.BeamStrainComponents + 3 * point;
        return FemCsfeaBarSectionState.Plane(f.BeamStrains[o], f.BeamStrains[o + 1], f.BeamStrains[o + 2], yAlongLocalY);
    }

    static bool FlagCracked(RcSecantStepFields f, int i) =>
        i < f.BeamFlags.Length && (f.BeamFlags[i] & RcSecantStepFields.Cracked) != 0;

    /// <summary>
    /// Плоскость длительной части ψ·усилий (кН, кН·м, оси сечения) по НДМ: бетон без растяжения, при
    /// <paramref name="psi"/> — поправка ψs с εs,crc при соотношении полных усилий; начальное приближение — ψ·плоскость
    /// полной нагрузки. Сечение мутирует фибры — клон на поток. Null — не сошлось.
    /// </summary>
    static Kurvature? LongPlane(CrossSection s, CalcType calc, bool psi, (double N, double Mx, double My) total, double share,
        Kurvature totalPlane)
    {
        var epsCrc = psi ? SecantCrossSectionBuilder.EpsCrcAtCracking(s, calc, calc, total.N, total.Mx, total.My) : null;
        var solver = new StrainSolver(s, calc, ten: false, ca: true,
            evaluate: epsCrc == null ? null : k => Curvature8232.ApplyPsiCorrection(s, k, s.Integral(k, calc, false, true), epsCrc, calc));
        var seed = new Kurvature { e0 = share * totalPlane.e0, ky = share * totalPlane.ky, kz = share * totalPlane.kz };
        var plane = solver.Solve(share * total.N, share * total.Mx, share * total.My, seed);
        return solver.Converged ? plane : null;
    }

    // ------------------------------------------------------------ все стержни шага

    /// <summary>
    /// Ширина раскрытия всех ЖБ-стержней шага в точках i, ½, j при доле длительной части (параллельно):
    /// <paramref name="calc"/> и <paramref name="psi"/> — вид расчёта и ψs постановки. Без деформаций стержней в полях —
    /// упрощённая проверка по усилиям.
    /// </summary>
    public static Dictionary<int, FemCsfeaBarCrackWidth> Compute(IReadOnlyList<FemCsfeaBarCrackElement> elements,
        RcSecantStepFields fields, double longShare, CalcType calc, bool psi, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (!fields.HasBeamStrains)
            return ComputeAll(elements, fields, ct, (e, i, p) => Simplified(e, FemCsfeaBarSectionState.ElementForces(fields, i, p), longShare));
        double share = Math.Clamp(longShare, 0, 1);
        // Досчёт длительной части мутирует фибры сечения — клоны на поток (диаграммы общие, только чтение).
        using var clones = new ThreadLocal<Dictionary<CrossSection, CrossSection>>(() => new(ReferenceEqualityComparer.Instance));
        return ComputeAll(elements, fields, ct, (e, i, p) =>
        {
            var plane = Plane(fields, i, p, e.YAlongLocalY);
            var forces = FemCsfeaBarSectionState.ToSection(FemCsfeaBarSectionState.ElementForces(fields, i, p), e.YAlongLocalY);
            var total = State(e.Section, plane, calc, FlagCracked(fields, i));
            PlaneState longPart;
            if (total.Eps.Count == 0 || share <= 0) longPart = PlaneState.None;
            else if (share >= 1) longPart = total;
            else
            {
                var map = clones.Value!;
                if (!map.TryGetValue(e.Section, out var clone)) map[e.Section] = clone = e.Section.CloneForCalc();
                // Не сошлось — длительная часть по плоскости полной нагрузки, умноженной на долю (в запас: при меньшей
                // нагрузке секущая жёсткость не ниже).
                var lp = LongPlane(clone, calc, psi, forces, share, plane)
                    ?? new Kurvature { e0 = share * plane.e0, ky = share * plane.ky, kz = share * plane.kz };
                longPart = State(e.Section, lp, calc, elementCracked: true);
            }
            var (aLong, aShort) = Widths(total, longPart, forces.N);
            return (aLong, aShort);
        });
    }

    /// <summary>
    /// Ширина раскрытия всех ЖБ-стержней по полям полной нагрузки и её длительной части (пара NL варианта пакета):
    /// длительная часть — по своей записанной плоскости и флагам трещин.
    /// </summary>
    public static Dictionary<int, FemCsfeaBarCrackWidth> Compute(IReadOnlyList<FemCsfeaBarCrackElement> elements,
        RcSecantStepFields fields, RcSecantStepFields longFields, CalcType calc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(longFields);
        if (!longFields.BeamIds.AsSpan().SequenceEqual(fields.BeamIds))
            throw new InvalidOperationException("Поля полной нагрузки и длительной части записаны на разных сетках.");
        if (!fields.HasBeamStrains || !longFields.HasBeamStrains)
            return ComputeAll(elements, fields, ct, (e, i, p) => Simplified(e, FemCsfeaBarSectionState.ElementForces(fields, i, p), 1.0,
                FemCsfeaBarSectionState.ElementForces(longFields, i, p)));
        return ComputeAll(elements, fields, ct, (e, i, p) =>
        {
            var total = State(e.Section, Plane(fields, i, p, e.YAlongLocalY), calc, FlagCracked(fields, i));
            var longPart = State(e.Section, Plane(longFields, i, p, e.YAlongLocalY), calc, FlagCracked(longFields, i));
            var (aLong, aShort) = Widths(total, longPart, FemCsfeaBarSectionState.ElementForces(fields, i, p).N / 1e3);
            return (aLong, aShort);
        });
    }

    static Dictionary<int, FemCsfeaBarCrackWidth> ComputeAll(IReadOnlyList<FemCsfeaBarCrackElement> elements, RcSecantStepFields fields,
        CancellationToken ct, Func<FemCsfeaBarCrackElement, int, int, (double? Long, double? Short)> compute)
    {
        ArgumentNullException.ThrowIfNull(elements);
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

    // ------------------------------------------------------------ запасной путь: упрощённая проверка по усилиям

    /// <summary>
    /// Упрощённая проверка в одной точке по усилиям КЭ (Н, Н·м: N, My, Mz в осях КЭ); <paramref name="longShare"/> —
    /// доля длительной части 0…1 либо <paramref name="longForces"/> — длительная часть своим расчётом.
    /// </summary>
    public static (double? Long, double? Short) Simplified(FemCsfeaBarCrackElement e, (double N, double My, double Mz) forces,
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
            var options = new Sp63CrackWidthOptions(shape, axis, Phi1: 1.0, Phi2: Phi2, AcrcLimMm: 0.3,
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

    /// <summary>Упрощённая проверка всех стержней по усилиям при доле длительной части.</summary>
    public static Dictionary<int, FemCsfeaBarCrackWidth> Simplified(IReadOnlyList<FemCsfeaBarCrackElement> elements,
        RcSecantStepFields fields, double longShare, CancellationToken ct = default) =>
        ComputeAll(elements, fields, ct, (e, i, p) => Simplified(e, FemCsfeaBarSectionState.ElementForces(fields, i, p), longShare));
}
