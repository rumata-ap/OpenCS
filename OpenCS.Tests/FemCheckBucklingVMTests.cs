using CScore.Fem;
using CScore.Sp16;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Блок «Продольный изгиб» проверки по КЭ: общий для диалога проверки и страницы группы.</summary>
public sealed class FemCheckBucklingVMTests
{
    [Fact]
    public void RcCheck_ApplyChangesEtaOnly_KeepsRebarSources()
    {
        var check = new FemCheck
        {
            NormCode = "rc_check",
            ParamsJson = new BarCheckParams
            {
                RebarSources = [FemCheckRebarSource.Assigned],
                Eta = new FemEtaParams { Enabled = true, MuX = 0.7 },
            }.ToJson(),
        };
        var vm = new FemCheckBucklingVM();
        vm.Load(check);
        Assert.True(vm.EtaEnabled);
        Assert.Equal("0.7", vm.EtaMuX);

        vm.EtaMuX = "2";
        vm.Apply(check);

        var p = BarCheckParams.Parse(check.ParamsJson);
        Assert.Equal(2, p.Eta!.MuX);
        Assert.Equal([FemCheckRebarSource.Assigned], p.RebarSources);
    }

    [Fact]
    public void SteelCheck_RoundTrip()
    {
        var check = new FemCheck { NormCode = "steel_check" };
        var vm = new FemCheckBucklingVM();
        vm.Load(check);
        Assert.False(vm.SteelMeshLef);
        Assert.Equal(System.Windows.Visibility.Visible, vm.SteelVisibility);
        Assert.Equal(System.Windows.Visibility.Collapsed, vm.EtaVisibility);

        vm.SteelMeshLef = true;
        vm.SteelMuX = "2,5";
        vm.SteelMuB = "1";
        vm.Apply(check);

        var p = SteelFemCheckParams.TryParse(check.ParamsJson)!;
        Assert.Equal(new SteelMeshLef { MuX = 2.5, MuY = 1, MuB = 1 }, p.MeshLef);

        var again = new FemCheckBucklingVM();
        again.Load(check);
        Assert.True(again.SteelMeshLef);
        Assert.Equal("2.5", again.SteelMuX);
    }

    [Fact]
    public void SteelCheck_AutoLef_RoundTrip_KeepsAnalysis()
    {
        var analyses = new[] { new FemAnalysis { Id = 4, Tag = "Линейный" }, new FemAnalysis { Id = 7, Tag = "Пакет" } };
        var check = new FemCheck { NormCode = "steel_check" };
        var vm = new FemCheckBucklingVM();
        vm.SetAnalyses(analyses);
        vm.Load(check);
        Assert.False(vm.SteelAutoLef);
        Assert.Equal(4, vm.SteelAutoAnalysis!.Id);                     // по умолчанию — первая постановка

        vm.SteelAutoLef = true;
        vm.SteelAutoAnalysis = analyses[1];
        vm.Apply(check);
        Assert.Equal(7, SteelFemCheckParams.TryParse(check.ParamsJson)!.AutoLef!.AnalysisId);

        var again = new FemCheckBucklingVM();
        again.SetAnalyses(analyses);
        again.Load(check);
        Assert.True(again.SteelAutoLef);
        Assert.Equal(7, again.SteelAutoAnalysis!.Id);

        // Постановки после загрузки проверки (страница: сначала проверка, затем схема) — выбор сохраняется.
        var late = new FemCheckBucklingVM();
        late.Load(check);
        late.SetAnalyses(analyses);
        Assert.Equal(7, late.SteelAutoAnalysis!.Id);

        again.SteelAutoLef = false;
        again.Apply(check);
        Assert.Null(SteelFemCheckParams.TryParse(check.ParamsJson)!.AutoLef);
    }

    [Fact]
    public void SlsOrUnknown_NotApplicable()
    {
        var vm = new FemCheckBucklingVM { NormCode = "other" };
        Assert.False(vm.Applies);
        Assert.Equal(System.Windows.Visibility.Collapsed, vm.Visibility);
    }
}
