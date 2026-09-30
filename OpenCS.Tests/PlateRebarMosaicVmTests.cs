using System.Text.Json;
using System.Windows.Media;
using CScore;
using CScore.Fem;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Мозаика по КЭ: импортированные усилия и коэффициент использования из проверки.</summary>
public class PlateRebarMosaicVmTests
{
    static ShellLoadItem Shell(int elem, double mx = 0, double? sigmaX = null) =>
        new() { SourceElementNum = elem, Mx = mx, SigmaX = sigmaX };

    static ForceSet ShellSet(string tag, params ShellLoadItem[] rows) =>
        new() { Tag = tag, Kind = "shell", SourceSchemaId = 1, ShellItems = [.. rows] };

    static ForceSet BarSet(string tag, params (int Elem, double N)[] rows) =>
        new() { Tag = tag, Kind = "bar", SourceSchemaId = 1, Items = [.. rows.Select(r => new LoadItem { SourceElementNum = r.Elem, N = r.N })] };

    static PlateRebarMosaicVM.Data Data(
        IReadOnlyList<ForceSet>? sets = null, IReadOnlyList<PlateRebarMosaicVM.CheckInfo>? checks = null,
        Dictionary<string, double>? thickness = null) =>
        new(null, null, null, null, [])
        {
            ForceSets = sets ?? [],
            Checks = checks ?? [],
            ThicknessByTag = thickness ?? [],
        };

    static void Select(PlateRebarMosaicVM vm, PlateRebarMosaicSourceKind kind) =>
        vm.SelectedSource = vm.SourceOptions.Single(o => o.Kind == kind);

    static void SelectComponent(PlateRebarMosaicVM vm, object component) =>
        vm.SelectedComponent = vm.ComponentOptions.Single(o => Equals(o.Component, component));

    [Fact]
    public void Forces_ShellSet_ColorsElementsBySign()
    {
        var vm = new PlateRebarMosaicVM();
        vm.Apply(Data([ShellSet("Собственный вес", Shell(1, mx: 40), Shell(2, mx: -40), Shell(2, mx: 5))]));
        Select(vm, PlateRebarMosaicSourceKind.Forces);

        Assert.True(vm.HasSubject);
        Assert.True(vm.HasAggregate);
        Assert.Equal("Собственный вес", vm.SelectedSubject!.Label);
        // Напряжений в наборе нет — компонент σ не предлагается.
        Assert.DoesNotContain(vm.ComponentOptions, o => Equals(o.Component, ShellForceComponent.SigmaX));
        SelectComponent(vm, ShellForceComponent.Mx);

        var coloring = vm.Compute(["1", "2", "3"], ["10"])!;

        Assert.False(coloring.Bars);
        Assert.Equal(2, coloring.ColorByTag.Count);           // КЭ 3 — нет данных
        var plus = coloring.ColorByTag["1"];
        var minus = coloring.ColorByTag["2"];                  // наибольшее по модулю: −40
        Assert.True(plus.R > plus.B);
        Assert.True(minus.B > minus.R);
        Assert.Equal(3, vm.Legend.Sum(l => l.Count));          // два значения + «нет данных»

        vm.SetHover("2");
        Assert.Contains("-40", vm.HoverText);
        vm.SetHover(null);
        Assert.Equal("", vm.HoverText);
    }

    [Fact]
    public void Forces_StressSet_MembraneForceUsesElementThickness()
    {
        var vm = new PlateRebarMosaicVM();
        vm.Apply(Data([ShellSet("ЗН 1", Shell(1, sigmaX: 1000), Shell(2, sigmaX: 1000))],
            thickness: new Dictionary<string, double> { ["1"] = 0.25 }));
        Select(vm, PlateRebarMosaicSourceKind.Forces);
        Assert.Contains(vm.ComponentOptions, o => Equals(o.Component, ShellForceComponent.SigmaX));

        SelectComponent(vm, ShellForceComponent.Nx);
        var coloring = vm.Compute(["1", "2"])!;
        Assert.Equal(["1"], coloring.ColorByTag.Keys);         // у КЭ 2 толщина неизвестна
        vm.SetHover("1");
        Assert.Contains("250", vm.HoverText);
    }

    [Fact]
    public void Forces_BarSet_ColorsBars()
    {
        var vm = new PlateRebarMosaicVM();
        vm.Apply(Data([BarSet("РСУ (C)", (10, -300), (11, 120))]));
        Select(vm, PlateRebarMosaicSourceKind.Forces);
        Assert.Equal(BarForceComponent.N, vm.SelectedComponent!.Component);

        var coloring = vm.Compute(["1"], ["10", "11", "12"])!;

        Assert.True(coloring.Bars);
        Assert.Equal(["10", "11"], coloring.ColorByTag.Keys.Order());
    }

