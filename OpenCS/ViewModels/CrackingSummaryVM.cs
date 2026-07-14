using System.Text.Json;
using System.Windows.Media;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>ViewModel сводки результата задачи «Момент трещинообразования» (cracking).</summary>
public sealed class CrackingSummaryVM : ViewModelBase
{
    public string TaskTag { get; }
    public string CreatedText { get; }
    public string StatusText { get; }
    public Brush StatusBrush { get; }
    public bool HasError { get; }
    public string ErrorText { get; } = "";
    public bool PlaneConverged { get; }

    public CrackingMomentPartVM CrackingPart { get; }

    public CrackingSummaryVM(CalcResult result)
    {
        TaskTag = result.TaskTag;
        CreatedText = result.Created;

        if (result.Status == "error")
        {
            HasError = true;
            try
            {
                var errDoc = JsonDocument.Parse(result.DataJson);
                ErrorText = errDoc.RootElement.TryGetProperty("error", out var e) ? e.GetString() ?? "" : result.DataJson;
            }
            catch { ErrorText = result.DataJson; }
            StatusText = Loc.S("CalcResultErrorLabel");
            StatusBrush = Brushes.DarkRed;
            CrackingPart = new CrackingMomentPartVM(0, 0, 0, 0, false, null, null);
            return;
        }

        var doc = JsonDocument.Parse(result.DataJson);
        var root = doc.RootElement;

        bool converged = root.TryGetProperty("converged", out var cv) && cv.GetBoolean();
        PlaneConverged = root.TryGetProperty("plane_converged", out var pc) && pc.GetBoolean();

        double n = GetD(root, "N");
        double mxCrc = GetD(root, "Mx_crc");
        double myCrc = GetD(root, "My_crc");
        double mcrc = GetD(root, "Mcrc");
        double? epsMaxTension = root.TryGetProperty("eps_max_tension", out var emt) ? emt.GetDouble() : null;
        double? epsTensionLimit = root.TryGetProperty("eps_tension_limit", out var etl) ? etl.GetDouble() : null;

        CrackingPart = new CrackingMomentPartVM(n, mxCrc, myCrc, mcrc, converged, epsMaxTension, epsTensionLimit);

        StatusText = converged ? Loc.S("ResultConvergedYes") : Loc.S("ResultConvergedNo");
        StatusBrush = converged ? Brushes.Green : Brushes.OrangeRed;
    }

    static double GetD(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0.0;
}
