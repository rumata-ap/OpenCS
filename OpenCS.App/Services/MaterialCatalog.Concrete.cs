using CScore;

namespace OpenCS.Services;

/// <summary>Вид бетона справочника: тяжёлый, мелкозернистый группы А (естественного твердения), группы Б (автоклавный).</summary>
public enum ConcreteKind { Heavy, FineA, FineB }

/// <summary>
/// Бетон из справочника: класс, вид, влажность среды (выбирает таблицу NL) и коэффициенты окна справочника —
/// бетонная конструкция γb2 = 0,9 (п. 6.1.12 б СП 63), слой бетонирования > 1,5 м γb3 = 0,85 (п. 6.1.12 в),
/// тепловая обработка E × 0,89 (только мелкозернистый группы А).
/// </summary>
public sealed record ConcreteRecipe(string Class, ConcreteKind Kind = ConcreteKind.Heavy,
    Dampness Dampness = Dampness.от40_до70, bool Plain = false, bool TallLayer = false, bool HeatTreated = false);

public static partial class MaterialCatalog
{
    static string ConcretePrefix(ConcreteKind kind) => kind switch
    {
        ConcreteKind.FineA => "Мелкозернистый группы А",
        ConcreteKind.FineB => "Мелкозернистый группы Б",
        _ => HeavyConcretePrefix
    };

    static string NlTable(Dampness dampness) => dampness switch
    {
        Dampness.ниже_40 => "NL_1",
        Dampness.свыше_70 => "NL_3",
        _ => "NL_2"
    };

    /// <summary>Бетон по рецепту; null — класса нет в справочнике.</summary>
    public static Material? CreateConcrete(ConcreteRecipe recipe, string? directory = null)
    {
        directory ??= DefaultDirectory;
        string prefix = ConcretePrefix(recipe.Kind);
        string key = ClassKey(recipe.Class);
        var c = Find(directory, $"{prefix}_C.csv", key);
        var cl = Find(directory, $"{prefix}_CL.csv", key);
        var n = Find(directory, $"{prefix}_N.csv", key);
        var nl = Find(directory, $"{prefix}_{NlTable(recipe.Dampness)}.csv", key);
        if (c == null || cl == null || n == null || nl == null) return null;
        var (fc, fcl, fn, fnl) = ApplyConcreteFactors(c, cl, n, nl, recipe);
        return Make(n.Tag, ConcreteDescription(recipe), MatType.Concrete, fc, fcl, fn, fnl);
    }

    /// <summary>
    /// Коэффициенты рецепта к строкам справочника (копии): γb2·γb3 — к расчётной прочности C и CL, kE — к модулю всех
    /// наборов; приведённые деформации εb1 = 0,6·Rb/E и εbt1 = 0,6·Rbt/E — заново. Так же считает окно справочника.
    /// </summary>
    public static (MaterialChars C, MaterialChars CL, MaterialChars N, MaterialChars NL) ApplyConcreteFactors(
        MaterialChars c, MaterialChars cl, MaterialChars n, MaterialChars nl, ConcreteRecipe recipe)
    {
        double gb = (recipe.Plain ? 0.9 : 1.0) * (recipe.TallLayer ? 0.85 : 1.0);
        double kE = recipe.HeatTreated && recipe.Kind == ConcreteKind.FineA ? 0.89 : 1.0;

        MaterialChars Scale(MaterialChars source, bool design)
        {
            var x = source.Clone();
            x.E *= kE;
            if (design) x.Fc *= gb;
            x.Et1 = 0.6 * x.Ft / x.E;
            x.Ec1 = 0.6 * x.Fc / x.E;
            return x;
        }
        return (Scale(c, true), Scale(cl, true), Scale(n, false), Scale(nl, false));
    }

    /// <summary>Описание материала: вид бетона и введённые коэффициенты (материал хранит уже умноженное Rb).</summary>
    public static string ConcreteDescription(ConcreteRecipe recipe)
    {
        string text = recipe.Kind switch
        {
            ConcreteKind.FineA => "Бетон мелкозернистый группы А (естественного твердения) по СП 63.13330",
            ConcreteKind.FineB => "Бетон мелкозернистый группы Б (автоклавного твердения) по СП 63.13330",
            _ => "Бетон тяжелый по СП 63.13330"
        };
        var factors = new List<string>();
        if (recipe.Plain) factors.Add("бетонная конструкция, γb2 = 0,9");
        if (recipe.TallLayer) factors.Add("слой бетонирования > 1,5 м, γb3 = 0,85");
        if (recipe.HeatTreated && recipe.Kind == ConcreteKind.FineA) factors.Add("тепловая обработка, E × 0,89");
        return factors.Count == 0 ? text : text + "; " + string.Join("; ", factors);
    }
}
