using System.Globalization;

namespace OpenCS.ViewModels;

/// <summary>
/// Ключ сортировки тега узла или КЭ в таблицах (<c>SortMemberPath</c>): числовые теги — по значению (4953 раньше
/// 14994), за ними прочие — по алфавиту. Строкой «1518» шло бы раньше «4953» и позже «14994».
/// </summary>
public readonly struct TagSortKey : IComparable, IComparable<TagSortKey>
{
    readonly string _tag;
    readonly bool _numeric;
    readonly long _number;

    public TagSortKey(string? tag)
    {
        _tag = tag?.Trim() ?? "";
        _numeric = long.TryParse(_tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out _number);
    }

    public int CompareTo(TagSortKey other)
    {
        if (_numeric != other._numeric) return _numeric ? -1 : 1;
        if (_numeric && _number != other._number) return _number.CompareTo(other._number);
        return string.CompareOrdinal(_tag, other._tag);
    }

    public int CompareTo(object? obj) => obj is TagSortKey other ? CompareTo(other) : 1;

    public override string ToString() => _tag;
}
