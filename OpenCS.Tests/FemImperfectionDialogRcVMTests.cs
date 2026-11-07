using CScore.Fem.Imperfections;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Срез 6 несовершенств (ЖБ): диалог — выбор нормы (EN 1993 / EN 1992 / СП 63) и её режимы φ и e₀, μ и высота
/// сечения, активность ЖБ «всегда», вид «Погибь стен» (сохранение, предпросмотр ярусов), подписи дерева.
/// </summary>
public sealed class FemImperfectionDialogRcVMTests
{
    static FemImperfection Item(string kind, string json, int id = 5) =>
        new() { Id = id, SchemaId = 3, LoadCaseId = 7, Kind = kind, Tag = "Т", ParamsJson = json };

    [Fact]
    public void Norm_FromParams_AndModeLists()
    {
        var steel = new FemImperfectionDialogVM(Item(FemImperfectionKinds.Bow, new FemBowParams { E0Mode = FemBowParams.E0Table }.ToJson()));
        Assert.Equal(FemImperfectionDialogVM.NormEn1993, steel.Norm);
        Assert.True(steel.ShowActivity);
        var en = new FemImperfectionDialogVM(Item(FemImperfectionKinds.Bow, new FemBowParams { E0Mode = FemBowParams.E0En1992, Mu = 0.7 }.ToJson()));
        Assert.Equal(FemImperfectionDialogVM.NormEn1992, en.Norm);
        Assert.True(en.IsE0Mu);
        Assert.False(en.ShowActivity);
        Assert.Equal("0,7", en.MuText.Replace('.', ','));
        var sp = new FemImperfectionDialogVM(Item(FemImperfectionKinds.Bow, new FemBowParams { E0Mode = FemBowParams.E0Sp63 }.ToJson()));
        Assert.Equal(FemImperfectionDialogVM.NormSp63, sp.Norm);
        Assert.True(sp.IsE0Sp63);
        Assert.Contains(sp.E0ModeChoices, k => k.Key == FemBowParams.E0Sp63);
        Assert.DoesNotContain(sp.E0ModeChoices, k => k.Key == FemBowParams.E0Table);

        // Смена нормы: режим не из списка — первый режим нормы.
        steel.Norm = FemImperfectionDialogVM.NormEn1992;
        Assert.Equal(FemBowParams.E0En1992Simplified, steel.E0Mode);
        steel.Norm = FemImperfectionDialogVM.NormSp63;
        Assert.Equal(FemBowParams.E0Sp63, steel.E0Mode);
        Assert.Equal("СП 63.13330.2018", steel.NormText);
    }

    [Fact]
    public void Bow_Concrete_BuildsAlwaysActive_WithMuAndHeight()
    {
        var vm = new FemImperfectionDialogVM(Item(FemImperfectionKinds.Bow,
            new FemBowParams { MemberTags = ["К1"], E0Mode = FemBowParams.E0Table, Activity = FemBowParams.ActivityEn1993 }.ToJson()));
        vm.Norm = FemImperfectionDialogVM.NormEn1992;
        vm.E0Mode = FemBowParams.E0En1992;
        vm.MuText = "0,7";
        var p = FemBowParams.Parse(vm.TryBuild(out var error)!.ParamsJson);
        Assert.Null(error);
        Assert.Equal((FemBowParams.E0En1992, 0.7, FemBowParams.ActivityAlways), (p.E0Mode, p.Mu!.Value, p.Activity));

        vm.MuText = "0";
        Assert.Null(vm.TryBuild(out error));
        Assert.NotNull(error);

        vm.Norm = FemImperfectionDialogVM.NormSp63;
        vm.SizeMmText = "";
        p = FemBowParams.Parse(vm.TryBuild(out error)!.ParamsJson);
        Assert.Equal((FemBowParams.E0Sp63, (double?)null), (p.E0Mode, p.SectionHeightMm));
        vm.SizeMmText = "450";
        Assert.Equal(450, FemBowParams.Parse(vm.TryBuild(out _)!.ParamsJson).SectionHeightMm);
    }

    [Fact]
    public void Sway_En1992_And_Sp63ManualOnly()
    {
        var vm = new FemImperfectionDialogVM(Item(FemImperfectionKinds.Sway,
            new FemSwayParams { PhiMode = FemSwayParams.PhiEn1992, HeightM = 16, M = 4 }.ToJson()));
        Assert.Equal(FemImperfectionDialogVM.NormEn1992, vm.Norm);
        Assert.True(vm.PhiNorm);
        Assert.True(vm.ComputePhi());
        Assert.Contains("(5.1)", vm.PhiDescription);
        Assert.Equal(FemSwayParams.PhiEn1992, FemSwayParams.Parse(vm.TryBuild(out _)!.ParamsJson).PhiMode);

        vm.Norm = FemImperfectionDialogVM.NormSp63;
        Assert.False(vm.CanPhiNorm);
        Assert.True(vm.PhiManual);
        vm.PhiNorm = true;
        Assert.False(vm.PhiNorm);
        Assert.Equal(FemSwayParams.PhiManual, FemSwayParams.Parse(vm.TryBuild(out _)!.ParamsJson).PhiMode);
    }

    [Fact]
    public void WallBow_RoundTrip_Preview_Summary()
    {
        var p = new FemWallBowParams
        {
            MemberTags = ["W1"], ElementTags = ["10", "11"], E0Mode = FemBowParams.E0Sp63, ThicknessMm = 200, Sign = FemBowParams.SignMinus,
        };
        FemWallBowParams? seen = null;
        var vm = new FemImperfectionDialogVM(Item(FemImperfectionKinds.WallBow, p.ToJson()), wallPreview: q => { seen = q; return "ярусы"; });
        Assert.True(vm.IsWallBow);
        Assert.True(vm.IsBowLike);
        Assert.True(vm.HasElementsList);
        Assert.Equal(FemImperfectionDialogVM.NormSp63, vm.Norm);
        Assert.DoesNotContain(vm.Norms, n => n.Key == FemImperfectionDialogVM.NormEn1993);
        vm.PreviewTiersCommand.Execute(null);
        Assert.Equal("ярусы", vm.TiersText);
        Assert.Equal(200, seen!.ThicknessMm);

        var built = vm.TryBuild(out var error)!;
        Assert.Null(error);
        Assert.Equal(FemImperfectionKinds.WallBow, built.Kind);
        var q = FemWallBowParams.Parse(built.ParamsJson);
        Assert.Equal((p.E0Mode, 200.0, FemBowParams.SignMinus), (q.E0Mode, q.ThicknessMm!.Value, q.Sign));
        Assert.Equal(p.ElementTags, q.ElementTags);

        // Вид «Погибь стен» из погиби стали — норма переключается на ЖБ.
        var bow = new FemImperfectionDialogVM(Item(FemImperfectionKinds.Bow, new FemBowParams { E0Mode = FemBowParams.E0Table }.ToJson()));
        bow.IsWallBow = true;
        Assert.Equal(FemImperfectionDialogVM.NormEn1992, bow.Norm);
        Assert.Equal(FemBowParams.E0En1992Simplified, bow.E0Mode);
        Assert.Null(bow.TryBuild(out error));  // нет стен
        Assert.NotNull(error);

        Assert.Contains("FemImperfectionWallTargetsShort", FemImperfectionText.Summary(built));
        Assert.Contains("FemImperfectionCaseSummaryWalls", FemImperfectionText.CaseSummary([built])!);
    }
}
