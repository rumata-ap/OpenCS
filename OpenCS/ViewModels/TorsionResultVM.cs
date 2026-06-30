using System.Text.Json;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>VM страницы результата задачи кручения.</summary>
public sealed class TorsionResultVM : ViewModelBase
{
    public string TaskTag { get; }
    public string StatusText { get; }
    public string StatusBrush { get; }

    public string MethodText { get; }
    public string ItText { get; }       // мм⁴
    public string ShearCenterText { get; }
    public string TauMaxText { get; }   // МПа (если заданы G, Mk)
    public string ElementsText { get; }
    public string ElementSizeText { get; }
    public bool IsSingular { get; }

    public TorsionResultVM(CalcResult r)
    {
        TaskTag = r.TaskTag ?? "";
        StatusText = r.Status switch
        {
            "ok" => "Сошлось ✓",
            "not_converged" => "НЕ СОШЛОСЬ ✗",
            "error" => "Ошибка ✗",
            _ => r.Status ?? ""
        };
        StatusBrush = r.Status switch
        {
            "ok" => "Green",
            "error" => "DarkOrange",
            _ => "DarkOrange"
        };

        try
        {
            using var doc = JsonDocument.Parse(r.DataJson);
            var root = doc.RootElement;
            MethodText = root.TryGetProperty("method", out var m) ? m.GetString() ?? "" : "";
            ItText = root.TryGetProperty("It_mm4", out var it) ? $"{it.GetDouble():e3}" : "—";
            double scx = root.TryGetProperty("shear_center_x_m", out var sx) ? sx.GetDouble() : double.NaN;
            double scy = root.TryGetProperty("shear_center_y_m", out var sy) ? sy.GetDouble() : double.NaN;
            ShearCenterText = (double.IsNaN(scx) || double.IsNaN(scy))
                ? "—" : $"({scx * 1000:F1}; {scy * 1000:F1}) мм";
            TauMaxText = root.TryGetProperty("tau_max_Pa", out var tau) && double.IsFinite(tau.GetDouble())
                ? $"{tau.GetDouble() / 1e6:F2} МПа" : "— (G, Mk не заданы)";
            ElementsText = root.TryGetProperty("n_elements", out var el) ? el.GetInt32().ToString() : "—";
            ElementSizeText = root.TryGetProperty("element_size_m", out var es) ? $"{es.GetDouble():F3}" : "—";
            IsSingular = root.TryGetProperty("singular", out var s) && s.GetBoolean();
        }
        catch
        {
            ItText = "—"; ShearCenterText = "—"; TauMaxText = "—"; ElementsText = "—";
            MethodText = ""; ElementSizeText = "—";
        }
    }
}
