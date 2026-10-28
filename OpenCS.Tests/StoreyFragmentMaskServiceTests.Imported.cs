using CScore.Fem;
using CScore.Fem.Fragment.Masks;
using CScore.Fem.Loads;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Маски из снимка результатов программы (срез 7 C/D, задача 5): поля линейного CSfea родителя переписываются «как SCAD»
/// (силы сечений в знаках программы, оси выдачи — углом пластин, загружения — с номером программы) → маски источника
/// <c>scad</c> совпадают с масками <c>csfea_linear</c>.
/// </summary>
public sealed partial class StoreyFragmentMaskServiceTests
{
    /// <summary>
    /// Снимок «как SCAD» из полей записанного результата постановки: шаг i — загружение программы № i; стержни — силы
    /// сечений в знаках <see cref="ImportedForceConvention.LiraScad"/>, пластины — в осях выдачи CSfea (их угол
    /// записывается в сетку родителя), поворот стержней — как у CSfea.
    /// </summary>
    ImportedResultSnapshot ScadSnapshotFromCsfea(DatabaseService db, FemSchema parent, FemAnalysis analysis, int loads)
    {
        var nodes = db.GetFemMeshNodes(parent.Id).ToDictionary(n => n.NodeTag);
        var members = db.GetFemMembers(parent.Id).ToDictionary(m => m.ElemTag);
        var regionAxes = FemCsfeaInputBuilder.RegionAxes([.. members.Values], db.GetPlanarRegions(parent.Id));
        foreach (var e in db.GetFemMeshElements(parent.Id))
        {
            var g = FemElementGeometry.Of(e, nodes)!;
            if (g.IsShell)
            {
                var axis = FemRcModelAdapter.ShellForceAxis(e, g, members, regionAxes);
                var (x1, _, n) = g.ShellFrame();
                double deg = Math.Atan2(x1.Cross(axis).Dot(n), x1.Dot(axis)) * 180 / Math.PI;
                Exec(FormattableString.Invariant(
                    $"UPDATE fem_elements SET local_axis_angle_deg={deg:R} WHERE schema_id={parent.Id} AND elem_tag='{e.ElemTag}'"));
            }
            else
                Exec(FormattableString.Invariant(
                    $"UPDATE fem_elements SET beam_rotation_deg={FemRcModelAdapter.BeamRotationDeg(e, members):R} WHERE schema_id={parent.Id} AND elem_tag='{e.ElemTag}'"));
        }

        var c = ImportedForceConvention.LiraScad;
        var result = new List<ImportedResultLoad>();
        for (int i = 1; i <= loads; i++)
        {
            var f = FemCsfeaStepFieldsCodec.Unpack(db.GetFemResultStep(analysis.ResultId!.Value, i)!);
            var bars = new double[f.BeamIds.Length * ImportedResultLoad.BarComponents];
            for (int b = 0; b < f.BeamIds.Length; b++)
                for (int k = 0; k < 6; k++)
                {
                    // Сила узла на КЭ: у узла 1 −S(0), у узла 2 +S(L); программа = c · S.
                    bars[12 * b + k] = -c.Bar[k] * f.BeamForces[RcSecantStepFields.BeamForceComponents * b + k];
                    bars[12 * b + 6 + k] = c.Bar[k] * f.BeamForces[RcSecantStepFields.BeamForceComponents * b + 6 + k];
                }
            var shells = new double[f.ShellIds.Length * ImportedResultLoad.ShellComponents];
            for (int s = 0; s < f.ShellIds.Length; s++)
                for (int k = 0; k < 8; k++)
                    shells[8 * s + k] = c.Shell[k] * f.ShellForces[RcSecantStepFields.ShellForceComponents * s + k];
            result.Add(new ImportedResultLoad
            {
                Num = i, Name = $"Загружение {i}", NodeNums = f.NodeIds, Displacements = f.Displacements,
                BarNums = f.BeamIds, BarForces = bars, ShellNums = f.ShellIds, ShellForces = shells,
            });
        }
        return new ImportedResultSnapshot
        {
            Program = ImportedResultSnapshot.ProgramScad, SourceName = "каркас.SPR", TakenAt = "2026-10-28T12:00:00", Loads = result,
        };
    }

