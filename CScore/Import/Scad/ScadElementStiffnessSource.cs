using System.Globalization;
using System.Text.RegularExpressions;
using CScore.Fem;

namespace CScore.Import;

/// <summary>
/// Свойства КЭ схемы SCAD по сохранённым жёсткостям (<see cref="ScadStiffnessParams.ScadKindCode"/>, строка SCAD в
/// <see cref="LiraStiffnessRecord.Params"/>) в единицах вложения: пластины GE/GEI — «E ν h», стержни — параметрические
/// S0/S3/S6 (<see cref="ScadBarSection"/>; E — второе число строки, ν — «NU», по умолчанию 0,2) и профили сортамента STZ
/// (<see cref="ImportedSteelElastic"/>; сталь по умолчанию SCAD: E = 2,06·10¹¹ Па, ν = 0,3). Удельный вес — RO строки,
/// у сортамента без RO — 7,85 т/м³; площадь стержня — по тем же сечениям.
/// </summary>
public sealed class ScadElementStiffnessSource : IFemElementStiffnessSource
{
    const double SteelE = 2.06e11, SteelNu = 0.3, SteelUnitWeight = 7.85 * 9810;
    static readonly Regex RoToken = new(@"\bRO\s+([-+0-9.,eE]+)", RegexOptions.Compiled);
    static readonly char[] Separators = [' ', '\t', '\r', '\n'];

    readonly IReadOnlyDictionary<int, LiraStiffnessRecord> _stiffnesses;
    readonly SteelProfileIndex? _steel;
    readonly double _fu, _lu;
    readonly Dictionary<int, FemBarStiffness?> _bars = new();

    /// <param name="stiffnesses">Жёсткости схемы по номеру.</param>
    /// <param name="forceUnitN">Единица силы проекта SCAD, Н.</param>
    /// <param name="lengthUnitM">Единица длины проекта SCAD, м.</param>
    /// <param name="steel">Профили сортамента жёсткостей STZ; null — сортаментные стержни без свойств.</param>
    public ScadElementStiffnessSource(IReadOnlyDictionary<int, LiraStiffnessRecord> stiffnesses, double forceUnitN,
        double lengthUnitM, SteelProfileIndex? steel = null)
    {
        _stiffnesses = stiffnesses;
        _fu = forceUnitN;
        _lu = lengthUnitM;
        _steel = steel;
    }

    /// <inheritdoc/>
    public FemShellStiffness? Shell(FemElement element)
    {
        if (Record(element) is not { } s) return null;
        var parts = s.Params.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || parts[0] is not ("GE" or "GEI")) return null;
        if (Number(parts, 1) is not { } e0 || !(e0 > 0)) return null;
        double nu = Number(parts, 2) ?? 0.2;
        double? h = element.ThicknessM is > 0 ? element.ThicknessM : Number(parts, 3) * _lu;
        return h is > 0 ? new FemShellStiffness(e0 * _fu / (_lu * _lu), nu, h.Value) : null;
    }

    /// <inheritdoc/>
    public FemBarStiffness? Bar(FemElement element)
    {
        if (element.StiffnessNum is not { } id) return null;
        if (_bars.TryGetValue(id, out var cached)) return cached;
        return _bars[id] = ComputeBar(id);
    }

    FemBarStiffness? ComputeBar(int id)
    {
        if (!_stiffnesses.TryGetValue(id, out var s)) return null;
        if (ScadBarSection.Parse(s.Params, s.SectionUnitM) is { } g)
        {
            var parts = s.Params.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            if (Number(parts, 1) is not { } e0 || !(e0 > 0)) return null;
            int iNu = Array.IndexOf(parts, "NU");
            double nu = iNu >= 0 ? Number(parts, iNu + 1) ?? 0.2 : 0.2;
            double e = e0 * _fu / (_lu * _lu);
            return new FemBarStiffness(e, e / (2 * (1 + nu)), g.A, g.Iy, g.Iz, g.J);
        }
        if (_steel?.Find(id)?.Shape is { } shape && ImportedSteelElastic.Compute(shape) is { } p)
            return new FemBarStiffness(SteelE, SteelE / (2 * (1 + SteelNu)), p.A, p.Iy, p.Iz, p.It);
        return null;
    }

    /// <inheritdoc/>
    public double? UnitWeight(FemElement element)
    {
        if (Record(element) is not { } s) return null;
        var m = RoToken.Match(s.Params);
        if (m.Success && double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double ro) && ro > 0)
            return ro * _fu / (_lu * _lu * _lu);
        return ScadSteelProfiles.SteelRef(s.Params) != null && Bar(element) != null ? SteelUnitWeight : null;
    }

    /// <inheritdoc/>
    public double? BarArea(FemElement element) => Bar(element)?.A;

    LiraStiffnessRecord? Record(FemElement element) =>
        element.StiffnessNum is { } id && _stiffnesses.TryGetValue(id, out var s) ? s : null;

    static double? Number(string[] parts, int index) =>
        index < parts.Length && double.TryParse(parts[index].Replace(',', '.'), NumberStyles.Float,
            CultureInfo.InvariantCulture, out double v) ? v : null;
}
