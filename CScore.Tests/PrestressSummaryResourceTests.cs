using Xunit;

namespace CScore.Tests;

/// <summary>
/// Блок «Преднапряжение» в сводке НДС показывает фактическое действие и предупреждение
/// о превышении расчётного сопротивления — все подписи обязаны быть в обоих словарях.
/// </summary>
public class PrestressSummaryResourceTests
{
    /// <summary>Ключи, которые обязаны быть в обоих словарях.</summary>
    static readonly string[] RequiredKeys =
    [
        "ResultPrestressActual",
        "ResultPrestressSigActual",
        "ResultPrestressAboveStrength",
    ];

    /// <summary>Ключи, на которые ссылается сама разметка (текст предупреждения
    /// форматируется во вьюмодели через Loc.S, поэтому в XAML его нет).</summary>
    static readonly string[] MarkupKeys =
    [
        "ResultPrestressActual",
        "ResultPrestressSigActual",
    ];

    [Theory]
    [InlineData("Strings.ru-RU.xaml")]
    [InlineData("Strings.en-US.xaml")]
    public void StringDictionary_ContainsPrestressActualKeys(string dictionary)
    {
        string xaml = File.ReadAllText(Find(Path.Combine("OpenCS", "Resources", dictionary)));

        foreach (string key in RequiredKeys)
            Assert.Contains($"x:Key=\"{key}\"", xaml);
    }

    [Fact]
    public void SummaryBody_UsesPrestressActualResources()
    {
        string xaml = File.ReadAllText(Find(Path.Combine("OpenCS", "Views", "StrainSummaryBody.xaml")));

        foreach (string key in MarkupKeys)
            Assert.Contains($"DynamicResource {key}", xaml);
    }

    static string Find(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Не найден файл {relativePath}");
    }
}
