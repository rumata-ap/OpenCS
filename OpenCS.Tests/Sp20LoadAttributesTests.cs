using CScore.Combinations;
using CScore.Fem;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Атрибуты нагрузки СП 20 загружения (срез 5б): γf п. 7.3 у постоянных, доля длительной части у кратковременных.</summary>
[Collection("FEM schema editor VM")]
public sealed class Sp20LoadAttributesTests(FemSchemaEditorVMFixture fixture)
{
    [Fact]
    public void ShareKind_FillsTypicalShare_FieldStaysEditable()
    {
        var vm = new Sp20LoadAttributesVM(new FemLoadCase { Sp20Type = "short_term" }, Sp20GammaDefaults.Standard);
        Assert.True(vm.IsShortTerm);
        Assert.False(vm.IsPermanent);
        Assert.Null(vm.ShareKindKey);

        vm.ShareKindKey = "snow_cold";
        Assert.Equal(FemLoadsFormat.Number(0.5), vm.ShareText);
        vm.ShareKindKey = "temperature"; // ручной ввод — поле не трогается
        Assert.Equal(FemLoadsFormat.Number(0.5), vm.ShareText);
        vm.ShareText = FemLoadsFormat.Number(0.45);

        var values = new FemLoadCase { GammaFFav = 0.8 };
        Assert.Null(vm.ApplyTo(values));
        Assert.Equal(0.45, values.LongTermShare);
        Assert.Equal(0.8, values.GammaFFav); // у кратковременных γf п. 7.3 не пишется

        vm.ShareText = "1,5";
        Assert.NotNull(vm.ApplyTo(new FemLoadCase()));
    }

    [Fact]
    public void Permanent_ReducedGamma_ShareKindRecognized()
    {
        var vm = new Sp20LoadAttributesVM(new FemLoadCase { Sp20Type = "short_term", LongTermShare = 0.35 }, Sp20GammaDefaults.Standard);
        Assert.Equal("floor", vm.ShareKindKey);

        vm.Kind = "permanent";
        Assert.True(vm.IsPermanent);
        vm.ReducedGammaText = FemLoadsFormat.Number(0.85);
        vm.GammaText = "";
        var values = new FemLoadCase { LongTermShare = 0.35 };
        Assert.Null(vm.ApplyTo(values));
        Assert.Equal(("permanent", (double?)0.85, (double?)null), (values.Sp20Type, values.GammaFFav, values.GammaFUnfav));
        Assert.Equal(0.35, values.LongTermShare); // у постоянных доля не правится

        vm.GammaText = "0";
        Assert.NotNull(vm.ApplyTo(new FemLoadCase()));
    }

    [Fact]
    public void LoadsTab_ShareAndReducedGammaCells_ByKind()
    {
        var editor = new FemSchemaEditorVM(new FemSchema { Id = 1, Tag = "S" }, fixture.App);
        var g = new FemLoadCase { Id = 1, SchemaId = 1, Tag = "G", Sp20Type = "permanent", GammaFFav = 0.9 };
        var q = new FemLoadCase { Id = 2, SchemaId = 1, Tag = "Q", Sp20Type = "short_term" };
        var a = new FemLoadCase { Id = 3, SchemaId = 1, Tag = "A", Sp20Type = "accidental", GammaFFav = 0.8 };
        editor.Session.LoadCases.AddRange([g, q, a]);
        editor.RefreshCollections();
        var tab = new FemLoadsTabVM(editor);
        var rows = tab.LoadCaseRows;

        Assert.Equal([FemLoadsFormat.Number(0.9), "", ""], rows.Select(r => r.GammaFFav));
        Assert.Equal([FemLoadsFormat.Number(1), "", "—"], rows.Select(r => r.LongTermShare));

        rows[1].LongTermShare = FemLoadsFormat.Number(0.35);
        Assert.Equal(0.35, q.LongTermShare);
        rows[1].LongTermShare = "2";
        Assert.Equal(0.35, q.LongTermShare);
        rows[0].LongTermShare = "0,5"; // у постоянных не правится
        Assert.Null(g.LongTermShare);
        rows[2].GammaFFav = "0,7";
        Assert.Equal(0.8, a.GammaFFav);
        editor.UndoCommand.Execute(null);
        Assert.Null(q.LongTermShare);
    }
}
