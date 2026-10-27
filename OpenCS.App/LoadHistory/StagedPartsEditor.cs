using CScore;

namespace OpenCS.LoadHistory;

/// <summary>
/// Операции над частями стадийного сечения для редактора (без WPF). После каждой операции индексы
/// частей — 0..n−1, объединённый список областей сечения пересобран.
/// </summary>
public static class StagedPartsEditor
{
    /// <summary>Добавляет часть со своими областями (пустую), Index = номер в списке.</summary>
    public static SectionPart AddOwnPart(StagedSection s, string tag)
    {
        var part = new SectionPart { Tag = tag };
        s.Parts.Add(part);
        Normalize(s);
        return part;
    }

    /// <summary>Добавляет часть-ссылку на обычное сечение (живой список его областей).</summary>
    public static SectionPart AddLinkedPart(StagedSection s, CrossSection source, string tag)
    {
        var part = new SectionPart { Tag = tag, SourceSectionId = source.Id, Areas = source.Areas };
        s.Parts.Add(part);
        Normalize(s);
        return part;
    }

    /// <summary>Удаляет часть.</summary>
    public static void Remove(StagedSection s, SectionPart part)
    {
        s.Parts.Remove(part);
        Normalize(s);
    }

    /// <summary>Сдвигает часть на <paramref name="delta"/> позиций в пределах списка.</summary>
    public static void Move(StagedSection s, SectionPart part, int delta)
    {
        int i = s.Parts.IndexOf(part);
        if (i < 0) return;
        int j = Math.Clamp(i + delta, 0, s.Parts.Count - 1);
        if (j == i) return;
        s.Parts.RemoveAt(i);
        s.Parts.Insert(j, part);
        Normalize(s);
    }

    /// <summary>Переводит ссылочную часть в собственную (копия списка областей, ссылка снимается).</summary>
    public static void Detach(SectionPart part)
    {
        part.Areas = [.. part.Areas];
        part.SourceSectionId = null;
    }

    /// <summary>Сечения, допустимые как источник: обычные (не стадийные), не само сечение.</summary>
    public static IEnumerable<CrossSection> SourceCandidates(IEnumerable<CrossSection> all, StagedSection self) =>
        all.Where(c => c is not StagedSection && !ReferenceEquals(c, self));

    /// <summary>Индексы частей по порядку и объединение областей.</summary>
    public static void Normalize(StagedSection s)
    {
        for (int i = 0; i < s.Parts.Count; i++) s.Parts[i].Index = i;
        s.SyncAreas();
    }
}
