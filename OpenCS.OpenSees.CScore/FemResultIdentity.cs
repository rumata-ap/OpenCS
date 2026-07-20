using System.Globalization;

namespace OpenCS.OpenSees.CScore;

/// <summary>Преобразует идентичность mesh-КЭ в идентичность исходного стержня.</summary>
public static class FemResultIdentity
{
    /// <summary>Возвращает тег конструктивного стержня или запасной mesh-тег.</summary>
    public static string ResolveMemberTag(string? sourceMemberTag, int meshElementTag) =>
        string.IsNullOrWhiteSpace(sourceMemberTag)
            ? meshElementTag.ToString(CultureInfo.InvariantCulture)
            : sourceMemberTag;
}
