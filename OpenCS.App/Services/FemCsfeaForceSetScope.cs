using System.Globalization;
using CScore;
using CScore.Fem;

namespace OpenCS.Services;

/// <summary>
/// Охват наборов усилий CSfea: КЭ сетки (теги) и их конструктивные элементы — выбранное в 3D-виде результата
/// (спека 2026-10-17 «наборы по вариантам», п. 2.1). Null вместо охвата — вся схема. Если все КЭ охвата — одного КонЭ,
/// наборы привязываются к нему (<see cref="ForceSet.SourceElementId"/>, как наборы КонЭ OpenSees); у охвата из
/// нескольких КонЭ — общий набор схемы без привязки.
/// </summary>
public sealed class FemCsfeaForceSetScope
{
    /// <summary>КонЭ в описании набора — первые столько.</summary>
    public const int DescribedMembers = 20;

    public FemCsfeaForceSetScope(IEnumerable<int> elements, IEnumerable<string> members, FemMember? member = null)
    {
        Elements = elements.ToHashSet();
        Members = members.ToList();
        Member = member;
    }

    public IReadOnlySet<int> Elements { get; }

    public IReadOnlyList<string> Members { get; }

    /// <summary>КонЭ, которому принадлежат все КЭ охвата, — к нему привязываются наборы; null — без привязки.</summary>
    public FemMember? Member { get; }

    /// <summary>
    /// Охват по выбору результата: теги КЭ строками (нечисловые пропускаются); null — числовых тегов нет.
    /// <paramref name="member"/> — КонЭ всех выбранных КЭ (null — КЭ разных КонЭ или без КонЭ).
    /// </summary>
    public static FemCsfeaForceSetScope? FromSelection(IEnumerable<string> elements, IEnumerable<string> members,
        FemMember? member = null)
    {
        var tags = elements.Select(t => int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : (int?)null)
            .OfType<int>().ToList();
        return tags.Count == 0 ? null : new FemCsfeaForceSetScope(tags, members, member);
    }

    /// <summary>Пометка охвата в имени набора: « [выбор: 42 КЭ]», у привязанного к КонЭ — « [КонЭ Б-1: 3 КЭ]».</summary>
    public string TagSuffix => Member is { } m
        ? string.Create(CultureInfo.CurrentCulture, $" [КонЭ {m.ElemTag}: {Elements.Count} КЭ]")
        : string.Create(CultureInfo.CurrentCulture, $" [выбор: {Elements.Count} КЭ]");

    /// <summary>Привязывает набор к КонЭ охвата (если он есть).</summary>
    public void Bind(ForceSet set)
    {
        if (Member is not { } m) return;
        set.SourceElementId = m.Id;
        set.SourceElementTag = m.ElemTag;
    }

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
