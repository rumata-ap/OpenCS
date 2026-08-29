using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Разбор результата задачи наклонных сечений во ViewModel отчёта.</summary>
public sealed class ShearInclinedResultVMTests
{
    const string Json = """
    {
      "sectionTag": "Б-1",
      "forceLabel": "оп. A",
      "calcType": "C",
      "elementKind": "bending_unstressed",
      "forceSource": "constant",
      "direction": -1,
      "inputs": { "vy": { "b": 0.3, "h0": 0.55, "qsw": 291.6, "sw": 0.15, "ns": 535.9,
                          "rb": 14500, "rbt": 1050, "autoB": 0.3, "autoH0": 0.55, "autoNs": 535.9 } },
      "profile": { "vy": { "kind": "constant", "q0": 150.0, "m0": -120.0, "n0": 0.0, "supportDistance": 0.0 } },
      "details": [
        { "plane": "vy", "formula": "8.55", "description": "Полоса", "normRef": "п. 8.1.32",
          "applied": 150.0, "allowable": 717.75, "ratio": 0.209, "status": "ok", "passed": true,
          "variables": { "s": 0.0, "b": 0.3, "h0": 0.55 } },
        { "plane": "vy", "formula": "8.56", "description": "Наклонное сечение", "normRef": "п. 8.1.33",
          "applied": 150.0, "allowable": 260.0, "ratio": 0.577, "status": "ok", "passed": true,
          "variables": { "s": 0.0, "C": 0.72, "Qb": 140.0, "Qsw": 120.0 } },
        { "plane": "vy", "formula": "8.63", "description": "Момент", "normRef": "п. 8.1.35",
          "applied": 120.0, "allowable": 300.0, "ratio": 0.4, "status": "ok", "passed": true,
          "variables": { "s": 0.0, "C": 1.1 } }
      ],
      "stations": [
        { "plane": "vy", "s": 0.0, "n": 0.0, "phiN": 1.0, "tensionOnPositiveSide": false,
          "q": 150.0, "cCrit": 0.72, "qb": 140.0, "qsw": 120.0, "eta": 0.577,
          "mApplied": 120.0, "cCritMoment": 1.1, "ms": 265.0, "msw": 35.0, "etaM": 0.4 }
      ],
      "warnings": [ "Конструктивные требования раздела 10.3 не подтверждены." ],
      "utilization": 0.577,
      "utilizationStatus": "ok",
      "utilizationExact": 0.577
    }
    """;

    const string BothPlanesJson = """
    {
      "sectionTag": "Б-1",
      "forceLabel": "оп. A",
      "calcType": "C",
      "elementKind": "bending_unstressed",
      "forceSource": "constant",
      "direction": 0,
      "inputs": {
        "vy": { "b": 0.3, "h0": 0.55, "qsw": 291.6, "sw": 0.15, "ns": 535.9,
                "rb": 14500, "rbt": 1050, "autoB": 0.3, "autoH0": 0.55 },
        "vx": { "b": 0.5, "h0": 0.35, "qsw": 291.6, "sw": 0.10, "ns": 535.9,
                "rb": 14500, "rbt": 1050, "autoB": 0.5, "autoH0": 0.35 }
      },
      "profile": {
        "vy": { "kind": "constant", "q0": 150.0, "m0": -120.0, "n0": 0.0, "supportDistance": 0.0 },
        "vx": { "kind": "constant", "q0": 60.0, "m0": 0.0, "n0": 0.0, "supportDistance": 0.0 }
      },
      "details": [],
      "stations": [
        { "plane": "vy", "s": 0.0, "n": 0.0, "phiN": 1.0,
          "tensionOnPositiveSide": false, "q": 150.0,
          "cCrit": 0.70, "qb": 130.0, "qsw": 100.0, "eta": 0.40,
          "mApplied": 50.0, "cCritMoment": 0.55, "ms": 100.0, "msw": 20.0, "etaM": 0.2 },
        { "plane": "vy", "s": 1.0, "n": 0.0, "phiN": 1.0,
          "tensionOnPositiveSide": false, "q": 160.0,
          "cCrit": 0.72, "qb": 130.0, "qsw": 100.0, "eta": 0.80,
          "mApplied": 50.0, "cCritMoment": 0.55, "ms": 100.0, "msw": 20.0, "etaM": 0.2 },
        { "plane": "vx", "s": 0.0, "n": 0.0, "phiN": 1.0,
          "tensionOnPositiveSide": false, "q": 60.0,
          "cCrit": 0.44, "qb": 80.0, "qsw": 0.0, "eta": 0.60,
          "mApplied": 0.0, "cCritMoment": 0.22, "ms": 80.0, "msw": 0.0, "etaM": 0.1 }
      ],
      "warnings": [],
      "utilization": 0.80,
      "utilizationStatus": "ok",
      "utilizationExact": 0.80
    }
    """;

