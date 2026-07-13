using System;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Media;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Views;

public partial class CrackingResultView : UserControl
{
    public CrackingResultView(CalcResult result, AppViewModel app, CalcTask task)
    {
        InitializeComponent();
        DataContext = new CrackingResultVM(result);
    }
}

public sealed class CrackingResultVM
{
    public string SummaryText { get; }
    public Brush StatusBrush { get; }
    public bool HasError { get; }
    public string ErrorText { get; } = "";
    public string NText { get; } = "—";
    public string MxCrcText { get; } = "—";
    public string MyCrcText { get; } = "—";
    public string McrcText { get; } = "—";

    public CrackingResultVM(CalcResult result)
    {
        if (result.Status == "error")
        {
            HasError = true;
            try
            {
                var doc = JsonDocument.Parse(result.DataJson);
                ErrorText = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() ?? "" : result.DataJson;
            }
            catch { ErrorText = result.DataJson; }
            SummaryText = Loc.S("CalcResultErrorLabel");
            StatusBrush = Brushes.DarkRed;
            return;
        }

        try
        {
            var doc = JsonDocument.Parse(result.DataJson);
            var root = doc.RootElement;
            bool converged = root.TryGetProperty("converged", out var cv) && cv.GetBoolean();

            NText     = Num(root, "N");
            MxCrcText = Num(root, "Mx_crc");
            MyCrcText = Num(root, "My_crc");
            McrcText  = Num(root, "Mcrc");

            SummaryText = converged ? Loc.S("ResultConvergedYes") : Loc.S("ResultConvergedNo");
            StatusBrush = converged
                ? new SolidColorBrush(Color.FromArgb(70, 80, 180, 80))
                : Brushes.OrangeRed;
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorText = ex.Message;
            SummaryText = Loc.S("CalcResultErrorLabel");
            StatusBrush = Brushes.DarkRed;
        }
    }

    static string Num(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble().ToString("G4") : "—";
}
