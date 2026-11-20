using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Поиск узла по номеру в таблицах продавливания по МКЭ и краткий текст сообщения ЛИРЫ.</summary>
public sealed class PunchingFemNodeSearchTests
{
    sealed record Row(string Node);

    static readonly Row[] Rows = [new("15059"), new("4942"), new("4940"), new("494"), new("25917")];

    [Fact]
    public void ExactMatchWinsOverPrefix() =>
        Assert.Equal("494", PunchingFemNodeSearch.Find(Rows, r => r.Node, "494")!.Node);

    [Fact]
    public void PrefixOrderedByNumberNotString()
    {
        // «49» — 494 (по номеру меньше 4940 и 4942), а не первая по строке строка.
        Assert.Equal("494", PunchingFemNodeSearch.Find(Rows, r => r.Node, "49")!.Node);
        Assert.Equal("4940", PunchingFemNodeSearch.Find(Rows, r => r.Node, "4940")!.Node);
        Assert.Equal("25917", PunchingFemNodeSearch.Find(Rows, r => r.Node, "2")!.Node);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("777")]
    public void EmptyOrMissingGivesNull(string? text) =>
        Assert.Null(PunchingFemNodeSearch.Find(Rows, r => r.Node, text));

    [Fact]
    public void LiraCode170IsExplained()
    {
        var s = PunchingFemLiraText.Short(170, 0, 0);
        Assert.Contains("N ≥ 0", s);
        Assert.Contains("#170", s);
        Assert.Contains("#5", PunchingFemLiraText.Short(5, 0, 0));
    }
}
