using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Planar;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using CScore.Fem.Fragment.Masks;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// БД v98 (спека призмы 3.3): нормаль разреза и сторона «lateral», пресет боковых граней и призма схемы — миграция,
/// запись и перечитывание, копия схемы; панель ГУ — боковые строки по своему пресету.
/// </summary>
public sealed class StoreyFragmentPrismDbTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs-fragment-prism-{Guid.NewGuid():N}.db");
    /// <summary>Призма 2 × 2 вокруг колонны C1 (0, 0) родителя <see cref="StoreyFragmentDbFixture"/>.</summary>
    static readonly StoreyFragmentRequest Request = new(1.5, 4.5, "Вокруг C1", Prism: FragmentPrism.Create(0, 0, 1, 0, 1, 1));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }

    [Fact]
    public void V97Database_MigratesToV98_OldCutsWithoutNormal()
    {
        int fragmentId;
        using (var db = new DatabaseService(_path))
        {
            var parent = StoreyFragmentDbFixture.CreateParent(db);
            fragmentId = StoreyFragmentService.Build(db, parent.Id, new StoreyFragmentRequest(1.5, 4.5, "Этаж 2")).Schema!.Id;
        }
        Exec("ALTER TABLE fem_fragment_cuts DROP COLUMN nx;ALTER TABLE fem_fragment_cuts DROP COLUMN ny;" +
             "ALTER TABLE fem_fragment_cuts DROP COLUMN nz;ALTER TABLE fem_schemas DROP COLUMN fragment_lateral_bc_preset;" +
             "ALTER TABLE fem_schemas DROP COLUMN fragment_prism_json;UPDATE settings SET value_json='97' WHERE key='schema_version'");
        using (var db = new DatabaseService(_path))
        {
            Assert.Equal("98", Scalar("SELECT value_json FROM settings WHERE key='schema_version'"));
            foreach (var c in new[] { "nx", "ny", "nz" })
                Assert.Equal(1L, Scalar($"SELECT COUNT(*) FROM pragma_table_info('fem_fragment_cuts') WHERE name='{c}'"));
            db.LoadAll();
            var fragment = db.FemSchemas.Single(s => s.Id == fragmentId);
            Assert.Null(fragment.FragmentLateralBcPreset);
            Assert.Null(fragment.FragmentPrismJson);
            var cuts = db.GetFragmentCuts(fragmentId);
            Assert.NotEmpty(cuts);
            Assert.All(cuts, c => Assert.Null(c.Normal));
            Assert.All(cuts, c => Assert.Equal(c.Side == FragmentCutSide.Bottom ? -1 : 1, c.Outward.Z));
        }
    }

    [Fact]
    public void PrismFragment_LateralCutsPrismAndPreset_RoundTripAndCopy()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        var built = StoreyFragmentService.Build(db, parent.Id, Request);
        var fragment = Assert.IsType<FemSchema>(built.Schema);

        var lateral = db.GetFragmentCuts(fragment.Id).Where(c => c.Side == FragmentCutSide.Lateral).ToList();
        Assert.Equal(built.Draft.Cuts.Count(c => c.Side == FragmentCutSide.Lateral), lateral.Count);
        Assert.NotEmpty(lateral);
        Assert.Equal(built.Draft.Cuts.Where(c => c.Side == FragmentCutSide.Lateral).Select(c => c.Normal), lateral.Select(c => c.Normal));
        Assert.Equal(Request.Prism, FragmentPrismCodec.Decode(fragment.FragmentPrismJson)!.Prism);

        db.SetFragmentLateralBcPreset(fragment, "Kinematic");
        db.LoadAll();
        var reread = db.FemSchemas.Single(s => s.Id == fragment.Id);
        Assert.Equal("Kinematic", reread.FragmentLateralBcPreset);
        Assert.Equal(Request.Prism, FragmentPrismCodec.Decode(reread.FragmentPrismJson)!.Prism);

        var copy = db.DuplicateFemSchema(fragment.Id, "Копия");
        Assert.Equal("Kinematic", copy.FragmentLateralBcPreset);
        Assert.Equal(reread.FragmentPrismJson, copy.FragmentPrismJson);
        Assert.Equal(lateral.Select(c => (c.Side, c.Normal)), db.GetFragmentCuts(copy.Id).Where(c => c.Side == FragmentCutSide.Lateral)
            .Select(c => (c.Side, c.Normal)));
    }

    [Fact]
    public void CutBcVM_LateralRowsFollowLateralPreset()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        var fragment = StoreyFragmentService.Build(db, parent.Id, Request).Schema!;

        var vm = new FragmentCutBcVM(db, fragment);
        Assert.True(vm.HasLateral);
        Assert.Equal(FragmentBcPreset.Static, vm.SelectedLateralPreset.Preset);
        Assert.All(vm.Rows.Where(r => r.Cut.Side == FragmentCutSide.Lateral),
            r => Assert.All(r.Dofs, d => Assert.Equal(FragmentDofMode.MaskForce, d.PresetMode)));
        Assert.All(vm.Rows.Where(r => r.Cut.Side != FragmentCutSide.Lateral),
            r => Assert.Equal(FragmentDofMode.MaskDisplacement, r.Dofs[0].PresetMode));

        vm.SelectedLateralPreset = vm.Presets.Single(p => p.Preset == FragmentBcPreset.Isolation);
        Assert.Equal("Isolation", db.FemSchemas.Single(s => s.Id == fragment.Id).FragmentLateralBcPreset);
        Assert.All(vm.Rows.Where(r => r.Cut.Side == FragmentCutSide.Lateral), r => Assert.Equal(
            new[] { FragmentDofMode.Free, FragmentDofMode.Free, FragmentDofMode.Fixed }, r.Dofs.Take(3).Select(d => d.PresetMode)));
        Assert.Equal(FragmentBcPreset.Isolation, new FragmentCutBcVM(db, fragment).SelectedLateralPreset.Preset);
    }

    void Exec(string sql)
    {
        using var c = new SqliteConnection($"Data Source={_path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    object? Scalar(string sql)
    {
        using var c = new SqliteConnection($"Data Source={_path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }
}