    [Fact]
    public void Extract_Scad_MatchesCsfeaLinear_AndTracksSnapshot()
    {
        var p = Create();
        using var db = p.Db;
        var lc = db.GetFemLoadCases(p.Schema.Id);
        var stages = new[] { Single("Пост.", lc[0].Id), Single("Ветер", lc[1].Id) };
        var analysis = SaveResult(db, p.Schema, FemCsfeaRunner.LinearAnalysisKind, stages, stages);

        var noSnapshot = StoreyFragmentMaskService.Extract(db, p.Fragment, FragmentMaskSet.SourceScad, "");
        Assert.Contains(noSnapshot.Diagnostics, d => d.Code == "fragment_mask_no_snapshot");

        Assert.False(StoreyFragmentMaskService.Extract(db, p.Fragment, FragmentMaskSet.SourceCsfeaLinear, S(analysis.Id)).HasErrors);
        var expected = db.GetFragmentMasks(p.Fragment.Id)!.Items.ToDictionary(i => (i.CutId, i.Key));

        var snapshot = ScadSnapshotFromCsfea(db, p.Schema, analysis, 2);
        for (int i = 0; i < 2; i++)
            Exec($"UPDATE fem_load_cases SET origin='import:scad', source_load_num={i + 1} WHERE id={lc[i].Id}");
        var data = ImportedResultSnapshotCodec.Encode(snapshot);
        db.SaveFemSchemaSourceFile(p.Schema.Id, FemSchemaSourceFileKind.ScadMaskResults, "каркас.SPR", data);

        var r = StoreyFragmentMaskService.Extract(db, p.Fragment, FragmentMaskSet.SourceScad, "");

        Assert.False(r.HasErrors, string.Join("\n", r.Diagnostics.Select(d => d.Message)));
        Assert.DoesNotContain(r.Diagnostics, d => d.Code is "fragment_mask_balance" or "fragment_mask_balance_skipped"
            or "fragment_mask_shear_sign" or "fragment_mask_kinematic_only" or "fragment_mask_bar_axes"
            or "fragment_mask_shell_axes" or "fragment_mask_source");
        var set = db.GetFragmentMasks(p.Fragment.Id)!;
        Assert.Equal(FragmentMaskSet.SourceScad, set.SourceKind);
        Assert.Equal(FemSchemaSourceFileKind.ScadMaskResults, set.SourceRef);
        Assert.Equal(expected.Count, set.Items.Count);
        double scale = -Q * 36;
        foreach (var item in set.Items)
        {
            var e = expected[(item.CutId, item.Key)];
            Assert.Equal(e.Samples.Count, item.Samples.Count);
            for (int s = 0; s < e.Samples.Count; s++)
                for (int k = 0; k < 6; k++)
                {
                    Assert.Equal(e.Samples[s].U![k], item.Samples[s].U![k], 12);
                    Assert.True(Math.Abs(e.Samples[s].F![k] - item.Samples[s].F![k]) <= 1e-6 * scale,
                        $"разрез {item.CutId}, {item.Key}, компонента {k}: {e.Samples[s].F![k]} против {item.Samples[s].F![k]}");
                }
        }

        Assert.Equal(FragmentMaskStatus.Current, StoreyFragmentMaskService.CheckMasks(db, p.Fragment));
        snapshot.Loads[0].Displacements[2] += 1e-6;
        db.SaveFemSchemaSourceFile(p.Schema.Id, FemSchemaSourceFileKind.ScadMaskResults, "каркас.SPR",
            ImportedResultSnapshotCodec.Encode(snapshot));
        Assert.Equal(FragmentMaskStatus.Stale, StoreyFragmentMaskService.CheckMasks(db, p.Fragment));
    }

    /// <summary>Окно «Маски из родителя…»: снимок SCAD — источник после постановок CSfea; по умолчанию — источник масок.</summary>
    [Fact]
    public void Flow_ScadSnapshotSource()
    {
        var p = Create();
        using var db = p.Db;
        var lc = db.GetFemLoadCases(p.Schema.Id);
        var stages = new[] { Single("Пост.", lc[0].Id), Single("Ветер", lc[1].Id) };
        var analysis = SaveResult(db, p.Schema, FemCsfeaRunner.LinearAnalysisKind, stages, stages);
        var snapshot = ScadSnapshotFromCsfea(db, p.Schema, analysis, 2);
        for (int i = 0; i < 2; i++)
            Exec($"UPDATE fem_load_cases SET origin='import:scad', source_load_num={i + 1} WHERE id={lc[i].Id}");
        db.SaveFemSchemaSourceFile(p.Schema.Id, FemSchemaSourceFileKind.ScadMaskResults, "каркас.SPR",
            ImportedResultSnapshotCodec.Encode(snapshot));

        var sources = StoreyFragmentMaskFlow.Sources(db, p.Fragment, out string? reason);

        Assert.Null(reason);
        Assert.Equal([FragmentMaskSet.SourceCsfeaLinear, FragmentMaskSet.SourceScad], sources.Select(s => s.SourceKind));
        Assert.Equal((0, "SCAD"), (sources[1].AnalysisId, sources[1].AnalysisTag));
        Assert.Same(sources[0], StoreyFragmentMaskFlow.DefaultSource(db, p.Fragment, sources));
        var r = StoreyFragmentMaskFlow.Extract(db, p.Fragment, sources[1]);
        Assert.False(r.IsError, string.Join("\n", r.Rows.Select(x => x.Message)));
        Assert.True(r.Rows[0].IsSummary);
        Assert.Same(sources[1], StoreyFragmentMaskFlow.DefaultSource(db, p.Fragment, sources));
    }
}
