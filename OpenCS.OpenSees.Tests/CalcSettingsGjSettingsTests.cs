using System.Text.Json;
using OpenCS.Utilites;

namespace OpenCS.OpenSees.Tests;

public sealed class CalcSettingsGjSettingsTests
{
    [Fact]
    public void NewSettingsHaveSafeGjDefaults()
    {
        var settings = new CalcSettings();

        Assert.Equal(1e7, settings.OpenSeesDefaultGjKnm2);
        Assert.True(settings.OpenSeesAutoGjFromSection);
    }

    [Fact]
    public void CloneCopiesGjSettings()
    {
        var settings = new CalcSettings
        {
            OpenSeesDefaultGjKnm2 = 123.45,
            OpenSeesAutoGjFromSection = false
        };

        var clone = settings.Clone();

        Assert.Equal(123.45, clone.OpenSeesDefaultGjKnm2);
        Assert.False(clone.OpenSeesAutoGjFromSection);
    }

    [Fact]
    public void MissingGjPropertiesInOldJsonUseDefaults()
    {
        var settings = JsonSerializer.Deserialize<CalcSettings>("{}")!;

        Assert.Equal(1e7, settings.OpenSeesDefaultGjKnm2);
        Assert.True(settings.OpenSeesAutoGjFromSection);
    }

    [Fact]
    public void GjPropertiesUseStableJsonNames()
    {
        var json = JsonSerializer.Serialize(new CalcSettings());

        Assert.Contains("\"openSeesDefaultGjKnm2\"", json);
        Assert.Contains("\"openSeesAutoGjFromSection\"", json);
    }
}
