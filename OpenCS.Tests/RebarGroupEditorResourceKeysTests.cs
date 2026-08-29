using System.Xml.Linq;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Новые подписи редактора групп арматуры есть в обоих словарях.</summary>
public sealed class RebarGroupEditorResourceKeysTests
{
    static readonly string[] Keys =
    [
        "RgCoverOffsetHelp", "RgOffsetStepHelp", "RgFillArcRHelp",
        "RgEdgesTableHelp", "RgBarsTableHelp",
        "RgNumberHeader", "RgEdgeOffsetHeader", "RgEdgeActionsHeader",
        "RgBarXHeader", "RgBarYHeader", "RgBarDiameterHeader", "RgBarDeleteHeader",
        "RgIncreaseOffsetSymbol", "RgDecreaseOffsetSymbol", "RgDeleteBarSymbol",
        "RgIncreaseOffsetToolTip", "RgDecreaseOffsetToolTip", "RgDeleteBarToolTip",
        "RgTranslate", "RgProperties"
    ];

    [Theory]
    [InlineData("Resources/Strings.ru-RU.xaml")]
    [InlineData("Resources/Strings.en-US.xaml")]
    public void Dictionary_ContainsAllRebarGroupEditorKeys(string relativePath)
    {
        var doc = XDocument.Load(relativePath);
        var present = doc.Descendants()
            .Select(e => e.Attributes().FirstOrDefault(a => a.Name.LocalName == "Key")?.Value)
            .Where(value => value != null)
            .ToHashSet()!;

        foreach (var key in Keys)
            Assert.True(present.Contains(key), $"Нет ключа {key} в {relativePath}");
    }

    [Theory]
    [InlineData("Resources/Strings.ru-RU.xaml", "RgCoverOffset", "Защитный слой, мм")]
    [InlineData("Resources/Strings.ru-RU.xaml", "RgOffsetStep", "Шаг изменения, мм")]
    [InlineData("Resources/Strings.ru-RU.xaml", "RgFillArcR", "Радиус дуги, мм")]
    [InlineData("Resources/Strings.ru-RU.xaml", "RgEdgesTable", "Линия защитного слоя")]
    [InlineData("Resources/Strings.en-US.xaml", "RgCoverOffset", "Cover, mm")]
    [InlineData("Resources/Strings.en-US.xaml", "RgOffsetStep", "Offset step, mm")]
    [InlineData("Resources/Strings.en-US.xaml", "RgFillArcR", "Arc radius, mm")]
    [InlineData("Resources/Strings.en-US.xaml", "RgEdgesTable", "Cover line")]
    public void Dictionary_UsesExpectedRebarGroupEditorLabels(
        string relativePath, string key, string expectedValue)
    {
        var doc = XDocument.Load(relativePath);
        var element = doc.Descendants()
            .Single(e => e.Attributes().FirstOrDefault(a => a.Name.LocalName == "Key")?.Value == key);

        Assert.Equal(expectedValue, element.Value);
    }
}
