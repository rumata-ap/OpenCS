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
        int Num, string Label, double N, double MxTotal, double MyTotal,
        double MxLong, double MyLong, string NText, string MxTotalText, string MyTotalText,
        string McrcText,
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
                    double n = Number(row, "N");
                    double mxTotal = Number(row, "Mx_total");
                    double myTotal = Number(row, "My_total");
                    double mxLong = Number(row, "Mx_long");
                    double myLong = Number(row, "My_long");
                    Rows.Add(new BatchRow(
                        Num: BatchResultRowHelper.RowNum(row, index),
                        Label: Str(row, "label"),
                        N: n,
                        MxTotal: mxTotal,
                        MyTotal: myTotal,
                        MxLong: mxLong,
                        MyLong: myLong,
                        NText: Format(n, 4),
                        MxTotalText: Format(mxTotal, 4),
                        MyTotalText: Format(myTotal, 4),
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
            ? Format(value.GetDouble(), significantDigits)
            : Loc.S("TotalCurvature_Empty");

    static double Number(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : 0.0;

    static string Format(double value, int significantDigits) =>
        value.ToString($"G{significantDigits}", CultureInfo.InvariantCulture);
}
