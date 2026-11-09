using CScore;
using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Fragment.Masks;
using CScore.Fem.Imperfections;
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

    /// <summary>
    /// Секущая постановка второго порядка по стадиям «Пост.» → «Полезная» (накопление), упругие сечения, запись всех шагов
    /// (по шагу на стадию — у стадии λ шагом 1).
    /// </summary>
    static FemAnalysis SecantSecondOrderByStages(DatabaseService db, FemSchema schema, string tag)
    {
        var cases = db.GetFemLoadCases(schema.Id);
        var analysis = new FemAnalysis
        {
            SchemaId = schema.Id, Tag = tag, Kind = FemCsfeaRunner.AnalysisKind,
            ParamsJson = new FemAnalysisParams
            {
                Stages = [Single("Пост.", cases.Single(c => c.Tag == "Пост.").Id), Single("Полезная", cases.Single(c => c.Tag == "Полезная").Id)],
                Csfea = new FemCsfeaParams
                {
                    PhysicalNonlinearity = false, GeomNonlinear = true, TolDisplacement = 1e-8, ResultRecording = FemCsfeaRecording.All,
                },
            }.ToJson(),
        };
        db.SaveFemAnalysis(analysis);
        return analysis;
    }

    /// <summary>
    /// Срез 2 спеки второго порядка фрагмента: родитель — секущий второго порядка → маски по стадиям с силами кромок плиты
    /// по узловым силам КЭ (K_s + K_σ)·u (без отката на МНК) → фрагмент вокруг колонны секущим второго порядка, горизонтальные
    /// разрезы «Как в родителе», боковые — «Усилия из родителя»: маски уравновешены, прогибы плиты в призме повторяют
    /// родителя, наборы — второго порядка (источник второго порядка, торцы кинематические) без предупреждений
    /// <c>fragment_second_order_*</c>.
    /// </summary>
    [Fact]
    public async Task SecondOrder_ParentSecondOrderMasks_NodalForces_PrismRepeatsParent()
    {
        await InTempRoot("opencs-fragment-roundtrip", async root =>
        {
            using var db = new DatabaseService(Path.Combine(root, "test.db"));
            var ctx = new FemCsfeaRunContext(db, GmshSettings(root), new ListLog(output));
            var linear = await CreateParent(ctx, root, PrismStep);
            var parentAnalysis = SecantSecondOrderByStages(db, linear.Schema, "Родитель · 2-й порядок");
            var parentResult = await FemCsfeaRunner.RunAsync(ctx, linear.Schema, parentAnalysis, null, CancellationToken.None);
            var parentSummary = FemCsfeaResultSummary.Parse(parentResult.DataJson)!;
            foreach (var line in parentSummary.Errors.Concat(parentSummary.Report)) output.WriteLine("родитель: " + line);
            Assert.Equal("ok", parentResult.Status);
            Assert.True(parentSummary.SecondOrder);
            parentAnalysis.ResultId = parentResult.Id;
            parentAnalysis.Status = "ok";
            db.SaveFemAnalysis(parentAnalysis);
            var parent = linear with { Analysis = parentAnalysis };

            var built = StoreyFragmentService.Build(db, parent.Schema.Id, new StoreyFragmentRequest(Z1, Z2, "Вокруг колонны", Prism: ColumnPrism));
            var fragment = Assert.IsType<FemSchema>(built.Schema);
            await Mesh(db, fragment, PrismStep, root);
            var extracted = StoreyFragmentMaskService.Extract(db, fragment, FragmentMaskSet.SourceCsfeaSecant, S(parentAnalysis.Id));
            foreach (var d in extracted.Diagnostics) output.WriteLine($"маски {(d.IsError ? "ОШИБКА" : "предупр.")} {d.Code}: {d.Message}");
            Assert.False(extracted.HasErrors);
            Assert.DoesNotContain(extracted.Diagnostics, d => d.Code is "fragment_mask_nodal_unsupported" or "fragment_mask_balance"
                or "fragment_mask_nodal_selfcheck");
            Assert.Contains(extracted.Diagnostics, d => d.Code == "fragment_mask_summary" && d.Message.Contains("узловые силы КЭ"));
            output.WriteLine($"невязка равновесия масок {extracted.BalanceResidual:e3}");
            // Факт 08.11: 9,3·10⁻⁵ (K_σ решателя — по перемещениям предыдущей итерации; у линейного источника — до 10⁻⁵).
            Assert.True(extracted.BalanceResidual is <= 5e-4, $"невязка масок {extracted.BalanceResidual}");
            db.LoadAll();
            Restore(db, parent.Sections);
            fragment = db.FemSchemas.Single(s => s.Id == fragment.Id);

            db.SetFragmentBcPreset(fragment, FragmentBcPreset.Kinematic.ToString());
            db.SetFragmentLateralBcPreset(fragment, FragmentBcPreset.Static.ToString());
            var fragmentAnalysis = SecantSecondOrderByStages(db, fragment, "Фрагмент · 2-й порядок");
            var result = await FemCsfeaRunner.RunAsync(ctx, fragment, fragmentAnalysis, null, CancellationToken.None);
            var summary = FemCsfeaResultSummary.Parse(result.DataJson)!;
            foreach (var line in summary.Errors.Concat(summary.Report)) output.WriteLine(line);
            Assert.Equal("ok", result.Status);
            Assert.DoesNotContain(summary.Report, l => l.Contains("не уравновешена", StringComparison.Ordinal));
            Assert.True(summary.SecondOrder, "наборы фрагмента — второго порядка");
            foreach (var (tag, scale, diff) in CompareSlab(db, parent, fragment, result, "Призма, 2-й порядок", where: InPrism))
                Assert.True(diff <= 0.05 * scale, $"«{tag}»: расхождение прогиба {diff / scale:P2} > 5 %");
        });
    }

    /// <summary>
    /// Спека 2026-11-09, срез 1: родитель — секущий второго порядка с несовершенством (крен 1/200 по X загружением
    /// «Несовершенство» в первой стадии, на весь прогон) → маски вокруг колонны по узловым силам КЭ с K_σ·u₀ (без отката на
    /// МНК). Строгий критерий — самопроверка равновесия узлов тела ступенькой (без <c>fragment_mask_nodal_selfcheck</c>).
    /// Равновесие масок с нагрузками полосы считается в исходной геометрии, а силы K_σ·u₀ КЭ тела уравновешены в геометрии
    /// с u₀ — их момент N·φ·h остаётся в невязке: факт 09.11 — 4,16·10⁻³ / 8,33·10⁻³ / 1,67·10⁻² при 1/φ = 400 / 200 / 100
    /// (≈ 1,67·φ, строго пропорционально φ; без несовершенства — 9,3·10⁻⁵).
    /// </summary>
    [Fact]
    public async Task SecondOrder_ParentWithImperfection_MasksByNodalForces()
    {
        await InTempRoot("opencs-fragment-roundtrip", async root =>
        {
            using var db = new DatabaseService(Path.Combine(root, "test.db"));
            var ctx = new FemCsfeaRunContext(db, GmshSettings(root), new ListLog(output));
            var parent = await CreateParent(ctx, root, PrismStep);
            var imp = new FemLoadCase { SchemaId = parent.Schema.Id, Tag = "Крен", Sp20Type = FemSp20Types.Imperfection };
            db.SaveFemLoadCase(imp);
            db.SaveFemImperfection(new FemImperfection
            {
                SchemaId = parent.Schema.Id, LoadCaseId = imp.Id, Kind = FemImperfectionKinds.Sway, Tag = "Крен X",
                ParamsJson = new FemSwayParams { PhiInv = 200 }.ToJson(),
            });
            db.LoadAll();
            Restore(db, parent.Sections);
            var cases = db.GetFemLoadCases(parent.Schema.Id);
            var analysis = SecantSecondOrderByStages(db, parent.Schema, "Родитель · 2-й порядок · крен");
            var ps = FemAnalysisParams.Parse(analysis.ParamsJson);
            ps.Stages![0].LoadExpressionJson = new FemLoadExpression
            {
                Mode = FemLoadExpressionMode.Sum,
                Terms = [new FemLoadTerm { LoadCaseId = cases.Single(c => c.Tag == "Пост.").Id, Coefficient = 1 },
                         new FemLoadTerm { LoadCaseId = imp.Id, Coefficient = 1 }],
            }.ToJson();
            analysis.ParamsJson = ps.ToJson();
            db.SaveFemAnalysis(analysis);
            var result = await FemCsfeaRunner.RunAsync(ctx, parent.Schema, analysis, null, CancellationToken.None);
            var summary = FemCsfeaResultSummary.Parse(result.DataJson)!;
            foreach (var line in summary.Errors.Concat(summary.Report)) output.WriteLine("родитель: " + line);
            Assert.Equal("ok", result.Status);
            Assert.False(string.IsNullOrEmpty(summary.ImperfectionTags), "в родителе нет несовершенства");
            analysis.ResultId = result.Id;
            analysis.Status = "ok";
            db.SaveFemAnalysis(analysis);

            var built = StoreyFragmentService.Build(db, parent.Schema.Id, new StoreyFragmentRequest(Z1, Z2, "Вокруг колонны", Prism: ColumnPrism));
            var fragment = Assert.IsType<FemSchema>(built.Schema);
            await Mesh(db, fragment, PrismStep, root);
            var extracted = StoreyFragmentMaskService.Extract(db, fragment, FragmentMaskSet.SourceCsfeaSecant, S(analysis.Id));
            foreach (var d in extracted.Diagnostics) output.WriteLine($"маски {(d.IsError ? "ОШИБКА" : "предупр.")} {d.Code}: {d.Message}");
            Assert.False(extracted.HasErrors);
            Assert.DoesNotContain(extracted.Diagnostics, d => d.Code is "fragment_mask_nodal_unsupported" or "fragment_mask_balance"
                or "fragment_mask_nodal_selfcheck");
            Assert.Contains(extracted.Diagnostics, d => d.Code == "fragment_mask_summary" && d.Message.Contains("узловые силы КЭ"));
            output.WriteLine($"невязка равновесия масок {extracted.BalanceResidual:e3}");
            const double phi = 1.0 / 200;
            Assert.True(extracted.BalanceResidual is <= 2 * phi, $"невязка масок {extracted.BalanceResidual} > 2·φ");
        });
    }
}
