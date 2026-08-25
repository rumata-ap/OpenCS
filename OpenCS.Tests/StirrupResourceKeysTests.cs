using System.Xml.Linq;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Все ключи строк хомутов заданы в обоих словарях локализации.</summary>
public sealed class StirrupResourceKeysTests
{
    static readonly string[] Keys =
    [
        "StirrupGroups", "NewStirrupGroup", "StirrupSpacing", "StirrupOffset", "StirrupAnchorArea",
        "StirrupElements", "StirrupAddLoop", "StirrupAddCut", "StirrupDuplicate", "StirrupRebuild",
        "StirrupKind", "StirrupDirectionVertical", "StirrupDirectionHorizontal",
        "StirrupErrorOffsetTooLarge", "StirrupErrorCutOutside", "StirrupErrorHolesUnsupported"
    ];

    [Theory]
    [InlineData("Resources/Strings.ru-RU.xaml")]
    [InlineData("Resources/Strings.en-US.xaml")]
    public void Dictionary_ContainsAllStirrupKeys(string relativePath)
    {
        var doc = XDocument.Load(relativePath);
        var present = doc.Descendants()
            .Select(e => e.Attributes().FirstOrDefault(a => a.Name.LocalName == "Key")?.Value)
            .Where(value => value != null)
            .ToHashSet()!;

        foreach (var key in Keys)
            Assert.True(present.Contains(key), $"Нет ключа {key} в {relativePath}");
    }
}
