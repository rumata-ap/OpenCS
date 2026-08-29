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
    public void Run_ManualRsw_IsCappedAtTable615Maximum()
    {
        var handler = new ShearInclinedHandler();
        var parameters = new ShearInclinedParams
        {
            Planes = "vy",
            ConstructiveRequirements103Confirmed = true,
            OverridesVy = new ShearInclinedOverrides { Rsw = 500_000.0 }
        };

        var result = handler.Run(Task(parameters), ShearInclinedFixtures.Beam(),
            new LoadItem { Vy = 150.0, Mx = -120.0 }, CalcSettings.Default);

        using var doc = JsonDocument.Parse(result.DataJson);
        double qsw = doc.RootElement.GetProperty("inputs").GetProperty("vy")
            .GetProperty("qsw").GetDouble();
        double expected = 300_000.0 * 2.0 * 0.0000503 / 0.15;

        Assert.Equal(expected, qsw, 9);
        Assert.Contains(Warnings(result), w => w.Contains("300 МПа"));
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
    public void Run_StationStepFromCalcSettings_MatchesSameStepSetInTask()
    {
        var handler = new ShearInclinedHandler();
        var item = new LoadItem { Vy = 150.0, Mx = -120.0 };
        var settings = CalcSettings.Default;
        settings.ShearStationStep = 0.5;

        var fromSettings = handler.Run(Task(Uniform() with { StationStep = null }),
            ShearInclinedFixtures.Beam(), item, settings);
        var fromTask = handler.Run(Task(Uniform() with { StationStep = 0.5 }),
            ShearInclinedFixtures.Beam(), item, CalcSettings.Default);

        Assert.Equal("ok", fromSettings.Status);
        Assert.Equal(StationCount(fromTask), StationCount(fromSettings));
    }

    [Fact]
    public void Run_StationStepSetInTask_OverridesCalcSettings()
    {
        // Сохранённые задачи содержат шаг в ParamsJson и должны считаться как прежде,
        // независимо от появившейся позже глобальной настройки.
        var handler = new ShearInclinedHandler();
        var item = new LoadItem { Vy = 150.0, Mx = -120.0 };
        var settings = CalcSettings.Default;
        settings.ShearStationStep = 1.5;

        var result = handler.Run(Task(Uniform() with { StationStep = 0.25 }),
            ShearInclinedFixtures.Beam(), item, settings);
        var reference = handler.Run(Task(Uniform() with { StationStep = 0.25 }),
            ShearInclinedFixtures.Beam(), item, CalcSettings.Default);

        Assert.Equal(StationCount(reference), StationCount(result));
        Assert.True(StationCount(result) > 5);
    }

    [Fact]
    public void Run_AnchorageFactorFromCalcSettings_AppearsInWarning()
    {
        var handler = new ShearInclinedHandler();
        var settings = CalcSettings.Default;
        settings.ShearAnchorageFactor = 0.75;

        var result = handler.Run(Task(new ShearInclinedParams { AnchorageFactor = null }),
            ShearInclinedFixtures.Beam(), new LoadItem { Vy = 150.0, Mx = -120.0 }, settings);

        Assert.Contains(Warnings(result), w => w.Contains("k = 0,75") || w.Contains("k = 0.75"));
    }

    [Fact]
    public void Run_AnchorageFactorSetInTask_OverridesCalcSettings()
    {
        var handler = new ShearInclinedHandler();
        var settings = CalcSettings.Default;
        settings.ShearAnchorageFactor = 0.75;

        var result = handler.Run(Task(new ShearInclinedParams { AnchorageFactor = 1.0 }),
            ShearInclinedFixtures.Beam(), new LoadItem { Vy = 150.0, Mx = -120.0 }, settings);

        Assert.Contains(Warnings(result), w => w.Contains("k = 1,00") || w.Contains("k = 1.00"));
    }

    [Fact]
    public void Parse_LegacyParamsJsonWithExplicitSteps_KeepsValues()
    {
        const string legacy = """
            {"ForceSource":"uniform_load","StationStep":0.4,"ProjectionStep":0.02,
             "MomentZoneLength":1.2,"AnchorageFactor":0.9}
            """;

        var parameters = ShearInclinedParams.Parse(legacy);

        Assert.Equal(0.4, parameters.StationStep);
        Assert.Equal(0.02, parameters.ProjectionStep);
        Assert.Equal(1.2, parameters.MomentZoneLength);
        Assert.Equal(0.9, parameters.AnchorageFactor);
    }

    [Fact]
    public void Parse_ParamsJsonWithoutSteps_LeavesThemUnset()
    {
        var parameters = ShearInclinedParams.Parse("""{"ForceSource":"constant"}""");

        Assert.Null(parameters.StationStep);
        Assert.Null(parameters.ProjectionStep);
        Assert.Null(parameters.MomentZoneLength);
        Assert.Null(parameters.AnchorageFactor);
    }

    [Fact]
    public void ManualForces_SurviveJsonRoundTrip()
    {
        var original = new ShearInclinedParams
        {
            ManualForces = new ShearManualForces
            {
                N = -250.0, Mx = -180.5, My = 12.0, Vy = 145.0, Vx = -30.0
            }
        };

        var restored = ShearInclinedParams.Parse(original.ToJson());

        Assert.NotNull(restored.ManualForces);
        Assert.Equal(-250.0, restored.ManualForces!.N, 12);
        Assert.Equal(-180.5, restored.ManualForces.Mx, 12);
        Assert.Equal(12.0, restored.ManualForces.My, 12);
        Assert.Equal(145.0, restored.ManualForces.Vy, 12);
        Assert.Equal(-30.0, restored.ManualForces.Vx, 12);
    }

    [Fact]
    public void ResolveSingleForces_ManualForces_ReturnsLoadItemWithShear()
    {
        var task = Task(new ShearInclinedParams
        {
            ManualForces = new ShearManualForces { N = -100.0, Mx = -120.0, Vy = 150.0, Vx = 20.0 }
        });

        var item = CalcTaskForceHelper.ResolveSingleForces(task, []);

        Assert.NotNull(item);
        Assert.Equal(150.0, item!.Vy, 12);
        Assert.Equal(20.0, item.Vx, 12);
        Assert.Equal(-120.0, item.Mx, 12);
        Assert.Equal(-100.0, item.N, 12);
    }

    [Fact]
    public void ResolveSingleForces_WithoutManualForces_ReturnsNullSoForceSetIsUsed()
    {
        // Задача, сохранённая до появления ручного ввода, ссылается на строку набора:
        // разрешение усилий должно остаться за набором, а не подсунуть нули.
        var task = Task(new ShearInclinedParams());

        Assert.Null(CalcTaskForceHelper.ResolveSingleForces(task, []));
    }

    [Fact]
    public void ResolveSingleForces_FemProfileWithoutManualForces_ReturnsEmptyItem()
    {
        // При источнике «профиль из FEM» ручные поля скрыты и не сохраняются,
        // но задача всё равно должна запускаться — эпюру строит сам профиль.
        var task = Task(new ShearInclinedParams { ForceSource = "fem_profile" });

        var item = CalcTaskForceHelper.ResolveSingleForces(task, []);

        Assert.NotNull(item);
        Assert.Equal(0.0, item!.Vy, 12);
    }

    [Fact]
    public void UsesManualForces_CoversSingleShearTaskOnly()
    {
        Assert.True(CalcTaskForceHelper.UsesManualForces(
            new CalcTask { Kind = "shear_inclined" }));
        Assert.False(CalcTaskForceHelper.UsesManualForces(
            new CalcTask { Kind = "shear_inclined_batch" }));
    }

    [Fact]
    public void Run_ManualForces_ProducesSameResultAsForceSetRow()
    {
        var handler = new ShearInclinedHandler();
        var parameters = new ShearInclinedParams { ConstructiveRequirements103Confirmed = true };
        var manual = parameters with
        {
            ManualForces = new ShearManualForces { Vy = 150.0, Mx = -120.0 }
        };

        var fromRow = handler.Run(Task(parameters), ShearInclinedFixtures.Beam(),
            new LoadItem { Vy = 150.0, Mx = -120.0 }, CalcSettings.Default);
        var fromManual = handler.Run(Task(manual), ShearInclinedFixtures.Beam(),
            manual.ManualForces!.ToLoadItem(), CalcSettings.Default);

        using var expected = JsonDocument.Parse(fromRow.DataJson);
        using var actual = JsonDocument.Parse(fromManual.DataJson);
        Assert.Equal(expected.RootElement.GetProperty("utilization").GetDouble(),
                     actual.RootElement.GetProperty("utilization").GetDouble(), 12);
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

    /// <summary>Профиль от равномерной нагрузки на участке 3 м — стоянок больше одной.</summary>
    static ShearInclinedParams Uniform() => new()
    {
        ForceSource = "uniform_load",
        DistributedLoad = 40.0,
        DistanceToSupport = 3.0,
        ConstructiveRequirements103Confirmed = true
    };

    static int StationCount(CalcResult result)
    {
        using var doc = JsonDocument.Parse(result.DataJson);
        return doc.RootElement.GetProperty("stations").GetArrayLength();
    }

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
