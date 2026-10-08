using CScore.Fem;

namespace CScore.Import;

/// <summary>
/// Свойства КЭ схемы ЛИРЫ по сохранённым жёсткостям (таблица «Жёсткости» API, <see cref="LiraStiffnessRecord.Params"/>)
/// в единицах документа (<see cref="LiraUnits"/>):
/// <list type="bullet">
/// <item>пластины (<c>PLATE_END</c>) — E, V, H; коэффициенты к жёсткостям плит и стен (PLKE, WLKE) не применяются;</item>
/// <item>«Брус» — E и размеры B×H (B вдоль Y1, H вдоль Z1; изгиб вокруг Y1 — B·H³/12), кручение — по Сен-Венану;
/// коэффициенты, заложенные ЛИРОЙ в численные EIy/EIz бруса, не применяются — модуль материала;</item>
/// <item>прочие стандартные сечения (<c>BAR_END</c>) — численные EF, EIy, EIz, GIk при E &gt; 0;</item>
/// <item>численная жёсткость (<c>DD10_END</c>, без E) — EF, EIy, EIz, GIk при условном E = 3·10¹⁰ Па, Ro — погонный
/// вес (сила/длина);</item>
/// <item>стальной профиль сортамента — <see cref="ImportedSteelElastic"/>, сталь E = 2,06·10¹¹ Па, ν = 0,3, 7,85 т/м³.</item>
/// </list>
/// Стержень без E и без численных жёсткостей (стержневой аналог) — без свойств. ν стержня — Mu, по умолчанию 0,2.
/// </summary>
public sealed class LiraElementStiffnessSource : IFemElementStiffnessSource
{
    const double SteelE = 2.06e11, SteelNu = 0.3, SteelUnitWeight = 7.85 * 9810, NumericE = 3e10, DefaultNu = 0.2;

    readonly IReadOnlyDictionary<int, LiraStiffnessRecord> _stiffnesses;
    readonly LiraUnits _units;
    readonly SteelProfileIndex? _steel;
    readonly Dictionary<int, BarProps?> _bars = new();

    /// <summary>Свойства стержня, его площадь для собственного веса и удельный вес, Н/м³.</summary>
    sealed record BarProps(FemBarStiffness Stiffness, double Area, double? UnitWeight);

    /// <param name="stiffnesses">Жёсткости схемы по номеру.</param>
    /// <param name="units">Единицы характеристик материалов документа.</param>
    /// <param name="steel">Профили стальных жёсткостей сортамента; null — стальные стержни без свойств.</param>
    public LiraElementStiffnessSource(IReadOnlyDictionary<int, LiraStiffnessRecord> stiffnesses, LiraUnits units,
        SteelProfileIndex? steel = null)
    {
        _stiffnesses = stiffnesses;
        _units = units;
        _steel = steel;
    }

    /// <inheritdoc/>
    public FemShellStiffness? Shell(FemElement element)
    {
        if (Record(element) is not { } s || !LiraStiffnessParams.IsPlate(s)) return null;
        if (LiraStiffnessParams.Value(s.Params, "E") is not { } e || !(e > 0)) return null;
        double nu = LiraStiffnessParams.Value(s.Params, "V") ?? DefaultNu;
        double? h = element.ThicknessM is > 0 ? element.ThicknessM : LiraStiffnessParams.PlateThicknessM(s);
        return h is > 0 ? new FemShellStiffness(_units.Stress(e), nu, h.Value) : null;
    }

    /// <inheritdoc/>
    public FemBarStiffness? Bar(FemElement element) => BarOf(element)?.Stiffness;

    /// <inheritdoc/>
    public double? UnitWeight(FemElement element)
    {
        if (element.ElemType == "beam") return BarOf(element)?.UnitWeight;
        if (Record(element) is not { } s || !LiraStiffnessParams.IsPlate(s)) return null;
        return LiraStiffnessParams.Value(s.Params, "Ro") is { } ro && ro > 0 ? _units.UnitWeight(ro) : null;
    }

