using System;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Media;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>ViewModel сводки результата задачи предельного нагружения.</summary>
public sealed class LimitForceSummaryVM : ViewModelBase
{
    public string TaskTag     { get; }
    public string CreatedText { get; }
    public string StatusText  { get; }
    public Brush  StatusBrush { get; }

    public string SolverText     { get; }
    public string FactorText     { get; }
    public string UtilizationText { get; }
    public string GoverningText  { get; }
    public string NLimitText     { get; }
    public string MxLimitText    { get; }
    public string MyLimitText    { get; }
    public string EpsContourText { get; }
    public string EpsCuText      { get; }
    public bool   HasRebarEps    { get; }
    public string EpsRebarText   { get; }
    public string EpsSuText      { get; }
    public string IterationsText { get; }
    public string NewtonIterText { get; }

    public StrainSummaryVM StrainPart { get; }

    public LimitForceSummaryVM(CalcResult result, CrossSection section, CalcType calcType, int gridDensity = 20)
    {
        TaskTag     = result.TaskTag;
        CreatedText = result.Created;
        StrainPart  = new StrainSummaryVM(result, section, calcType, gridDensity);

        var doc  = JsonDocument.Parse(result.DataJson);
        var root = doc.RootElement;

        bool converged = root.TryGetProperty("converged", out var cv) && cv.GetBoolean();
        StatusText  = converged ? Loc.S("ResultConvergedYes") : Loc.S("ResultConvergedNo");
        StatusBrush = converged ? Brushes.Green : Brushes.Red;

        string solver = root.TryGetProperty("solver_method", out var sm) ? sm.GetString() ?? "" : "";
        SolverText = solver == "fast" ? Loc.S("LimitForceSolver_Fast") : Loc.S("LimitForceSolver_Bisection");

        double factor = root.TryGetProperty("factor", out var fv) ? fv.GetDouble() : 0;
        double util   = root.TryGetProperty("utilization", out var uv) ? uv.GetDouble() : 0;
        FactorText      = $"{factor:0.0000}";
        UtilizationText = $"{util:0.0000}";

        string gov = root.TryGetProperty("governing", out var gv) ? gv.GetString() ?? "none" : "none";
        GoverningText = gov switch
        {
            "concrete" => Loc.S("LimitForceGov_Concrete"),
            "rebar"    => Loc.S("LimitForceGov_Rebar"),
            "both"     => Loc.S("LimitForceGov_Both"),
            _          => "—"
        };

        NLimitText  = Num(root, "N_limit",  3) + "  кН";
        MxLimitText = Num(root, "Mx_limit", 3) + "  кН·м";
        MyLimitText = Num(root, "My_limit", 3) + "  кН·м";

        EpsContourText = Signed(root, "eps_contour_min");
        EpsCuText      = Signed(root, "eps_cu");

        string epsRebarText = "—";
        string epsSuText    = "—";
        bool hasRebarEps = root.TryGetProperty("eps_rebar_max", out var er)
                           && er.ValueKind != JsonValueKind.Null;
        if (hasRebarEps)
        {
            epsRebarText = $"{er.GetDouble():+0.000000;-0.000000}";
            epsSuText = root.TryGetProperty("eps_su", out var es) && es.ValueKind != JsonValueKind.Null
                ? $"{es.GetDouble():+0.000000;-0.000000}"
                : "—";
        }
        HasRebarEps  = hasRebarEps;
        EpsRebarText = epsRebarText;
        EpsSuText    = epsSuText;

        int iters = root.TryGetProperty("iterations", out var iv) ? iv.GetInt32() : 0;
        int newton = root.TryGetProperty("newton_iterations", out var nv) ? nv.GetInt32() : 0;
        IterationsText = iters.ToString();
        NewtonIterText = newton.ToString();
    }

    static string Num(JsonElement root, string prop, int dec)
    {
        if (!root.TryGetProperty(prop, out var v)) return "—";
        return v.GetDouble().ToString($"+0.{new string('0', dec)};-0.{new string('0', dec)}");
    }

    static string Signed(JsonElement root, string prop)
    {
        if (!root.TryGetProperty(prop, out var v)) return "—";
        return $"{v.GetDouble():+0.000000;-0.000000}";
    }
}
