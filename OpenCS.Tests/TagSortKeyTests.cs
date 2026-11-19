using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Сортировка тегов узлов в таблицах продавливания: числа по значению, затем прочие по алфавиту.</summary>
public sealed class TagSortKeyTests
{
    [Fact]
    public void NumericByValue_ThenOtherOrdinal()
    {
        string[] tags = ["14994", "1518", "4953", "Б", "А", " 15002 ", "200"];
        var sorted = tags.OrderBy(t => new TagSortKey(t)).Select(t => t.Trim()).ToArray();
        Assert.Equal(["200", "1518", "4953", "14994", "15002", "А", "Б"], sorted);
        // DataGrid сравнивает упакованные значения через IComparable.
        Assert.True(((IComparable)new TagSortKey("4953")).CompareTo(new TagSortKey("14994")) < 0);
    }
}
