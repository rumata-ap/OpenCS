using CScore.Fem.Fragment.Masks;
using CScore.Fem.Fragment;
using OpenCS.Services;
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
}
