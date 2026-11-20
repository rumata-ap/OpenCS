using System.Threading;
using System.Windows;
using CScore;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Подписи типа материала и влажности в списках: словарь шаблонов грузится (x:Static по значениям перечислений),
/// и для каждого значения есть строка в обоих языках.
/// </summary>
[Collection("Loc language")]
public sealed class EnumItemTemplatesTests
{
    [Fact]
    public void Dictionary_Loads_WithBothTemplates()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var uri = new Uri("/OpenCS;component/Resources/EnumItemTemplates.xaml", UriKind.Relative);
                var dict = (ResourceDictionary)Application.LoadComponent(uri);
                Assert.IsType<DataTemplate>(dict["MatTypeItem"]);
                Assert.IsType<DataTemplate>(dict["DampnessItem"]);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(error);
    }

    [Theory]
    [InlineData("ru-RU")]
    [InlineData("en-US")]
    public void EveryValue_HasText(string language)
    {
        try
        {
            Loc.SetLanguage(language);
            foreach (var type in Enum.GetValues<MatType>())
                Assert.NotEqual($"MatType_{type}", Loc.S($"MatType_{type}"));
            foreach (var key in new[] { "HumidityBelow40", "Humidity40_75", "HumidityAbove75", "HumidityAny" })
                Assert.NotEqual(key, Loc.S(key));
        }
        finally
        {
            Loc.SetLanguage(null);
        }
    }
}
