using System.Text.RegularExpressions;
using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Planar;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Перевод диагностики CScore (хвост 6 вехи «Аудит GUI + CLI»): каталог русских шаблонов ↔ ресурсы <c>Diag_*</c>, показ на
/// языке интерфейса с вложенными частями, хранение ключа и аргументов в отчёте БД.
/// </summary>
[Collection("Loc language")]
public sealed class DiagnosticTextTests : IDisposable
{
    public void Dispose() => Loc.SetLanguage(null);

    static readonly Regex Placeholder = new(@"(?<!\{)\{(\d+)(?::[^}]*)?\}", RegexOptions.Compiled);

    static SortedSet<string> Holes(string template) => [.. Placeholder.Matches(template).Select(m => m.Groups[1].Value)];

    [Fact]
    public void Catalog_EveryKey_RuEqualsTemplate_EnSamePlaceholders()
    {
        Loc.SetLanguage(Loc.Russian);
        foreach (var (key, template) in DiagCatalog.All)
        {
            Assert.True(Loc.TryGet(DiagnosticText.KeyPrefix + key, out var ru), $"нет ru Diag_{key}");
            Assert.Equal(template, ru);
        }
        Loc.SetLanguage(Loc.English);
        foreach (var (key, template) in DiagCatalog.All)
        {
            Assert.True(Loc.TryGet(DiagnosticText.KeyPrefix + key, out var en), $"нет en Diag_{key}");
            Assert.True(Holes(template).SetEquals(Holes(en)), $"Diag_{key}: {{n}} en не совпадают с каталогом");
            Assert.DoesNotMatch("[А-Яа-яЁё]", en);
        }
    }

    [Fact]
    public void English_NestedParts_Translated_RussianEqualsMessage()
    {
        var cut = new FragmentCut("C1", FragmentCutKind.End, FragmentCutSide.Bottom, 1.5, "1", []);
        var where = new DiagText("fragment_cut_where", "C1", FragmentCutLabel.SideText(cut), 1.5);
        var d = FemValidationDiagnostic.Of("fragment_mask_missing",
            new DiagText("fragment_mask_missing", where, "Ux", new DiagText("fragment_mask_case_load", "Пост."),
                new DiagText("fragment_mask_missing_none")), true);

        Loc.SetLanguage(Loc.Russian);
        Assert.Equal(d.Message, DiagnosticText.Format(d));
        Assert.Contains("низ", d.Message);

        Loc.SetLanguage(Loc.English);
        string en = DiagnosticText.Format(d);
        Assert.Equal("Cut “C1” (bottom, z = 1.5), DOF Ux, load case “Пост.”: no mask.", en);
    }

    [Fact]
    public void Report_Json_KeepsTextForOldRows_UnknownKeyFallsBackToMessage()
    {
        var list = DiagText.List(["1", "2", "3"], 2);
        var d = FemValidationDiagnostic.Of("fragment_load_lost", new DiagText("fragment_load_lost", list), false, ["1"]);
        var plain = new FemValidationDiagnostic("fragment_old", "Старая строка без перевода.", false);
        string json = StoreyFragmentBuildReport.ToJson([d, plain]);

        var back = StoreyFragmentBuildReport.FromJson(json);
        Assert.Equal(2, back.Count);
        Assert.NotNull(back[0].Text);
        Assert.Null(back[1].Text);
        Loc.SetLanguage(Loc.English);
        Assert.Equal("Loads not transferred — no fragment part or the load kind is not supported: 1, 2 and 1 more.",
            DiagnosticText.Format(back[0]));
        Assert.Equal("Старая строка без перевода.", DiagnosticText.Format(back[1]));

        // Ключ, которого больше нет в каталоге, — показ по Message.
        var renamed = StoreyFragmentBuildReport.FromJson(json.Replace("\"fragment_load_lost\",\"a\"", "\"gone_key\",\"a\""));
        Assert.Null(renamed[0].Text);
        Assert.Equal(d.Message, DiagnosticText.Format(renamed[0]));
    }

    [Fact]
    public void Builder_Refusal_EnglishInReportRows()
    {
        var draft = StoreyFragmentBuilder.Build(new StoreyFragmentParentSnapshot(), new StoreyFragmentRequest(5, 3, "x"));
        var error = Assert.Single(draft.Diagnostics, x => x.IsError);
        Assert.Equal("fragment_band_empty", error.Code);
        Loc.SetLanguage(Loc.English);
        var row = Assert.Single(StoreyFragmentReportRows.From([error]));
        Assert.Equal("Level z₂ = 3 m must be above z₁ = 5 m.", row.Message);
    }
}
