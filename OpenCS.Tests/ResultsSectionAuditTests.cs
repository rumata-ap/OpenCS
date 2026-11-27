using CScore;
using CScore.Fem;
using OpenCS.Converters;
using OpenCS.Tasks;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Раздел «Результаты» (results): подпись сечения родных стержней в таблицах схемы, запоминание выбора экспорта
/// разреза, строка устаревания результата продавливания, Esc у окон просмотра.
/// </summary>
[Collection("Loc language")]
public sealed class ResultsSectionAuditTests
{
    [Fact]
    public void SectionLabel_NativeBarsShowAssignedSection_ImportedShowSourceTag()
    {
        var cross = new List<CrossSection> { new() { Id = 5, Tag = "Б-1" } };
        var plates = new List<PlateSection> { new() { Id = 7, Tag = "Плита 200" } };

        Assert.Equal("Б-1", FemSectionLabelConverter.Label(new FemMember { ElemType = "beam", CrossSectionId = 5 }, cross, plates));
        Assert.Equal("Плита 200", FemSectionLabelConverter.Label(new FemMember { ElemType = "shell", PlateSectionId = 7 }, cross, plates));
        Assert.Equal("Ж12", FemSectionLabelConverter.Label(new FemMember { ElemType = "beam", SectionTag = "Ж12" }, cross, plates));
        Assert.Equal("Ж12", FemSectionLabelConverter.Label(new FemMember { ElemType = "beam", CrossSectionId = 99, SectionTag = "Ж12" }, cross, plates));
        Assert.Equal("", FemSectionLabelConverter.Label(new FemMember { ElemType = "beam" }, cross, plates));
        Assert.Equal("Б-1", FemSectionLabelConverter.Label(new FemElement { CrossSectionId = 5 }, cross));
        Assert.Equal("Ж3", FemSectionLabelConverter.Label(new FemElement { SectionTag = "Ж3" }, cross));
    }

    [Fact]
    public void SectionCutExport_RemembersChoiceInSettings()
    {
        var s = new SectionCutWindowSettings();
        Assert.Equal(new SectionCutExportOptions(SectionCutExportFormat.Png, true), SectionCutExportDialog.Recall(s));

        SectionCutExportDialog.Store(s, new SectionCutExportOptions(SectionCutExportFormat.Dxf, false));
        Assert.Equal("dxf", s.ExportFormat);
        var copy = s.Clone();
        Assert.Equal(new SectionCutExportOptions(SectionCutExportFormat.Dxf, false), SectionCutExportDialog.Recall(copy));

        // Сериализация настроек переносит выбор; неизвестный формат — PNG.
        var json = System.Text.Json.JsonSerializer.Serialize(s);
        var back = System.Text.Json.JsonSerializer.Deserialize<SectionCutWindowSettings>(json)!;
        Assert.Equal(SectionCutExportFormat.Dxf, SectionCutExportDialog.Recall(back).Format);
        back.ExportFormat = "bmp";
        Assert.Equal(SectionCutExportFormat.Png, SectionCutExportDialog.Recall(back).Format);
    }

    [Fact]
    public void PunchingResult_StaleWhenSourceDeletedOrRecomputed()
    {
        Loc.SetLanguage("ru-RU");
        try
        {
            var analysis = new FemAnalysis { Id = 3, Tag = "Линейный", ResultId = 10 };
            var data = new PunchingFemCheckData { Source = "Линейный", SourceKind = "csfea_linear", SourceAnalysisId = 3, SourceResultId = 10 };
            FemAnalysis? Find(int id) => id == 3 ? analysis : null;

            Assert.Equal("", PunchingFemResultVM.SourceStaleText(data, null, Find));

            analysis.ResultId = 11;
            Assert.Equal(string.Format(Loc.S("PunchingFemSourceChanged"), "Линейный"), PunchingFemResultVM.SourceStaleText(data, null, Find));

            Assert.Equal(string.Format(Loc.S("PunchingFemSourceDeleted"), "Линейный"),
                PunchingFemResultVM.SourceStaleText(data, null, _ => null));

            // Результат до 27.11 (без Id источника): «удалена» — по источнику в параметрах проверки; снимок импорта — нет.
            var old = data with { SourceAnalysisId = null, SourceResultId = null };
            var check = new FemCheck { NormCode = PunchingFemCheckParams.NormCode,
                ParamsJson = new PunchingFemCheckParams { SourceRef = "3" }.ToJson() };
            Assert.Equal("", PunchingFemResultVM.SourceStaleText(old, check, Find));
            Assert.Equal(string.Format(Loc.S("PunchingFemSourceDeleted"), "Линейный"), PunchingFemResultVM.SourceStaleText(old, check, _ => null));
            var lira = new FemCheck { NormCode = PunchingFemCheckParams.NormCode,
                ParamsJson = new PunchingFemCheckParams { SourceKind = CScore.Fem.Fragment.Masks.FragmentMaskSet.SourceLira, SourceRef = "" }.ToJson() };
            Assert.Equal("", PunchingFemResultVM.SourceStaleText(old, lira, _ => null));
        }
        finally { Loc.SetLanguage(null); }
    }

    /// <summary>Окна просмотра без кнопок закрываются по Esc (общий помощник; новое окно не забудется).</summary>
    [Theory]
    [InlineData("ShearInclinedProjectionDialog")]
    [InlineData("TotalCurvatureStageWindow")]
    [InlineData("FemBarDiagramDialog")]
    [InlineData("FemCsfeaBarDetailDialog")]
    [InlineData("FemPlateSectionStateWindow")]
    [InlineData("FemSectionStateWindow")]
    [InlineData("SectionCutDiagramWindow")]
    [InlineData("FemMemberForceDialog")]
    public void ViewerWindows_CloseOnEscape(string window)
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "OpenCS.sln"))) root = Path.GetDirectoryName(root)!;
        string code = File.ReadAllText(Path.Combine(root, "OpenCS", "Views", window + ".xaml.cs"));
        Assert.Contains("ViewerWindowKeys.CloseOnEscape(this)", code);
    }
}
