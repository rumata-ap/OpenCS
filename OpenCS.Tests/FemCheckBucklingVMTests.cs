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

    /// <summary>7б: нераспознанное число не становится умолчанием (1,0) — значение заведомо неверное, его отклоняет служба.</summary>
    [Fact]
    public void BadNumbers_NotSilentDefaults_RejectedByService()
    {
        var vm = new FemCheckBucklingVM { NormCode = "rc_check" };
        vm.EtaEnabled = true;
        vm.EtaMuX = "abc";
        vm.EtaPsiY = "x";
        vm.EtaLength = "";
        var eta = vm.BuildEta()!;
        Assert.Equal((-1.0, -1.0, (double?)null), (eta.MuX, eta.PsiY, eta.LengthM));

        vm.NormCode = "steel_check";
        vm.SteelMeshLef = true;
        vm.SteelMuB = "?";
        vm.SteelAutoLef = true;                                         // постановки нет — Id 0, не «выключено»
        var steel = vm.BuildSteel();
        Assert.Equal((-1.0, 0), (steel.MeshLef!.MuB, steel.AutoLef!.AnalysisId));

        var s = new FemSchema { Id = 1 };
        s.MemberGroups.Add(new FemMemberGroup { Id = 2, SchemaId = 1 });
        var refs = new OpenCS.Services.FemCheckRefs(s, [], [], [], []);
        var check = new FemCheck { SchemaId = 1, MemberId = 2, Tag = "А", NormCode = "steel_check", ParamsJson = steel.ToJson() };
        Assert.Equal(["steel.analysis", "steel.muB"],
            OpenCS.Services.FemCheckAuthoring.Validate(refs, check).Select(e => e.Path).Order());
    }

    /// <summary>У стен в блоке нет μy, ψy и предельной гибкости — правка блока их не сбрасывает.</summary>
    [Fact]
    public void PlateEta_KeepsHiddenFields()
    {
        var check = new FemCheck
        {
            NormCode = "rc_plate_check",
            ParamsJson = new PlateCheckParams
            {
                Eta = new FemEtaParams { Enabled = true, MuY = 2, PsiY = 0.3, SlendernessThreshold = 20 },
            }.ToJson(),
        };
        var vm = new FemCheckBucklingVM();
        vm.Load(check);
        vm.EtaMuX = "0,8";
        vm.Apply(check);
        var e = PlateCheckParams.Parse(check.ParamsJson).Eta!;
        Assert.Equal((0.8, 2.0, 0.3, (double?)20), (e.MuX, e.MuY, e.PsiY, e.SlendernessThreshold));
    }
}
