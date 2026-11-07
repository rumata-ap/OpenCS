using CScore.Combinations;
using CScore.Fem;
using CScore.Fem.Imperfections;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Срез 3 несовершенств (UI): диалог элемента несовершенства (<see cref="FemImperfectionDialogVM"/>) — загрузка и
/// сохранение параметров, валидация, кнопка «по (5.5)», выбор из 3D; дерево загружений с элементами; вид загружения
/// «Несовершенство» в атрибутах СП 20.
/// </summary>
public sealed class FemImperfectionDialogVMTests
{
    static FemImperfection Item(string kind, string json, int id = 5) =>
        new() { Id = id, SchemaId = 3, LoadCaseId = 7, Kind = kind, Tag = "Т", ParamsJson = json, Comment = "к" };

    [Fact]
    public void Sway_RoundTrip_KeepsParams()
    {
        var p = new FemSwayParams
        {
            Scope = FemSwayParams.ScopeList, MemberTags = ["К1", "К2"], ElementTags = ["101", "102"], AngleDeg = 90,
            PhiMode = FemSwayParams.PhiEn1993, HeightM = 12, M = 3,
        };
        var vm = new FemImperfectionDialogVM(Item(FemImperfectionKinds.Sway, p.ToJson()));
        Assert.True(vm.IsSway);
        Assert.False(vm.ScopeAll);
        Assert.Equal("К1, К2", vm.MembersText);
        var result = vm.TryBuild(out var error);
        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal((5, 3, 7, FemImperfectionKinds.Sway, "Т", "к"), (result!.Id, result.SchemaId, result.LoadCaseId, result.Kind, result.Tag, result.Comment));
        var q = FemSwayParams.Parse(result.ParamsJson);
        Assert.Equal(p.MemberTags, q.MemberTags);
        Assert.Equal(p.ElementTags, q.ElementTags);
        Assert.Equal((90.0, FemSwayParams.PhiEn1993, (double?)12, 3, (double?)null), (q.AngleDeg, q.PhiMode, q.HeightM, q.M, q.PhiInv));
    }

