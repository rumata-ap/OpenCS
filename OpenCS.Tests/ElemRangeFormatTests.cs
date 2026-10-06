using OpenCS.Views;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Строка номеров КЭ в диалоге группы: выделение сворачивается в диапазоны и разбирается обратно.</summary>
public sealed class ElemRangeFormatTests
{
    [Fact]
    public void Numbers_CollapseToRanges_AndParseBack()
    {
        string[] tags = ["106", "101", "102", "103", "143", "144", "116", "117", "118"];
        string range = LiraElemRangeDialog.FormatRange(tags);
        Assert.Equal("101-103 106 116-118 143-144", range);
        Assert.Equal(tags.Select(int.Parse).Order(), LiraElemRangeDialog.ParseRange(range));
    }

    [Fact]
    public void NonNumericTags_AreAppendedAsIs()
    {
        Assert.Equal("1 3 Колонна · 1", LiraElemRangeDialog.FormatRange(["3", "Колонна · 1", "1", "1"]));
        Assert.Equal("", LiraElemRangeDialog.FormatRange([]));
    }
}
