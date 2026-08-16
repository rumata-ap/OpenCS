using System.Globalization;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

    /// <summary>Жёсткости каждой рассчитанной стадии с учётом σ/ψs в растянутой арматуре.</summary>
    public ObservableCollection<TotalCurvatureStiffnessRow> StiffnessRows { get; } = [];
    public bool HasStiffness => StiffnessRows.Count > 0;

    public sealed record TotalCurvatureStiffnessRow(
        string StageLabel,
        string XcText, string YcText,
        string EAText, string EIy0Text, string EIz0Text,
        string EIycText, string EIzcText,
        string EAelText, string EIyelText, string EIzelText,
        string PhiEAText, string PhiEIyText, string PhiEIzText);

    public TotalCurvatureSummaryVM(
        CalcResult result,
        CrossSection? section = null,
        CalcSettings? settings = null,
        IReadOnlyList<Diagramm>? diagramPool = null)
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

            BuildStiffnessRows(root, section, settings, diagramPool);

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

    static void AddStiffnessRow(
        ObservableCollection<TotalCurvatureStiffnessRow> rows,
        TotalCurvatureStageVM stage,
        SectionStiffnessResult stiffness)
    {
        rows.Add(new TotalCurvatureStiffnessRow(
            stage.Label,
            $"{stiffness.Xc_mm:+0.0;-0.0}  мм",
            $"{stiffness.Yc_mm:+0.0;-0.0}  мм",
            $"{stiffness.EA_kN:F0}  кН",
            $"{stiffness.EIy0_kNm2:F2}  кН·м²",
            $"{stiffness.EIz0_kNm2:F2}  кН·м²",
            $"{stiffness.EIyc_kNm2:F2}  кН·м²",
            $"{stiffness.EIzc_kNm2:F2}  кН·м²",
            $"{stiffness.EAel_kN:F0}  кН",
            $"{stiffness.EIyel_kNm2:F2}  кН·м²",
            $"{stiffness.EIzel_kNm2:F2}  кН·м²",
            FmtRatio(stiffness.PhiEA),
            FmtRatio(stiffness.PhiEIy),
            FmtRatio(stiffness.PhiEIz)));
    }

    static string FmtRatio(double value) =>
        double.IsNaN(value) || double.IsInfinity(value)
            ? Loc.S("TotalCurvature_Empty")
            : $"{value:0.000}";

    void BuildStiffnessRows(
        JsonElement root,
        CrossSection? section,
        CalcSettings? settings,
        IReadOnlyList<Diagramm>? diagramPool)
    {
        if (section == null)
            return;

        try
        {
            var actualSettings = settings ?? CalcSettings.Default;
            section.ResolveAndBuildDiagramms(
                actualSettings.Sp63DescEtaMin,
                pool: diagramPool,
                rebarDifferentialDiagram: actualSettings.RebarDifferentialDiagram);

            for (int number = 1; number <= 3; number++)
            {
                var stage = TotalCurvatureStageVM.Parse(root, number, Cracked);
                if (stage == null || !stage.Converged)
                    continue;

                section.SetEps(stage.Plane, stage.CalcType, stage.ConcreteTension);
                var stiffness = SectionStiffnessCalculator.Compute(
                    section,
                    stage.Plane,
                    stage.CalcType,
                    actualSettings.GridDensity,
                    stage.ConcreteTension,
                    effectiveStressKpaByFiber: stage.EffectiveStressKpa,
                    effectiveStrainByFiber: fiber => fiber.Eps + fiber.Eps_p);
                if (stiffness.HasValue)
                    AddStiffnessRow(StiffnessRows, stage, stiffness.Value);
            }
        }
        catch
        {
            // Сводка результата должна оставаться доступной даже для старого
            // результата или повреждённой геометрии, где жёсткости не построить.
            StiffnessRows.Clear();
        }
    }
}
