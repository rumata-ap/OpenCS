using System.Globalization;
using System.Text.Json;
using System.Windows.Media;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>ViewModel сводки результата задачи «Полная кривизна».</summary>
public sealed class TotalCurvatureSummaryVM : ViewModelBase
{
    public string TaskTag { get; }
    public string CreatedText { get; }
    public string StatusText { get; } = "";
    public Brush StatusBrush { get; } = Brushes.Gray;
    public bool HasError { get; }
    public string ErrorText { get; } = "";

    public string NText { get; } = Loc.S("TotalCurvature_Empty");
    public string MxLongText { get; } = Loc.S("TotalCurvature_Empty");
    public string MxTotalText { get; } = Loc.S("TotalCurvature_Empty");
    public string MyLongText { get; } = Loc.S("TotalCurvature_Empty");
    public string MyTotalText { get; } = Loc.S("TotalCurvature_Empty");

    public bool Cracked { get; }
    public string CrackedText { get; } = Loc.S("TotalCurvature_Empty");
    public string McrcText { get; } = Loc.S("TotalCurvature_Empty");
    public string MxCrcText { get; } = Loc.S("TotalCurvature_Empty");
    public string MyCrcText { get; } = Loc.S("TotalCurvature_Empty");

    public bool HasStage1 { get; }
    public bool CanPlotStage1 { get; }
    public string Stage1PlotTooltip { get; } = "";
    public string Stage1Label { get; } = "";
    public string Stage1Text { get; } = Loc.S("TotalCurvature_Empty");
    public bool HasStage2 { get; }
    public bool CanPlotStage2 { get; }
    public string Stage2PlotTooltip { get; } = "";
    public string Stage2Label { get; } = "";
    public string Stage2Text { get; } = Loc.S("TotalCurvature_Empty");
    public bool HasStage3 { get; }
    public bool CanPlotStage3 { get; }
    public string Stage3PlotTooltip { get; } = "";
    public string Stage3Label { get; } = "";
    public string Stage3Text { get; } = Loc.S("TotalCurvature_Empty");

    public string KyFullText { get; } = Loc.S("TotalCurvature_Empty");
    public string KzFullText { get; } = Loc.S("TotalCurvature_Empty");
    public string KFullText { get; } = Loc.S("TotalCurvature_Empty");
    public bool AllConverged { get; }