    [Fact]
    public void Bow_RoundTrip_KeepsParams()
    {
        var p = new FemBowParams
        {
            MemberTags = ["К1"], ElementChains = [["1", "2", "3"], ["7", "8"]], Plane = FemBowParams.PlaneZ, E0Mode = FemBowParams.E0Table,
            Curve = "c", Plastic = true, Sign = FemBowParams.SignMinus, Activity = FemBowParams.ActivityAlways,
        };
        var vm = new FemImperfectionDialogVM(Item(FemImperfectionKinds.Bow, p.ToJson()));
        Assert.True(vm.IsBow);
        Assert.Equal("1, 2, 3; 7, 8", vm.ChainsText);
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, "FemImperfectionTableValue", 200), vm.TableValueText);
        var q = FemBowParams.Parse(vm.TryBuild(out var error)!.ParamsJson);
        Assert.Null(error);
        Assert.Equal(p.MemberTags, q.MemberTags);
        Assert.Equal(2, q.ElementChains.Count);
        Assert.Equal(p.ElementChains[1], q.ElementChains[1]);
        Assert.Equal((FemBowParams.PlaneZ, FemBowParams.E0Table, "c", true, FemBowParams.SignMinus, FemBowParams.ActivityAlways),
            (q.Plane, q.E0Mode, q.Curve, q.Plastic, q.Sign, q.Activity));
        Assert.Null(q.LOverE0);
        Assert.Null(q.E0Mm);
    }

    [Fact]
    public void Validation_Errors()
    {
        var sway = new FemImperfectionDialogVM(Item(FemImperfectionKinds.Sway, new FemSwayParams().ToJson(), 0));
        sway.ScopeList = true;
        Assert.Null(sway.TryBuild(out var e1));
        Assert.Equal("FemImperfectionScopeEmpty", e1);
        sway.ScopeAll = true;
        sway.PhiManual = true;
        sway.PhiInvText = "0";
        Assert.Null(sway.TryBuild(out var e2));
        Assert.Equal("FemImperfectionBadPhiInv", e2);
        sway.PhiNorm = true;
        sway.MText = "0";
        Assert.Null(sway.TryBuild(out var e3));
        Assert.Equal("FemImperfectionBadM", e3);
        sway.MText = "2";
        sway.HText = "-1";
        Assert.Null(sway.TryBuild(out var e4));
        Assert.Equal("FemImperfectionBadH", e4);
        sway.HText = "";
        sway.AngleText = "x";
        Assert.Null(sway.TryBuild(out var e5));
        Assert.Equal("FemImperfectionBadAngle", e5);

        var bow = new FemImperfectionDialogVM(Item(FemImperfectionKinds.Bow, new FemBowParams().ToJson(), 0));
        Assert.Null(bow.TryBuild(out var b1));
        Assert.Equal("FemImperfectionBowEmpty", b1);
        bow.MembersText = "К1";
        bow.E0Mode = FemBowParams.E0Relative;
        bow.LOverE0Text = "";
        Assert.Null(bow.TryBuild(out var b2));
        Assert.Equal("FemImperfectionBadLOverE0", b2);
        bow.E0Mode = FemBowParams.E0Absolute;
        bow.E0MmText = "-5";
        Assert.Null(bow.TryBuild(out var b3));
        Assert.Equal("FemImperfectionBadE0Mm", b3);
        bow.E0MmText = "12";
        Assert.Equal(12, FemBowParams.Parse(bow.TryBuild(out _)!.ParamsJson).E0Mm);
    }

    [Fact]
    public void ComputePhi_ByH_BySpan_FillsManual()
    {
        FemSwayParams? asked = null;
        var vm = new FemImperfectionDialogVM(Item(FemImperfectionKinds.Sway, new FemSwayParams { PhiMode = FemSwayParams.PhiEn1993 }.ToJson()),
            spanZ: p => { asked = p; return 9; });
        // h = 9: α_h = 2/3, m = 1: α_m = 1 → φ = 1/300.
        Assert.True(vm.ComputePhi());
        Assert.NotNull(asked);
        Assert.Contains("1/300", vm.PhiDescription);
        vm.HText = "4";
        vm.MText = "2";
        vm.PhiManual = true;
        // h = 4: α_h = 1; m = 2: α_m = √0,75 → φ = 1/(200/0,866) = 1/230,9.
        Assert.True(vm.ComputePhi());
        Assert.Equal((1 / CScore.En1993.En1993Imperfections.Phi(4, 2).Phi).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture), vm.PhiInvText);
        var noSpan = new FemImperfectionDialogVM(Item(FemImperfectionKinds.Sway, new FemSwayParams().ToJson()), spanZ: _ => null);
        Assert.False(noSpan.ComputePhi());
        Assert.Equal("FemImperfectionHUnknown", noSpan.PhiDescription);
    }

    [Fact]
    public void TakeSelection_FillsListsAndChain()
    {
        (IReadOnlyCollection<string>, IReadOnlyCollection<string>) Sel() => (["К1"], ["11", "12"]);
        var sway = new FemImperfectionDialogVM(Item(FemImperfectionKinds.Sway, new FemSwayParams().ToJson()), selection: Sel);
        Assert.True(sway.CanTakeSelection);
        sway.TakeSelectionCommand.Execute(null);
        Assert.False(sway.ScopeAll);
        Assert.Equal(("К1", "11, 12"), (sway.MembersText, sway.ElementsText));
        var bow = new FemImperfectionDialogVM(Item(FemImperfectionKinds.Bow, new FemBowParams { E0Mode = FemBowParams.E0Table }.ToJson()),
            selection: Sel);
        bow.TakeSelectionCommand.Execute(null);
        var q = FemBowParams.Parse(bow.TryBuild(out _)!.ParamsJson);
        Assert.Equal(["К1"], q.MemberTags);
        Assert.Equal(["11", "12"], Assert.Single(q.ElementChains));
        Assert.False(new FemImperfectionDialogVM(Item(FemImperfectionKinds.Bow, "{}")).CanTakeSelection);
    }

    [Fact]
    public void KindSwitch_And_Tags()
    {
        var vm = new FemImperfectionDialogVM(Item(FemImperfectionKinds.Sway, new FemSwayParams().ToJson()));
        vm.IsBow = true;
        vm.MembersText = "A;B  A";
        var result = vm.TryBuild(out _)!;
        Assert.Equal(FemImperfectionKinds.Bow, result.Kind);
        Assert.Equal(["A", "B"], FemBowParams.Parse(result.ParamsJson).MemberTags);
        Assert.Equal(["3", "4", "5", "К-1"], FemImperfectionDialogVM.ExpandTags(["3-5", "4", "К-1"]));
    }

    [Fact]
    public void Tree_ImperfectionCase_HasItems_UnsavedHasNone()
    {
        var schema = new FemSchema { Id = 3, SourceType = "internal" };
        var cases = new List<FemLoadCase>
        {
            new() { Id = 7, SchemaId = 3, Tag = "Imp", Sp20Type = FemSp20Types.Imperfection },
            new() { Id = 8, SchemaId = 3, Tag = "P", Sp20Type = FemSp20Types.ShortTerm },
            new() { Id = 0, SchemaId = 3, Tag = "New", Sp20Type = FemSp20Types.Imperfection },
        };
        var items = new List<FemImperfection>
        {
            Item(FemImperfectionKinds.Sway, new FemSwayParams().ToJson(), 1),
            Item(FemImperfectionKinds.Bow, new FemBowParams { MemberTags = ["К1"] }.ToJson(), 2),
            new() { Id = 3, SchemaId = 3, LoadCaseId = 0, Kind = FemImperfectionKinds.Sway },
        };
        var node = new FemLoadCasesSubNode(schema);
        node.Reset(cases, null, items);
        var imp = node.Items[0];
        Assert.True(imp.IsImperfection);
        Assert.Equal(2, imp.Imperfections.Count);
        Assert.Equal("FemImperfectionCaseSummary", imp.ParametersText);
        Assert.Equal(FemImperfectionKinds.Bow, imp.Imperfections[1].Kind);
        Assert.False(node.Items[1].IsImperfection);
        Assert.Empty(node.Items[1].Imperfections);
        Assert.Empty(node.Items[2].Imperfections);
        Assert.Equal("FemImperfectionCaseEmpty", node.Items[2].ParametersText);
    }

    [Fact]
    public void Sp20Attributes_Imperfection_ClearsLoadFields()
    {
        var lc = new FemLoadCase { Tag = "Imp", Sp20Type = FemSp20Types.ShortTerm, GammaFUnfav = 1.2, LongTermShare = 0.3, Sp20Group = "g" };
        var vm = new Sp20LoadAttributesVM(lc, new Sp20GammaDefaults());
        vm.Kind = FemSp20Types.Imperfection;
        Assert.True(vm.IsImperfection);
        Assert.False(vm.IsLoad);
        var values = new FemLoadCase { SelfWeightFactor = 1 };
        Assert.Null(vm.ApplyTo(values));
        Assert.Equal(FemSp20Types.Imperfection, values.Sp20Type);
        Assert.Null(values.Sp20Group);
        Assert.Null(values.GammaFUnfav);
        Assert.Null(values.LongTermShare);
        Assert.Null(values.SelfWeightFactor);
        // Сохранённое загружение-несовершенство открывается с этим видом (не «кратковременное»).
        Assert.Equal(FemSp20Types.Imperfection, new Sp20LoadAttributesVM(values, new Sp20GammaDefaults()).Kind);
        Assert.Contains(FemLoadsTabVM.Sp20Types, t => t.Key == FemSp20Types.Imperfection);
    }
}