    [Fact]
    public void ShellOnlyWithScope_OffersOnlySetsOfItsElements()
    {
        var vm = new PlateRebarMosaicVM { ShellOnly = true, ScopeTags = new HashSet<string> { "1", "2" } };
        vm.Apply(Data([ShellSet("своя плита", Shell(2, mx: 1)), ShellSet("чужая плита", Shell(50, mx: 1)), BarSet("балки", (1, 5))]));
        Select(vm, PlateRebarMosaicSourceKind.Forces);

        Assert.Equal(["своя плита"], vm.SubjectOptions.Select(o => o.Label));
    }

    [Fact]
    public void NoSetsAndChecks_OnlyNoneOption()
    {
        var vm = new PlateRebarMosaicVM();
        vm.Apply(Data());

        Assert.False(vm.HasData);
        Assert.Null(vm.Compute(["1"]));
    }

    static PlateRebarMosaicVM.CheckInfo Check(string normCode, params object[] elements)
    {
        string json = JsonSerializer.Serialize(new { perElement = true, elements });
        return new(new FemCheck { Id = 3, SchemaId = 1, NormCode = normCode, ResultId = 8, Tag = "проверка" }, "ПЛИТА №56 — проверка", () => json);
    }

    [Fact]
    public void Utilization_ColorsByThresholdOneAndMarksUnchecked()
    {
        var vm = new PlateRebarMosaicVM();
        vm.Apply(Data(checks:
        [
            Check("rc_plate_check",
                new { elemTag = "1", rebarSource = "assigned", status = "ok", utilMax = 0.6 },
                new { elemTag = "2", rebarSource = "assigned", status = "failed", utilMax = 1.7 },
                new { elemTag = "3", rebarSource = "assigned", status = "failed", utilMax = (double?)null },
                new { elemTag = "4", rebarSource = "assigned", status = "no_forces", utilMax = (double?)null },
                new { elemTag = "1", rebarSource = "selected", status = "failed", utilMax = 1.2 }),
        ]));
        Select(vm, PlateRebarMosaicSourceKind.Utilization);

        Assert.False(vm.HasAggregate);
        Assert.Equal(["assigned", "selected"], vm.ComponentOptions.Select(o => o.Component));
        SelectComponent(vm, "assigned");

        var coloring = vm.Compute(["1", "2", "3", "4", "5"])!;

        Assert.False(coloring.Bars);
        var passed = coloring.ColorByTag["1"];
        var failed = coloring.ColorByTag["2"];
        Assert.True(passed.G > passed.R);
        Assert.True(failed.R > failed.G);
        Assert.Equal(PlateRebarMosaicVM.FailureColor, coloring.ColorByTag["3"]);
        Assert.Equal(PlateRebarMosaicVM.NotCheckedColor, coloring.ColorByTag["4"]);
        Assert.False(coloring.ColorByTag.ContainsKey("5"));

        SelectComponent(vm, "selected");
        coloring = vm.Compute(["1", "2"])!;
        var other = coloring.ColorByTag["1"];
        Assert.True(other.R > other.G);
        Assert.False(coloring.ColorByTag.ContainsKey("2"));
    }

    [Fact]
    public void Utilization_BarCheck_ColorsBars()
    {
        var vm = new PlateRebarMosaicVM();
        vm.Apply(Data(checks: [Check("rc_check", new { elemTag = "10", rebarSource = "", status = "ok", utilMax = 0.4 })]));
        Select(vm, PlateRebarMosaicSourceKind.Utilization);

        var coloring = vm.Compute(["1"], ["10"])!;

        Assert.True(coloring.Bars);
        Assert.Contains("10", coloring.ColorByTag.Keys);
    }

    [Fact]
    public void Thresholds_AreKeptPerComponent()
    {
        var vm = new PlateRebarMosaicVM();
        vm.Apply(Data([ShellSet("набор", Shell(1, mx: 40))]));
        Select(vm, PlateRebarMosaicSourceKind.Forces);
        SelectComponent(vm, ShellForceComponent.Mx);
        vm.ThresholdsText = "-10; 10";

        SelectComponent(vm, ShellForceComponent.Qx);
        Assert.Equal("", vm.ThresholdsText);

        SelectComponent(vm, ShellForceComponent.Mx);
        Assert.Equal("-10; 10", vm.ThresholdsText);
        vm.Compute(["1"]);
        Assert.Equal(3, vm.Legend.Count);                      // < −10, −10…10, > 10
    }
}