    /// <inheritdoc/>
    public double? BarArea(FemElement element) => BarOf(element)?.Area;

    BarProps? BarOf(FemElement element)
    {
        if (element.StiffnessNum is not { } id) return null;
        if (_bars.TryGetValue(id, out var cached)) return cached;
        return _bars[id] = _stiffnesses.TryGetValue(id, out var s) ? ComputeBar(s) : null;
    }

    BarProps? ComputeBar(LiraStiffnessRecord s)
    {
        if (LiraSteelProfiles.SteelRef(s.Params) != null)
            return _steel?.Find(s.Id)?.Shape is { } shape && ImportedSteelElastic.Compute(shape) is { } p
                ? new BarProps(new FemBarStiffness(SteelE, SteelE / (2 * (1 + SteelNu)), p.A, p.Iy, p.Iz, p.It), p.A, SteelUnitWeight)
                : null;

        double? ro = LiraStiffnessParams.Value(s.Params, "Ro") is { } r && r > 0 ? r : null;
        if (s.Params.Contains("DD10_END", StringComparison.Ordinal))
        {
            var n = Numeric(s, NumericE, DefaultNu);
            return n == null ? null : new BarProps(n, n.A, ro is { } w ? _units.PerLength(w) / n.A : null);
        }
        if (!LiraStiffnessParams.IsBar(s)) return null;

        double nu = LiraStiffnessParams.Value(s.Params, "Mu") ?? DefaultNu;
        if (LiraStiffnessParams.Value(s.Params, "E") is not { } e0 || !(e0 > 0)) return null;
        double e = _units.Stress(e0), unitWeight = ro is { } q ? _units.UnitWeight(q) : 0;
        if (LiraStiffnessParams.BarRect(s) is { } rect)
        {
            double b = rect.WidthM, h = rect.HeightM;
            var st = new FemBarStiffness(e, e / (2 * (1 + nu)), b * h, b * h * h * h / 12, h * b * b * b / 12,
                RectTorsion(b, h));
            return new BarProps(st, b * h, ro == null ? null : unitWeight);
        }
        var num = Numeric(s, e, nu);
        return num == null ? null : new BarProps(num, num.A, ro == null ? null : unitWeight);
    }

    /// <summary>Численные EF, EIy, EIz, GIk → свойства при модуле <paramref name="e"/>; null — не заданы.</summary>
    FemBarStiffness? Numeric(LiraStiffnessRecord s, double e, double nu)
    {
        double? ef = LiraStiffnessParams.Value(s.Params, "EF"), eiy = LiraStiffnessParams.Value(s.Params, "EIy"),
            eiz = LiraStiffnessParams.Value(s.Params, "EIz"), gik = LiraStiffnessParams.Value(s.Params, "GIk");
        if (ef is not > 0 || eiy is not > 0 || eiz is not > 0) return null;
        double g = e / (2 * (1 + nu));
        double iy = _units.Rigidity(eiy.Value) / e, iz = _units.Rigidity(eiz.Value) / e;
        double j = gik is > 0 ? _units.Rigidity(gik.Value) / g : iy + iz;
        return new FemBarStiffness(e, g, _units.Force(ef.Value) / e, iy, iz, j);
    }

    /// <summary>Момент инерции при кручении прямоугольника по Сен-Венану (приближение Роарка), м⁴.</summary>
    internal static double RectTorsion(double b, double h)
    {
        double a = Math.Max(b, h) / 2, c = Math.Min(b, h) / 2;
        return a * c * c * c * (16.0 / 3 - 3.36 * c / a * (1 - Math.Pow(c / a, 4) / 12));
    }

    LiraStiffnessRecord? Record(FemElement element) =>
        element.StiffnessNum is { } id && _stiffnesses.TryGetValue(id, out var s) ? s : null;
}
