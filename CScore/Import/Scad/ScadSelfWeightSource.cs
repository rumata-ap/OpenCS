using System.Globalization;
using System.Text.RegularExpressions;
using CScore.Fem;
using CScore.Fem.Loads;

namespace CScore.Import;

/// <summary>
/// Собственный вес КЭ схемы SCAD по сохранённым жёсткостям: удельный вес — RO строки жёсткости (в единицах проекта),
/// площадь стержня — брус S0. Сортаменты (STZ) площадь не дают — такие стержни остаются без веса (в журнал).
/// </summary>
public sealed class ScadSelfWeightSource(IReadOnlyDictionary<int, LiraStiffnessRecord> stiffnesses, double forceUnitN,
    double lengthUnitM) : IFemSelfWeightSource
{
    static readonly Regex RoToken = new(@"\bRO\s+([-+0-9.,eE]+)", RegexOptions.Compiled);

    /// <inheritdoc/>
    public double? UnitWeight(FemElement element)
    {
        if (element.StiffnessNum is not { } id || !stiffnesses.TryGetValue(id, out var s)) return null;
        var m = RoToken.Match(s.Params);
        if (!m.Success || !double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double ro)) return null;
        return ro * forceUnitN / (lengthUnitM * lengthUnitM * lengthUnitM);
    }

    /// <inheritdoc/>
    public double? BarArea(FemElement element) =>
        element.StiffnessNum is { } id && stiffnesses.TryGetValue(id, out var s) && ScadStiffnessParams.BarRect(s) is { } r
            ? r.WidthM * r.HeightM : null;
}
