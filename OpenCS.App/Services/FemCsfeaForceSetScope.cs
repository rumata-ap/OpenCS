using System.Globalization;

namespace OpenCS.Services;

/// <summary>
/// Охват наборов усилий CSfea: КЭ сетки (теги) и их конструктивные элементы — выбранное в 3D-виде результата
/// (спека 2026-10-17 «наборы по вариантам», п. 2.1). Null вместо охвата — вся схема.
/// </summary>
public sealed class FemCsfeaForceSetScope
{
    /// <summary>КонЭ в описании набора — первые столько.</summary>
    public const int DescribedMembers = 20;

    public FemCsfeaForceSetScope(IEnumerable<int> elements, IEnumerable<string> members)
    {
        Elements = elements.ToHashSet();
        Members = members.ToList();
    }

    public IReadOnlySet<int> Elements { get; }

    public IReadOnlyList<string> Members { get; }

    /// <summary>Охват по выбору результата: теги КЭ строками (нечисловые пропускаются); null — числовых тегов нет.</summary>
    public static FemCsfeaForceSetScope? FromSelection(IEnumerable<string> elements, IEnumerable<string> members)
    {
        var tags = elements.Select(t => int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : (int?)null)
            .OfType<int>().ToList();
        return tags.Count == 0 ? null : new FemCsfeaForceSetScope(tags, members);
    }

    /// <summary>Пометка охвата в имени набора: « [выбор: 42 КЭ]».</summary>
    public string TagSuffix => string.Create(CultureInfo.CurrentCulture, $" [выбор: {Elements.Count} КЭ]");

    /// <summary>Охват в описании набора: число КЭ и КонЭ (первые <see cref="DescribedMembers"/>, далее «и ещё N»).</summary>
    public string Description
    {
        get
        {
            string text = string.Create(CultureInfo.CurrentCulture, $"Охват — выбранные КЭ: {Elements.Count}");
            if (Members.Count == 0) return text + ".";
            string list = string.Join(", ", Members.Take(DescribedMembers));
            if (Members.Count > DescribedMembers)
                list += string.Create(CultureInfo.CurrentCulture, $" и ещё {Members.Count - DescribedMembers}");
            return text + "; КонЭ: " + list + ".";
        }
    }
}
