using System;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Media;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Views;

public partial class CrackWidthResultView : UserControl
{
    public CrackWidthResultView(CalcResult result, AppViewModel app, CalcTask task)
    {
        InitializeComponent();
        DataContext = new CrackWidthResultVM(result);
    }
}

public sealed class CrackWidthResultVM
{
    public string SummaryText { get; }
    public Brush StatusBrush { get; }
    public bool HasError { get; }
    public string ErrorText { get; } = "";
    public bool Cracked { get; }

    public string NText { get; } = "—";
    public string MxLongText { get; } = "—";
    public string MxTotalText { get; } = "—";
    public string AcrcLongText { get; } = "—";
    public string AcrcShortText { get; } = "—";
    public string SigmaSText { get; } = "—";
    public string PsiSText { get; } = "—";
    public string LsText { get; } = "—";
    public string DsEqText { get; } = "—";
    public string McrcText { get; } = "—";

    public CrackWidthResultVM(CalcResult result)
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

            Cracked = root.TryGetProperty("cracked", out var cr) && cr.GetBoolean();
            bool passedLong = root.TryGetProperty("passed_long", out var pl) && pl.GetBoolean();
            bool passedShort = root.TryGetProperty("passed_short", out var ps) && ps.GetBoolean();

            NText = Num(root, "N");
            MxLongText = Num(root, "Mx_long");
            MxTotalText = Num(root, "Mx_total");
            AcrcLongText = Num(root, "acrc_long");
            AcrcShortText = Num(root, "acrc_short");
            SigmaSText = Num(root, "sigma_s");
            PsiSText = Num(root, "psi_s");
            LsText = Num(root, "ls");
            DsEqText = Num(root, "ds_eq");
            McrcText = Num(root, "Mcrc");

            bool allPassed = passedLong && passedShort;
            SummaryText = !Cracked
                ? Loc.S("CrackWidth_NoCracks")
                : (allPassed ? Loc.S("StrengthNDM_Passed") : Loc.S("StrengthNDM_NotPassed"));
            StatusBrush = (!Cracked || allPassed)
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
