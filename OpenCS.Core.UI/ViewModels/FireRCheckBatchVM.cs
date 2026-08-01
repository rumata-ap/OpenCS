using CScore;
using OpenCS.Utilites;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace OpenCS.ViewModels;

/// <summary>ViewModel пакетной R-проверки.</summary>
public sealed class FireRCheckBatchVM : ViewModelBase
{
    public string TaskTag { get; }
    public string CreatedText { get; }
    public string SummaryText { get; }
    public string StatusBrush { get; }
    public bool HasError { get; }
    public string ErrorText { get; } = "";

    public ObservableCollection<BatchRow> AllRows { get; } = [];
    public ObservableCollection<BatchRow> FailedRows { get; } = [];
    public bool HasFailedRows => FailedRows.Count > 0;

    public sealed record BatchRow(
        int Num,
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
            StatusBrush = Argb.FromRgb(139, 0, 0).ToHex();
            return;
        }

        JsonElement root = FireResultJson.Root(result.DataJson);
        bool passed = FireResultJson.Bool(root, "passed");
        double worst = FireResultJson.Dbl(root, "worst_margin");
        StatusBrush = passed
            ? Argb.FromArgb(70, 80, 180, 80).ToHex()
            : Argb.FromRgb(178, 34, 34).ToHex();

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
                    Num: BatchResultRowHelper.RowNum(row, total),
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
