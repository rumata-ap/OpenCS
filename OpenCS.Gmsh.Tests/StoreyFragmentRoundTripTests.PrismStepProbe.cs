using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Fragment.Masks;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using static OpenCS.Gmsh.Tests.StoreyFragmentGmshFixture;

namespace OpenCS.Gmsh.Tests;

/// <summary>
/// Разбор предупреждений <c>fragment_mask_balance</c> на призме колонны (музей: шаг 0,46 м, предупреждения 4–18 %).
/// Та же постановка, что в <c>StoreyFragmentRoundTripTests.Prism.cs</c>, но без проверок: печатаются предупреждения
/// равновесия и расхождения равнодействующей по шагу сетки родителя и фрагмента (один шаг для обоих).
/// Диагностический тест, ничего не утверждает.
/// </summary>
public sealed partial class StoreyFragmentRoundTripTests
{
    [Theory]
    [InlineData(0.5, 1.5)]
    [InlineData(0.5, 1.4)]
    [InlineData(0.46, 1.5)]
    [InlineData(0.25, 1.5)]
    public async Task Prism_MaskBalance_StepProbe(double step, double half)
    {
        var prism = CScore.Fem.Fragment.FragmentPrism.Create(3, 3, 1, 0, half, half);
        await InTempRoot($"opencs-prism-probe-{step.ToString(System.Globalization.CultureInfo.InvariantCulture)}-{half.ToString(System.Globalization.CultureInfo.InvariantCulture)}", async root =>
        {
            using var db = new DatabaseService(Path.Combine(root, "test.db"));
            var ctx = new FemCsfeaRunContext(db, GmshSettings(root), new ListLog(output));
            var parent = await CreateParent(ctx, root, step);

            var built = StoreyFragmentService.Build(db, parent.Schema.Id,
                new StoreyFragmentRequest(Z1, Z2, "Вокруг колонны", Prism: prism));
            var fragment = Assert.IsType<CScore.Fem.FemSchema>(built.Schema);
            await Mesh(db, fragment, step, root);

            var extracted = StoreyFragmentMaskService.Extract(db, fragment, FragmentMaskSet.SourceCsfeaLinear, S(parent.Analysis.Id));
            output.WriteLine($"=== шаг {step} м, полуразмер призмы {half} м: предупреждений масок {extracted.Diagnostics.Count} ===");
            foreach (var d in extracted.Diagnostics.Where(d => d.Code == "fragment_mask_balance"))
                output.WriteLine($"BALANCE {d.Message}");
            var resultant = extracted.Diagnostics.Where(d => d.Code == "fragment_mask_cut_resultant").ToList();
            output.WriteLine($"resultant-предупреждений: {resultant.Count}");
            foreach (var d in resultant) output.WriteLine($"RES {d.Message}");
        });
    }
}
