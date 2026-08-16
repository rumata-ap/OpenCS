using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows.Media;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>ViewModel пакетного расчёта полной кривизны.</summary>
public sealed class TotalCurvatureBatchVM : ViewModelBase
{
    public string TaskTag { get; }
    public string CreatedText { get; }
    public string SummaryText { get; } = "";
    public Brush StatusBrush { get; } = Brushes.Gray;
    public bool HasError { get; }
    public string ErrorText { get; } = "";

    public ObservableCollection<BatchRow> Rows { get; } = [];

    public sealed record BatchRow(
        int Num, string Label, string NText, string MxTotalText, string McrcText,
        string CrackedText, string KyFullText, string StatusText, bool IsConverged);

    public TotalCurvatureBatchVM(CalcResult result)
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
            SummaryText = Loc.S("CalcResultErrorLabel");
            StatusBrush = Brushes.DarkRed;
            return;
        }

        try
        {
            var root = JsonDocument.Parse(result.DataJson).RootElement;
            int total = root.TryGetProperty("total", out var totalValue) ? totalValue.GetInt32() : 0;
            int converged = root.TryGetProperty("converged_count", out var countValue)
                ? countValue.GetInt32()
                : 0;
            bool allConverged = root.TryGetProperty("all_converged", out var allValue)
                && allValue.GetBoolean();

            StatusBrush = allConverged
                ? new SolidColorBrush(Color.FromArgb(70, 80, 180, 80))
                : Brushes.OrangeRed;
            SummaryText = string.Format(Loc.S("TotalCurvatureBatch_SummaryFormat"),
                total, converged, total - converged);

            if (root.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (var row in rows.EnumerateArray())
                {
                    index++;
                    bool isConverged = row.TryGetProperty("converged", out var convergedValue)
                        && convergedValue.GetBoolean();
                    bool cracked = row.TryGetProperty("cracked", out var crackedValue)
                        && crackedValue.GetBoolean();
                    Rows.Add(new BatchRow(
                        Num: BatchResultRowHelper.RowNum(row, index),
                        Label: Str(row, "label"),
                        NText: Num(row, "N", 4),
                        MxTotalText: Num(row, "Mx_total", 4),
                        McrcText: Num(row, "Mcrc", 4),
                        CrackedText: cracked
                            ? Loc.S("TotalCurvature_Cracked")
                            : Loc.S("TotalCurvature_NotCracked"),
                        KyFullText: Num(row, "ky_full", 6),
                        StatusText: isConverged
                            ? Loc.S("ResultConvergedYes")
                            : Loc.S("ResultConvergedNo"),
                        IsConverged: isConverged));
                }
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorText = ex.Message;
            SummaryText = Loc.S("CalcResultErrorLabel");
            StatusBrush = Brushes.DarkRed;
        }
    }

    static string Str(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value)
            ? value.GetString() ?? Loc.S("TotalCurvature_Empty")
            : Loc.S("TotalCurvature_Empty");

    static string Num(JsonElement element, string key, int significantDigits) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble().ToString($"G{significantDigits}", CultureInfo.InvariantCulture)
            : Loc.S("TotalCurvature_Empty");
}
