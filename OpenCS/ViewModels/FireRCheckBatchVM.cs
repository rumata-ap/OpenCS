using CScore;
using OpenCS.Utilites;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Media;

namespace OpenCS.ViewModels;

/// <summary>ViewModel пакетной R-проверки.</summary>
public sealed class FireRCheckBatchVM : ViewModelBase
{
    public string TaskTag { get; }
    public string CreatedText { get; }
    public string SummaryText { get; }
    public Brush StatusBrush { get; }
    public bool HasError { get; }
    public string ErrorText { get; } = "";

    public ObservableCollection<BatchRow> AllRows { get; } = [];
    public ObservableCollection<BatchRow> FailedRows { get; } = [];
    public bool HasFailedRows => FailedRows.Count > 0;

    public sealed record BatchRow(
        string Label,
        string PassedText,
        string MarginText,
        string FactorText,
        string GoverningText);

    public FireRCheckBatchVM(CalcResult result)
    {
        TaskTag = result.TaskTag;
        CreatedText = result.Created;

        if (FireResultJson.TryGetError(result.DataJson, out string err))
        {
            HasError = true;
            ErrorText = err;
            SummaryText = Loc.S("CalcResultErrorLabel");
            StatusBrush = Brushes.DarkRed;
            return;
        }

        JsonElement root = FireResultJson.Root(result.DataJson);
        bool passed = FireResultJson.Bool(root, "passed");
        double worst = FireResultJson.Dbl(root, "worst_margin");
        StatusBrush = passed ? Brushes.ForestGreen : Brushes.Firebrick;

        int total = 0;
        int nPassed = 0;
        if (root.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                total++;
                bool rowPassed = FireResultJson.Bool(row, "passed");
                if (rowPassed) nPassed++;

                var item = new BatchRow(
                    Label: FireResultJson.Str(row, "label", $"#{total}"),
                    PassedText: rowPassed ? Loc.S("FireRCheck_PassedShort") : Loc.S("FireRCheck_NotPassedShort"),
                    MarginText: FireResultJson.Fmt(FireResultJson.Dbl(row, "margin"), 4),
                    FactorText: FireResultJson.Fmt(FireResultJson.Dbl(row, "factor"), 4),
                    GoverningText: FireResultJson.Str(row, "governing", "—"));

                AllRows.Add(item);
                if (!rowPassed)
                    FailedRows.Add(item);
            }
        }

        SummaryText = string.Format(
            Loc.S("FireRCheckBatch_SummaryFormat"),
            total, nPassed, total - nPassed, FireResultJson.Fmt(worst, 4));
    }
}
