using CScore.Fem.Fragment.Masks;
using CScore.Fem.Fragment;
using OpenCS.Services;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

public sealed partial class StoreyFragmentMaskServiceTests
{
    /// <summary>
    /// Маски фрагмента с призмой вокруг угловой колонны C1 (−3, −3): отпечаток источника снимается по призме схемы —
    /// сразу после снятия «актуальны», правка сетки родителя вне габарита отбора (узел в (3, 3): его КЭ не заходят в призму + 1 м) их не устаревает,
    /// правка внутри — устаревает.
    /// </summary>
    [Fact]
    public void Masks_Prism_FingerprintByPrism()
    {
        var p = Create(Band with { Prism = FragmentPrism.Create(-2.5, -2.5, 1, 0, 1, 1) });
        using var db = p.Db;
        Assert.NotNull(p.Fragment.FragmentPrismJson);
        var lc = db.GetFemLoadCases(p.Schema.Id);
        var stages = new[] { Single("Пост.", lc[0].Id), Single("Ветер", lc[1].Id) };
        var analysis = SaveResult(db, p.Schema, FemCsfeaRunner.LinearAnalysisKind, stages, stages);

        var extraction = StoreyFragmentMaskService.Extract(db, p.Fragment, FragmentMaskSet.SourceCsfeaLinear, S(analysis.Id));
        Assert.False(extraction.HasErrors, string.Join("; ", extraction.Diagnostics.Select(d => d.Message)));
        Assert.Equal(FragmentMaskStatus.Current, StoreyFragmentMaskService.CheckMasks(db, p.Fragment));
        Assert.Equal(FragmentParentStatus.Unchanged, StoreyFragmentService.CheckParent(db, p.Fragment));

        Exec($"UPDATE fem_mesh_nodes SET z=3.01 WHERE schema_id={p.Schema.Id} AND node_tag='{S(Slab(4, 4))}'");
        Assert.Equal(FragmentMaskStatus.Current, StoreyFragmentMaskService.CheckMasks(db, p.Fragment));

        Exec($"UPDATE fem_mesh_nodes SET z=3.01 WHERE schema_id={p.Schema.Id} AND node_tag='{S(Slab(1, 1))}'");
        Assert.Equal(FragmentMaskStatus.Stale, StoreyFragmentMaskService.CheckMasks(db, p.Fragment));
    }

    /// <summary>
    /// Панель ГУ фрагмента с призмой: есть боковые разрезы, по умолчанию «усилия из маски»; смена пресета боковых граней
    /// пишется в схему и меняет режимы только боковых строк; глифы боковых разрезов — со стороной <see cref="FragmentCutSide.Lateral"/>.
    /// </summary>
    [Fact]
    public void CutBc_Prism_LateralPreset_AndGlyphs()
    {
        var p = Create(Band with { Prism = FragmentPrism.Create(-2.5, -2.5, 1, 0, 1, 1) });
        using var db = p.Db;
        var vm = new FragmentCutBcVM(db, p.Fragment);
        Assert.True(vm.HasLateral);
        Assert.Equal(FragmentBcPreset.Static, vm.SelectedLateralPreset.Preset);
        var lateral = vm.Rows.Where(r => r.Cut.Side == FragmentCutSide.Lateral).ToList();
        Assert.NotEmpty(lateral);
        Assert.All(lateral, r => Assert.Equal(FragmentDofMode.MaskForce, r.Ux.Effective));
        Assert.Contains(vm.Glyphs(), g => g.Side == FragmentCutSide.Lateral && !g.IsBottom);

        string Modes(FragmentCutSide side) => string.Join("|", vm.Rows.Where(r => r.Cut.Side == side)
            .Select(r => string.Join(",", r.Dofs.Select(d => d.Effective))));
        string bottom = Modes(FragmentCutSide.Bottom), top = Modes(FragmentCutSide.Top);
        vm.SelectedLateralPreset = vm.Presets.Single(o => o.Preset == FragmentBcPreset.Kinematic);
        Assert.Equal("Kinematic", db.FemSchemas.Single(s => s.Id == p.Fragment.Id).FragmentLateralBcPreset);
        Assert.All(vm.Rows.Where(r => r.Cut.Side == FragmentCutSide.Lateral), r => Assert.Equal(FragmentDofMode.MaskDisplacement, r.Ux.Effective));
        Assert.Equal((bottom, top), (Modes(FragmentCutSide.Bottom), Modes(FragmentCutSide.Top)));
        Assert.Equal(FragmentBcPreset.Kinematic, new FragmentCutBcVM(db, p.Fragment).SelectedLateralPreset.Preset);

        vm.SelectedLateralPreset = vm.Presets.Single(o => o.Preset == FragmentBcPreset.Static);
        Assert.Null(db.FemSchemas.Single(s => s.Id == p.Fragment.Id).FragmentLateralBcPreset);
    }
}
