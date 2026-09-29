using CScore.Submodel;

namespace OpenCS.ViewModels;

/// <summary>
/// Чистые правила кнопки «Извлечь субмодель» в редакторе родительской схемы: причина недоступности и имя
/// дочерней схемы по умолчанию. Без БД и без WPF — тестируются отдельно от <see cref="FemSchemaEditorVM"/>.
/// </summary>
public static class SubmodelExtractGate
{
    /// <summary>
    /// Ключ ресурса с причиной, по которой извлекать нельзя; <c>null</c> — можно. Порядок проверок —
    /// от того, что инженеру исправлять первым.
    /// </summary>
    public static string? Reason(ChainVerdict? verdict, bool selectionMatches, bool hasParentAnalysis, bool isDirty)
    {
        if (isDirty) return "SubmodelExtractBlockDirty";
        if (verdict is null || !selectionMatches) return "SubmodelExtractBlockCheckChain";
        if (verdict == ChainVerdict.NotExtractable) return "SubmodelExtractBlockNotExtractable";
        if (!hasParentAnalysis) return "SubmodelExtractBlockNoParentAnalysis";
        return null;
    }

    /// <summary>
    /// Имя дочерней схемы: «{родитель} — субмодель {первый}…{последний}» по тегам конструктивных элементов
    /// крайних сегментов (без тега элемента — тег КЭ сетки); один элемент — <paramref name="singleFormat"/>.
    /// </summary>
    public static string DefaultTag(string format, string singleFormat, string parentTag, StraightBeamChain chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        static string Name(OrderedBeamSegment s) => s.Source.SourceMemberTag ?? s.Source.SourceKey;
        string first = Name(chain.Segments[0]);
        string last = Name(chain.Segments[^1]);
        return first == last
            ? string.Format(singleFormat, parentTag, first)
            : string.Format(format, parentTag, first, last);
    }
}