    public TotalCurvatureSummaryVM(CalcResult result)
    {
        TaskTag = result.TaskTag;
        CreatedText = result.Created;

        if (result.Status == "error")
        {
            HasError = true;
            try
            {
                var errorDoc = JsonDocument.Parse(result.DataJson);
                ErrorText = errorDoc.RootElement.TryGetProperty("error", out var error)
                    ? error.GetString() ?? ""
                    : result.DataJson;
            }
            catch { ErrorText = result.DataJson; }

            StatusText = Loc.S("CalcResultErrorLabel");
            StatusBrush = Brushes.DarkRed;
            return;
        }

        try
        {
            var root = JsonDocument.Parse(result.DataJson).RootElement;
            Cracked = root.TryGetProperty("cracked", out var cracked) && cracked.GetBoolean();
            AllConverged = root.TryGetProperty("all_converged", out var converged)
                && converged.GetBoolean();

            NText = NumUnit(root, "N", "Unit_kN");
            MxLongText = NumUnit(root, "Mx_long", "Unit_kNm");
            MxTotalText = NumUnit(root, "Mx_total", "Unit_kNm");
            MyLongText = NumUnit(root, "My_long", "Unit_kNm");
            MyTotalText = NumUnit(root, "My_total", "Unit_kNm");

            string plotTooltip = Cracked
                ? Loc.S("TotalCurvature_StageStarTooltip") + ". "
                    + Loc.S("TotalCurvature_StagePlotTooltip")
                : Loc.S("TotalCurvature_StagePlotTooltip");
            Stage1PlotTooltip = plotTooltip;
            Stage2PlotTooltip = plotTooltip;
            Stage3PlotTooltip = plotTooltip;

            CrackedText = Cracked
                ? Loc.S("TotalCurvature_Cracked")
                : Loc.S("TotalCurvature_NotCracked");
            McrcText = NumUnit(root, "Mcrc", "Unit_kNm");
            MxCrcText = NumUnit(root, "Mx_crc", "Unit_kNm");
            MyCrcText = NumUnit(root, "My_crc", "Unit_kNm");

            if (root.TryGetProperty("stage1", out var stage1)
                && stage1.ValueKind == JsonValueKind.Object)
            {
                HasStage1 = true;
                Stage1Label = TotalCurvatureStageVM.LabelFor(1, Cracked);
                Stage1Text = StageText(stage1);
                CanPlotStage1 = HasPlotData(stage1);
            }
            if (root.TryGetProperty("stage2", out var stage2)
                && stage2.ValueKind == JsonValueKind.Object)
            {
                HasStage2 = true;
                Stage2Label = TotalCurvatureStageVM.LabelFor(2, Cracked);
                Stage2Text = StageText(stage2);
                CanPlotStage2 = HasPlotData(stage2);
            }
            if (root.TryGetProperty("stage3", out var stage3)
                && stage3.ValueKind == JsonValueKind.Object)
            {
                HasStage3 = true;
                Stage3Label = TotalCurvatureStageVM.LabelFor(3, Cracked);
                Stage3Text = StageText(stage3);
                CanPlotStage3 = HasPlotData(stage3);
            }

            KyFullText = NumRaw(root, "ky_full");
            KzFullText = NumRaw(root, "kz_full");
            KFullText = NumRaw(root, "k_full");

            StatusText = AllConverged
                ? Loc.S("ResultConvergedYes")
                : Loc.S("ResultConvergedNo");
            StatusBrush = AllConverged ? Brushes.Green : Brushes.OrangeRed;
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorText = ex.Message;
            StatusText = Loc.S("CalcResultErrorLabel");
            StatusBrush = Brushes.DarkRed;
        }
    }

    static string StageText(JsonElement stage)
    {
        string mx = NumberOrEmpty(stage, "Mx", "0.####");
        string my = NumberOrEmpty(stage, "My", "0.####");
        string ky = NumberOrEmpty(stage, "ky", "0.########");
        string kz = NumberOrEmpty(stage, "kz", "0.########");
        bool converged = stage.TryGetProperty("converged", out var value) && value.GetBoolean();
        return string.Format(Loc.S("TotalCurvature_StageValuesFormat"), mx, my, ky, kz,
            Loc.S(converged ? "TotalCurvature_StageConverged" : "TotalCurvature_StageNotConverged"));
    }

    static string NumRaw(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble().ToString("0.########", CultureInfo.InvariantCulture)
            : Loc.S("TotalCurvature_Empty");

    static string NumUnit(JsonElement element, string key, string unitKey) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
            ? $"{value.GetDouble().ToString("0.####", CultureInfo.InvariantCulture)}  {Loc.S(unitKey)}"
            : Loc.S("TotalCurvature_Empty");

    static string NumberOrEmpty(JsonElement element, string key, string format) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble().ToString(format, CultureInfo.InvariantCulture)
            : Loc.S("TotalCurvature_Empty");

    static bool HasPlotData(JsonElement stage) =>
        stage.TryGetProperty("e0", out var e0) && e0.ValueKind == JsonValueKind.Number
        && stage.TryGetProperty("ky", out var ky) && ky.ValueKind == JsonValueKind.Number
        && stage.TryGetProperty("kz", out var kz) && kz.ValueKind == JsonValueKind.Number
        && stage.TryGetProperty("converged", out var converged)
        && converged.ValueKind == JsonValueKind.True && converged.GetBoolean();
}
