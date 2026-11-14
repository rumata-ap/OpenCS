using CScore;
using OpenCS.Tasks;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Окно результата продавливания: строки, тоны, определяющий контур, выбор.</summary>
[Collection("Loc language")]
public sealed class PunchingResultVMTests
{
    static PunchingTaskParams Gosha(double f = 600, params PunchingEdgeParams[] edges) => new()
    {
        Area = new PunchingAreaParams { Shape = "rectangle", Bx = 0.25, By = 0.8 },
        Slab = new PunchingSlabParams { H = 0.2, Ax = 0.04, Ay = 0.03, RbtOverride = 1050 },
        Edges = [.. edges],
        Reinforcement = new PunchingReinforcementParams { Enabled = true, Diameter = 0.006, Count = 2, Step = 0.05, RswOverride = 170_000 },
        Forces = new PunchingForceParams { F = f, Mx = 30, My = 20 }
    };

    static (CalcResult Result, CalcTask Task) Run(PunchingTaskParams p)
    {
        var task = new CalcTask { Id = 1, Tag = "Узел 1", Kind = PunchingRunner.Kind, CalcType = CalcType.C, ParamsJson = p.ToJson() };
        return (PunchingRunner.Run(task, CalcSettings.Default, null), task);
    }

    [Fact]
    public void Rows_GoverningSelected_ToneFail()
    {
        Loc.SetLanguage(Loc.Russian);
        try
        {
            var (result, task) = Run(Gosha());
            var vm = new PunchingResultVM(result, task);

            Assert.False(vm.HasError);
            Assert.Equal("Узел 1", vm.Title);
            Assert.NotNull(vm.Scene);
            Assert.Contains(vm.Rows, r => r.Id == "closed");
            var gov = Assert.Single(vm.Rows, r => r.Governs);
            Assert.Equal("pylon_broken", gov.Id);
            Assert.Same(gov, vm.SelectedRow);
            Assert.Equal("pylon_broken", vm.SelectedVariantId);
            Assert.Equal(1.0181060000790736, vm.Utilization, 12);
            Assert.Equal(BatchVerdictTone.Fail, vm.Tone);
            Assert.Equal(BatchVerdictTone.Fail, gov.Reason.Tone);
            Assert.Equal(BatchVerdictTone.Ok, vm.Rows.Single(r => r.Id == "closed").Reason.Tone);
            // Кисп округляется вверх, как в отчёте: 1,01811 → 1,019; 0,92726 → 0,928 (ручная проверка 14.11).
            Assert.Contains("1,019", vm.VerdictText);
            Assert.Equal("1,019", gov.K);
            Assert.Equal("0,928", vm.Rows.Single(r => r.Id == "closed").K);
            Assert.Contains("НЕ обеспечена", vm.VerdictText);
            Assert.StartsWith("Замкнутый контур", vm.Rows.Single(r => r.Id == "closed").Title);
            Assert.Equal("учтена", gov.Sw);
            Assert.Contains("250×800", vm.InputsSummary);
            Assert.Contains("h0 = 165", vm.InputsSummary);
        }
        finally { Loc.SetLanguage(null); }
    }

    [Fact]
    public void Warnings_LocalizedByCode_WithVariantTitles()
    {
        // Большие моменты — вклад kM ограничивается 0,5·kF (предупреждение с id контура в аргументах).
        var (result, task) = Run(Gosha() with { Forces = new PunchingForceParams { F = 600, Mx = 300, My = 200 } });
        Loc.SetLanguage(Loc.English);
        try
        {
            var vm = new PunchingResultVM(result, task);
            Assert.Contains(vm.Warnings, w => w.StartsWith("Detailing requirements of cl. 10.3", StringComparison.Ordinal));
            Assert.Contains(vm.Warnings, w => w == "Perimeter \"Broken perimeter at pylon ends (SP 430)\": the moment contribution "
                + "is limited to half of the force contribution (cl. 8.1.46).");
            Assert.DoesNotContain(vm.Warnings, w => w.Any(c => c is >= 'А' and <= 'я'));
        }
        finally { Loc.SetLanguage(null); }
    }

    [Fact]
    public void Warnings_OldResultWithoutArgs_FallsBackToCoreText()
    {
        var data = OpenCS.Reporting.PunchingReportData.Parse(
            """{"passed":true,"ratio":0.5,"details":[],"warnings":[{"code":"m_limited_half_kf","text":"Контур «closed»: вклад моментов ограничен."}]}""");
        Loc.SetLanguage(Loc.English);
        try
        {
            Assert.Equal("Контур «closed»: вклад моментов ограничен.",
                OpenCS.Views.Helpers.Variants.Warning(data.Warnings[0], data.Variants, 0));
        }
        finally { Loc.SetLanguage(null); }
    }

    [Fact]
    public void Selection_RowAndVariantIdStayInSync()
    {
        var (result, task) = Run(Gosha());
        var vm = new PunchingResultVM(result, task);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.SelectedVariantId = "closed";
        Assert.Equal("closed", vm.SelectedRow?.Id);
        Assert.Contains(nameof(PunchingResultVM.SelectedRow), changed);

        vm.SelectedRow = vm.Rows.Single(r => r.Id == "pylon_broken");
        Assert.Equal("pylon_broken", vm.SelectedVariantId);
    }

    [Fact]
    public void Tone_OkAndNearLimit()
    {
        // Кисп линеен по F при прочих равных лишь приближённо — подбираем F так, чтобы Кисп попал в нужный диапазон.
        var ok = new PunchingResultVM(Run(Gosha(f: 300)).Result, null);
        Assert.Equal(BatchVerdictTone.Ok, ok.Tone);
        Assert.True(ok.Utilization < 0.99);

        double lo = 300, hi = 600;
        PunchingResultVM near = ok;
        for (int i = 0; i < 60; i++)
        {
            double mid = 0.5 * (lo + hi);
            near = new PunchingResultVM(Run(Gosha(f: mid)).Result, null);
            if (near.Utilization < 0.995) lo = mid; else hi = mid;
            if (near.Utilization is >= 0.99 and <= 1.0) break;
        }
        Assert.InRange(near.Utilization, 0.99, 1.0);
        Assert.Equal(BatchVerdictTone.NearLimit, near.Tone);
        Assert.True(near.Data.Passed);
    }

    [Fact]
    public void Error_ShowsTextWithoutRows()
    {
        var bad = Gosha() with { Slab = new PunchingSlabParams { H = 0 } };
        var vm = new PunchingResultVM(Run(bad).Result, null);
        Assert.True(vm.HasError);
        Assert.Equal(BatchVerdictTone.Error, vm.Tone);
        Assert.Empty(vm.Rows);
        Assert.Null(vm.Scene);
        Assert.Equal("", vm.StatusText);
    }
}
