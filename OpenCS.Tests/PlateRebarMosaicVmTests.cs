using System.Text.Json;
using System.Windows.Media;
using CScore;
using CScore.Fem;
using CScore.PlateRebar;
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
    public void Foundation_C1_ShownOnlyWhenPresent_AndColorsShells()
    {
        var vm = new PlateRebarMosaicVM();
        vm.Apply(Data());
        Assert.DoesNotContain(vm.SourceOptions, o => o.Kind == PlateRebarMosaicSourceKind.Foundation);

        vm.Apply(Data() with { FoundationC1 = new Dictionary<string, double> { ["1"] = 8494, ["2"] = 24130 } });
        Select(vm, PlateRebarMosaicSourceKind.Foundation);
        Assert.False(vm.HasSubject);
        Assert.True(vm.IsActive);

        var coloring = vm.Compute(["1", "2", "3"], ["10"])!;

        Assert.False(coloring.Bars);
        Assert.Equal(["1", "2"], coloring.ColorByTag.Keys.Order());
        Assert.NotEqual(coloring.ColorByTag["1"], coloring.ColorByTag["2"]);
        Assert.Contains(vm.Legend, l => l.Count == 1 && l.Label == Utilites.Loc.S("PlateRebarMosaicNoData"));
        vm.SetHover("2");
        Assert.EndsWith("24130", vm.HoverText);
    }

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

    sealed class BarRebar : IBarRebarFieldSource
    {
        public bool Supports(BarRebarComponent component) =>
            component is not (BarRebarComponent.Bottom or BarRebarComponent.Top);

        public PlateRebarValue Get(string elemTag, BarRebarComponent component) => elemTag switch
        {
            "10" => PlateRebarValue.Of(component == BarRebarComponent.As1 ? 6.4 : 25.6),
            "11" => PlateRebarValue.Failure(274),
            _ => PlateRebarValue.Missing,
        };

        public IReadOnlyList<PlateRebarValue> GetSections(string elemTag, BarRebarComponent component) => [];
    }

    [Fact]
    public void SelectedBars_ColorsBarsByDesignedReinforcement()
    {
        var vm = new PlateRebarMosaicVM();
        vm.Apply(Data() with { SelectedBars = new BarRebar(), SelectedFile = "1-lin.asp" });
        Select(vm, PlateRebarMosaicSourceKind.SelectedBars);

        Assert.False(vm.HasSubject);
        Assert.Equal(BarRebarComponent.LongitudinalSum, vm.SelectedComponent!.Component);
        Assert.Equal(12, vm.ComponentOptions.Count);

        var coloring = vm.Compute(["1"], ["10", "11", "12"])!;

        Assert.True(coloring.Bars);
        Assert.Equal(["10", "11"], coloring.ColorByTag.Keys.Order());   // КЭ 12 — нет данных
        Assert.Equal(PlateRebarMosaicVM.FailureColor, coloring.ColorByTag["11"]);
        Assert.Contains("1-lin.asp", vm.LegendTitle);
        vm.SetHover("10");
        Assert.Contains("25", vm.HoverText);

        SelectComponent(vm, BarRebarComponent.As1);
        vm.Compute([], ["10"]);
        vm.SetHover("10");
        Assert.Contains("6", vm.HoverText);
    }

    [Fact]
    public void BarProfiles_ForcesBySections_PlaneFollowsComponent()
    {
        var set = new ForceSet
        {
            Tag = "ЗН 1", Kind = "bar", SourceSchemaId = 1,
            Items =
            [
                new LoadItem { SourceElementNum = 10, SourceSectionNum = 1, Mx = -20, My = 5 },
                new LoadItem { SourceElementNum = 10, SourceSectionNum = 2, Mx = 40, My = 7 },
            ],
        };
        var vm = new PlateRebarMosaicVM();
        vm.Apply(Data([set]));
        Assert.False(vm.HasBarDiagrams);
        Select(vm, PlateRebarMosaicSourceKind.Forces);
        SelectComponent(vm, BarForceComponent.Mx);

        Assert.True(vm.HasBarDiagrams);
        var profiles = vm.BarProfiles(["10", "11"], out var plane)!;
        Assert.Equal(BarDiagramPlane.Z1, plane);
        Assert.Equal([(0.0, -20.0), (1.0, 40.0)], Assert.Single(profiles).Value);

        SelectComponent(vm, BarForceComponent.My);
        vm.BarProfiles(["10"], out plane);
        Assert.Equal(BarDiagramPlane.Y1, plane);

        int changes = 0;
        vm.Changed += (_, _) => changes++;
        vm.ShowBarDiagrams = false;
        Assert.Equal(1, changes);
        Assert.Null(vm.BarProfiles(["10"], out _));
    }

    [Fact]
    public void BarProfiles_ShellMosaic_None()
    {
        var vm = new PlateRebarMosaicVM();
        vm.Apply(Data([ShellSet("плита", Shell(1, mx: 40))]));
        Select(vm, PlateRebarMosaicSourceKind.Forces);

        Assert.False(vm.HasBarDiagrams);
        Assert.Null(vm.BarProfiles(["10"], out _));
    }

    /// <summary>Заданная арматура стержней: на КЭ 10 низ 12, верх 8 см².</summary>
    sealed class AssignedBarRebar : IBarRebarFieldSource
    {
        public bool Supports(BarRebarComponent component) =>
            component is BarRebarComponent.LongitudinalSum or BarRebarComponent.Bottom or BarRebarComponent.Top;

        public PlateRebarValue Get(string elemTag, BarRebarComponent component) => elemTag != "10"
            ? PlateRebarValue.Missing
            : PlateRebarValue.Of(component switch { BarRebarComponent.Bottom => 12, BarRebarComponent.Top => 8, _ => 20 });

        public IReadOnlyList<PlateRebarValue> GetSections(string elemTag, BarRebarComponent component) =>
            Get(elemTag, component) is { IsMissing: false } v ? [v] : [];
    }

    [Fact]
    public void AssignedBars_AndDifferenceWithSelected()
    {
        var vm = new PlateRebarMosaicVM();
        vm.Apply(Data() with
        {
            SelectedBars = new BarRebar(), SelectedFile = "1-lin.asp",
            AssignedBars = new AssignedBarRebar(), AssignedFile = "1-lin.RBT",
        });

        Select(vm, PlateRebarMosaicSourceKind.AssignedBars);
        Assert.Equal(
            [BarRebarComponent.LongitudinalSum, BarRebarComponent.Bottom, BarRebarComponent.Top],
            vm.ComponentOptions.Select(o => o.Component));
        SelectComponent(vm, BarRebarComponent.Bottom);
        var coloring = vm.Compute([], ["10", "11"])!;
        Assert.True(coloring.Bars);
        Assert.Equal(["10"], coloring.ColorByTag.Keys);
        vm.SetHover("10");
        Assert.Contains("12", vm.HoverText);
        // Заданное армирование постоянно по длине КЭ.
        Assert.Equal([(0.0, 12.0), (1.0, 12.0)], vm.BarProfiles(["10"], out _)!["10"]);

        // Разность — только по величине, которая есть и в ТЗА, и в подборе: заданная 20 − подобранная 25,6.
        Select(vm, PlateRebarMosaicSourceKind.DifferenceBars);
        Assert.Equal([BarRebarComponent.LongitudinalSum], vm.ComponentOptions.Select(o => o.Component));
        coloring = vm.Compute([], ["10", "11"])!;
        vm.SetHover("10");
        Assert.Contains("-5", vm.HoverText);
        var deficit = coloring.ColorByTag["10"];
        Assert.True(deficit.R > deficit.G);
        // У КЭ 11 подбор не выполнен — отдельным цветом, как в мозаике подобранной.
        Assert.Equal(PlateRebarMosaicVM.FailureColor, coloring.ColorByTag["11"]);
    }

    [Fact]
    public void SelectedBars_NotOfferedInPlateEditor()
    {
        var vm = new PlateRebarMosaicVM { ShellOnly = true };
        vm.Apply(Data() with { SelectedBars = new BarRebar() });

        Assert.DoesNotContain(vm.SourceOptions, o => o.Kind == PlateRebarMosaicSourceKind.SelectedBars);
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
        return new(new FemCheck { Id = 3, SchemaId = 1, NormCode = normCode, ResultId = 8, Tag = "проверка" }, "ПЛИТА №56 — проверка", () => json,
            () => FemCheckElementResults.ParseRows(json));
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
    public void Utilization_BarCheck_ProfilesBySectionAndStepColors()
    {
        string json = JsonSerializer.Serialize(new
        {
            perElement = true,
            elements = new[] { new { elemTag = "10", rebarSource = "selected", status = "failed", utilMax = 1.4 } },
            rows = new[]
            {
                new { elemNum = 10, sectionNum = 1, rebarSource = "selected", utilization = 0.5, passed = true, notChecked = false },
                new { elemNum = 10, sectionNum = 2, rebarSource = "selected", utilization = 1.4, passed = false, notChecked = false },
            },
        });
        var check = new PlateRebarMosaicVM.CheckInfo(
            new FemCheck { Id = 4, SchemaId = 1, NormCode = "rc_check", ResultId = 9, Tag = "проверка" }, "Балки — проверка", () => json,
            () => FemCheckElementResults.ParseRows(json));
        var vm = new PlateRebarMosaicVM();
        vm.Apply(Data(checks: [check]));
        Select(vm, PlateRebarMosaicSourceKind.Utilization);
        vm.Compute([], ["10"]);

        Assert.True(vm.HasBarDiagrams);
        var profiles = vm.BarProfiles(["10", "11"], out _)!;
        Assert.Equal([(0.0, 0.5), (0.5, 0.5), (0.5, 1.4), (1.0, 1.4)], Assert.Single(profiles).Value);
        // Ступени эпюры — цветами шкалы мозаики: проходящее сечение зелёное, непроходящее красное.
        var low = vm.UtilizationColor(0.5)!.Value;
        var high = vm.UtilizationColor(1.4)!.Value;
        Assert.True(low.G > low.R);
        Assert.True(high.R > high.G);

        // Подсказка над стержнем: Кисп КЭ и по сечениям.
        vm.SetHover("10");
        var lines = vm.HoverText.Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.Contains(string.Format(Utilites.Loc.S("MosaicHoverSection"), 2, 1.4.ToString("0.###")), lines[2]);
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