    [Fact]
    public void Constructor_ParsesHeaderAndUtilization()
    {
        var vm = new ShearInclinedResultVM(Json);

        Assert.Equal("Б-1", vm.SectionTag);
        Assert.Equal(0.577, vm.Utilization, 6);
        Assert.Equal(0.577, vm.UtilizationExact, 6);
    }

    [Fact]
    public void BuildProjectionCharts_SelectsWorstStationIndependentlyForVyAndVx()
    {
        var vm = new ShearInclinedResultVM(BothPlanesJson);

        var charts = vm.BuildProjectionCharts();

        Assert.Equal(new[] { "vy", "vx" }, charts.Select(c => c.Plane));
        Assert.Equal(0.80, charts[0].Station.Eta, 6);
        Assert.Equal(0.60, charts[1].Station.Eta, 6);
        Assert.NotEmpty(charts[0].Curve);
        Assert.NotEmpty(charts[1].Curve);
    }

    [Fact]
    public void Constructor_GroupsDetailsByNormClause()
    {
        var vm = new ShearInclinedResultVM(Json);

        Assert.Equal(3, vm.Groups.Count);
        Assert.Equal("8.55", vm.Groups[0].Items[0].Formula);
        Assert.Equal("8.56", vm.Groups[1].Items[0].Formula);
        Assert.Equal("8.63", vm.Groups[2].Items[0].Formula);
    }

    [Fact]
    public void Constructor_ExposesCautions()
    {
        var vm = new ShearInclinedResultVM(Json);

        Assert.Contains(vm.Cautions, c => c.Contains("10.3"));
    }

    [Fact]
    public void Constructor_ExposesStations()
    {
        var vm = new ShearInclinedResultVM(Json);

        Assert.Single(vm.Stations);
        Assert.Equal(0.72, vm.Stations[0].CriticalC, 6);
        Assert.Equal(0.577, vm.Stations[0].Eta, 6);
        Assert.Equal(1.1, vm.Stations[0].CriticalCMoment, 6);
        Assert.Equal(120.0, vm.Stations[0].MomentApplied, 6);
        Assert.False(vm.Stations[0].TensionOnPositiveSide);
    }

    [Fact]
    public void Constructor_NullRatioIsRenderedAsFailure()
    {
        // null = нулевая несущая способность, а не «нет значения»
        string json = Json.Replace("\"ratio\": 0.577", "\"ratio\": null");

        var vm = new ShearInclinedResultVM(json);
        var item = vm.Groups.SelectMany(g => g.Items).First(i => i.Formula == "8.56");

        Assert.False(item.Passed);
        Assert.Equal("∞", item.RatioText);
    }

    [Fact]
    public void Constructor_NullUtilization_IsShownAsFailure()
    {
        string json = Json.Replace("\"utilization\": 0.577", "\"utilization\": null");

        var vm = new ShearInclinedResultVM(json);

        Assert.True(double.IsPositiveInfinity(vm.Utilization));
        Assert.Contains(Loc.S("ShearInclinedFailed"), vm.VerdictText);
    }

    [Fact]
    public void Constructor_SkippedStationChecks_AreExposedAsNaN()
    {
        // Стоянка ближе h0 к опоре: (8.56) не выполнялась
        string json = Json.Replace(
            "\"cCrit\": 0.72, \"qb\": 140.0, \"qsw\": 120.0, \"eta\": 0.577",
            "\"cCrit\": null, \"qb\": null, \"qsw\": null, \"eta\": null");

        var vm = new ShearInclinedResultVM(json);

        Assert.True(double.IsNaN(vm.Stations[0].Eta));
        Assert.True(double.IsNaN(vm.Stations[0].CriticalC));
    }

    [Fact]
    public void BuildProjectionCurve_UsesStoredProfileNotConstantSubstitute()
    {
        // Профиль переменный: Q меняется вдоль наклонного сечения, поэтому точки кривой
        // обязаны отличаться от построенных по постоянному Q = 150 кН.
        string json = Json.Replace(
            @"{ ""kind"": ""constant"", ""q0"": 150.0, ""m0"": -120.0, ""n0"": 0.0, ""supportDistance"": 0.0 }",
            @"{ ""kind"": ""uniform_load"", ""q0"": 300.0, ""m0"": 0.0, ""n0"": 0.0, ""load"": 40.0, "
            + @"""supportDistance"": 5.0, ""supportAtStart"": false, ""supportAtEnd"": true }");

        var vm = new ShearInclinedResultVM(json);
        var curve = vm.BuildProjectionCurve(vm.Stations[0]);

        Assert.NotEmpty(curve);
        Assert.Contains(curve, p => Math.Abs(p.Q - 150.0) > 1.0);
    }

    [Fact]
    public void Constructor_ErrorJson_DoesNotThrow()
    {
        var vm = new ShearInclinedResultVM("""{ "error": "нет бетонной области" }""");

        Assert.Empty(vm.Groups);
        Assert.Contains("нет бетонной области", vm.VerdictText);
    }
}
