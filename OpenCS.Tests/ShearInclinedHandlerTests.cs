using System.Text.Json;
using CScore;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Обработчик задачи расчёта наклонных сечений.</summary>
public sealed class ShearInclinedHandlerTests
{
    [Fact]
    public void Run_RectangularBeam_ProducesOkResultWithDetails()
    {
        var handler = new ShearInclinedHandler();
        var item = new LoadItem { Vy = 150.0, Mx = -120.0 };

        var result = handler.Run(
            Task(new ShearInclinedParams { ConstructiveRequirements103Confirmed = true }),
            ShearInclinedFixtures.Beam(), item, CalcSettings.Default);

        Assert.Equal("ok", result.Status);
        using var doc = JsonDocument.Parse(result.DataJson);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("utilization").GetDouble() > 0.0);
        Assert.NotEmpty(root.GetProperty("details").EnumerateArray());
        Assert.NotEmpty(root.GetProperty("stations").EnumerateArray());
    }

    [Fact]
    public void Run_WithoutConstructiveConfirmation_AddsWarning()
    {
        var handler = new ShearInclinedHandler();

        var result = handler.Run(Task(new ShearInclinedParams()),
            ShearInclinedFixtures.Beam(), new LoadItem { Vy = 150.0, Mx = -120.0 },
            CalcSettings.Default);

        var warnings = Warnings(result);
        Assert.Contains(warnings, w => w.Contains("10.3"));
        Assert.Contains(warnings, w => w.Contains("анкеровк", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Run_ConfirmedConstructiveRequirements_ReplacesWarning()
    {
        var handler = new ShearInclinedHandler();

        var result = handler.Run(
            Task(new ShearInclinedParams { ConstructiveRequirements103Confirmed = true }),
            ShearInclinedFixtures.Beam(), new LoadItem { Vy = 150.0, Mx = -120.0 },
            CalcSettings.Default);

        Assert.Contains(Warnings(result),
            w => w.Contains("подтвержден", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Run_WithoutConstructiveConfirmation_ExcludesStirrupsFromCapacity()
    {
        var handler = new ShearInclinedHandler();
        var item = new LoadItem { Vy = 150.0, Mx = -120.0 };

        var unconfirmedResult = handler.Run(Task(new ShearInclinedParams()),
            ShearInclinedFixtures.Beam(), item, CalcSettings.Default);
        var confirmedResult = handler.Run(
            Task(new ShearInclinedParams { ConstructiveRequirements103Confirmed = true }),
            ShearInclinedFixtures.Beam(), item, CalcSettings.Default);

        // Хомуты включаются в расчёт только после подтверждения требований 10.3
        Assert.True(ShearCapacity(confirmedResult) > ShearCapacity(unconfirmedResult));

        using var doc = JsonDocument.Parse(unconfirmedResult.DataJson);
        Assert.Equal(0.0, doc.RootElement.GetProperty("inputs").GetProperty("vy")
            .GetProperty("qsw").GetDouble(), 9);
    }

    [Fact]
    public void Run_DataJson_ContainsProfileDescriptionForRebuildingCurves()
    {
        var handler = new ShearInclinedHandler();
        var parameters = new ShearInclinedParams
        {
            ForceSource = "uniform_load",
            DistributedLoad = 30.0,
            DistanceToSupport = 4.0,
            ConstructiveRequirements103Confirmed = true
        };

        var result = handler.Run(Task(parameters), ShearInclinedFixtures.Beam(),
            new LoadItem { Vy = 150.0, Mx = -120.0 }, CalcSettings.Default);

        using var doc = JsonDocument.Parse(result.DataJson);
        var profile = doc.RootElement.GetProperty("profile").GetProperty("vy");
        Assert.Equal("uniform_load", profile.GetProperty("kind").GetString());
        Assert.Equal(30.0, profile.GetProperty("load").GetDouble(), 9);
        Assert.Equal(4.0, profile.GetProperty("supportDistance").GetDouble(), 9);
    }

    [Fact]
    public void Run_ZeroCapacity_WritesNullUtilizationWithStatus()
    {
        var handler = new ShearInclinedHandler();
        // Растягивающая продольная сила при ElementKind = other обнуляет φn
        var parameters = new ShearInclinedParams
        {
            ElementKind = "other",
            ConstructiveRequirements103Confirmed = true
        };

        var result = handler.Run(Task(parameters), ShearInclinedFixtures.Beam(),
            new LoadItem { Vy = 150.0, Mx = -120.0, N = 500_000.0 }, CalcSettings.Default);

        using var doc = JsonDocument.Parse(result.DataJson);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("utilization").ValueKind);
        Assert.Equal("no_capacity", doc.RootElement.GetProperty("utilizationStatus").GetString());
    }

    [Fact]
    public void Run_OnlyVyRequested_ContainsSinglePlaneInputs()
    {
        var handler = new ShearInclinedHandler();

        var result = handler.Run(Task(new ShearInclinedParams { Planes = "vy" }),
            ShearInclinedFixtures.Beam(), new LoadItem { Vy = 150.0, Vx = 90.0, Mx = -120.0 },
            CalcSettings.Default);

        using var doc = JsonDocument.Parse(result.DataJson);
        var inputs = doc.RootElement.GetProperty("inputs");
        Assert.True(inputs.TryGetProperty("vy", out _));
        Assert.False(inputs.TryGetProperty("vx", out _));
    }

    [Fact]
    public void Run_SectionWithoutConcrete_ReturnsError()
    {
        var handler = new ShearInclinedHandler();

        var result = handler.Run(Task(new ShearInclinedParams()), new CrossSection(),
            new LoadItem { Vy = 150.0 }, CalcSettings.Default);

        Assert.Equal("error", result.Status);
    }

    [Fact]
    public void TaskRunner_KnowsBothKinds()
    {
        Assert.Contains("shear_inclined", TaskRunner.KindList);
        Assert.Contains("shear_inclined_batch", TaskRunner.KindList);
    }

    static CalcTask Task(ShearInclinedParams parameters) => new()
    {
        Id = 1,
        Kind = "shear_inclined",
        CalcType = CalcType.C,
        ParamsJson = parameters.ToJson()
    };

    static List<string> Warnings(CalcResult result)
    {
        using var doc = JsonDocument.Parse(result.DataJson);
        return doc.RootElement.GetProperty("warnings").EnumerateArray()
            .Select(w => w.GetString() ?? "").ToList();
    }

    static double ShearCapacity(CalcResult result)
    {
        using var doc = JsonDocument.Parse(result.DataJson);
        return doc.RootElement.GetProperty("details").EnumerateArray()
            .First(d => d.GetProperty("formula").GetString() == "8.56")
            .GetProperty("allowable").GetDouble();
    }
}
