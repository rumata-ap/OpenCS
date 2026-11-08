using CScore;
using CScore.Fem;
using CScore.Fem.Fragment.Masks;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using static OpenCS.Gmsh.Tests.StoreyFragmentGmshFixture;

namespace OpenCS.Gmsh.Tests;

/// <summary>
/// Согласованность второго порядка фрагмента (спека 2026-11-06, срез 3) сквозь БД: маски из линейного родителя (первый
/// порядок), постановка фрагмента — секущий CSfea с геомнелином; проверяется подготовка (без расчёта решателем).
/// </summary>
public sealed partial class StoreyFragmentRoundTripTests
{
    /// <summary>Секущая постановка фрагмента по загружениям с геомнелином, флажок масок первого порядка — <paramref name="allow"/>.</summary>
    static FemAnalysis SecantGeometric(DatabaseService db, FemSchema schema, string tag, bool allow)
    {
        var analysis = new FemAnalysis
        {
            SchemaId = schema.Id, Tag = tag, Kind = FemCsfeaRunner.AnalysisKind,
            ParamsJson = new FemAnalysisParams
            {
                Stages = db.GetFemLoadCases(schema.Id).Select(lc => Single(lc.Tag, lc.Id)).ToList(),
                Csfea = new FemCsfeaParams { PhysicalNonlinearity = false, GeomNonlinear = true, FragmentSecondOrderOnFirstOrderMasks = allow },
            }.ToJson(),
        };
        db.SaveFemAnalysis(analysis);
        return analysis;
    }

    [Fact]
    public async Task SecondOrder_FirstOrderMasks_ConservativeUnlessAllowed_StaticEndsAlwaysFirstOrder()
    {
        await InTempRoot("opencs-fragment-roundtrip", async root =>
        {
            using var db = new DatabaseService(Path.Combine(root, "test.db"));
            var ctx = new FemCsfeaRunContext(db, GmshSettings(root), new ListLog(output));
            var parent = await CreateParent(ctx, root);
            var fragment = await CreateFragment(db, parent, root, ParentStep);

            async Task<FemCsfeaPrepared> Prepare(FragmentBcPreset preset, bool allow)
            {
                db.SetFragmentBcPreset(fragment, preset.ToString());
                var prepared = await FemCsfeaRunner.PrepareAsync(ctx, fragment, SecantGeometric(db, fragment, $"2-й порядок · {preset} · {allow}", allow),
                    buildMesh: false, CancellationToken.None);
                foreach (var line in prepared.Report) output.WriteLine($"{preset}/{allow}: {line}");
                Assert.False(prepared.HasErrors);
                return prepared;
            }
            static string[] Codes(FemCsfeaPrepared p) => p.Diagnostics.Select(d => d.Code).Where(c => c.StartsWith("fragment_second_order", StringComparison.Ordinal)).ToArray();

            // «Как в родителе» на масках линейного родителя: торцы кинематические, источник первого порядка → η применяется.
            var kinematic = await Prepare(FragmentBcPreset.Kinematic, allow: false);
            Assert.False(kinematic.SecondOrderAllowed);
            Assert.False(kinematic.SecondOrderOf(true));
            Assert.Equal(["fragment_second_order_source_first_order", "fragment_second_order_eta"], Codes(kinematic));
            Assert.Contains(kinematic.Diagnostics, d => d.Message.Contains("«Линейный»", StringComparison.Ordinal));

            // Флажок пользователя: тот же фрагмент — второй порядок, предупреждение об источнике остаётся.
            var allowed = await Prepare(FragmentBcPreset.Kinematic, allow: true);
            Assert.True(allowed.SecondOrderAllowed);
            Assert.Equal(["fragment_second_order_source_first_order"], Codes(allowed));

            // Силовые торцы: P-Δ этажа не передан — первый порядок и с флажком.
            var statics = await Prepare(FragmentBcPreset.Static, allow: true);
            Assert.False(statics.SecondOrderAllowed);
            Assert.Contains("fragment_second_order_static_ends", Codes(statics));

            // Сетка фрагмента 0,5 м на 3 м высоты — не грубая.
            Assert.DoesNotContain("fragment_second_order_coarse", Codes(kinematic));

            // Тот же источник по сведениям: сводка линейного родителя без P-Δ — первый порядок.
            var set = db.GetFragmentMasks(fragment.Id, items: false);
            Assert.False(FragmentSecondOrderCheck.SourceSecondOrder(set, StoreyFragmentMaskService.CheckMasks(db, fragment),
                FemCsfeaResultSummary.Parse(db.GetCalcResultById(parent.Analysis.ResultId!.Value)!.DataJson)));
        });
    }
}
