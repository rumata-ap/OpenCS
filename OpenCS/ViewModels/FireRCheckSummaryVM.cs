using CScore;
using OpenCS.Utilites;
using System.Text.Json;
using System.Windows.Media;

namespace OpenCS.ViewModels;

/// <summary>ViewModel сводки R-проверки огнестойкости.</summary>
public sealed class FireRCheckSummaryVM : ViewModelBase
{
    public string TaskTag { get; }
    public string CreatedText { get; }
    public string VerdictText { get; }
    public Brush VerdictBrush { get; }
    public string ConvergenceText { get; }
    public Brush ConvergenceBrush { get; }
    public bool HasError { get; }
    public string ErrorText { get; }

    public string FactorText { get; }
    public string UtilizationText { get; }
    public string GoverningText { get; }
    public string MarginText { get; }
    public string NTargetText { get; }
    public string MxTargetText { get; }
    public string MyTargetText { get; }
    public string NLimitText { get; }
    public string MxLimitText { get; }
    public string MyLimitText { get; }
    public string EpsContourMinText { get; }
    public string EpsCuText { get; }

    public string FireSectionNameText { get; }
    public string FireCurveText { get; }
    public string FireDurationText { get; }
    public string CriticalTimeText { get; }
    public string AggregateTypeText { get; }
    public string MethodText { get; }

    public string GammaBtMinText { get; }
    public string GammaBtAvgText { get; }
    public string GammaBtMaxText { get; }
    public string GammaStCMinText { get; }
    public string GammaStTMinText { get; }
    public string ElementCountsText { get; }

    public string IterationsText { get; }
    public string NewtonIterationsText { get; }

    public bool ShowGammaSection { get; }
    public bool ShowLimitSection { get; }

    public FireRCheckSummaryVM(CalcResult result)
    {
        TaskTag = result.TaskTag;
        CreatedText = result.Created;

        if (FireResultJson.TryGetError(result.DataJson, out string err))
        {
            HasError = true;
            ErrorText = err;
            VerdictText = Loc.S("CalcResultErrorLabel");
            VerdictBrush = Brushes.DarkRed;
            ConvergenceText = "—";
            ConvergenceBrush = Brushes.Gray;
            FactorText = MarginText = UtilizationText = GoverningText = "—";
            NTargetText = MxTargetText = MyTargetText = "—";
            NLimitText = MxLimitText = MyLimitText = "—";
            EpsContourMinText = EpsCuText = "—";
            FireSectionNameText = FireCurveText = FireDurationText = CriticalTimeText = "—";
            AggregateTypeText = MethodText = "—";
            GammaBtMinText = GammaBtAvgText = GammaBtMaxText = "—";
            GammaStCMinText = GammaStTMinText = ElementCountsText = "—";
            IterationsText = NewtonIterationsText = "—";
            ShowGammaSection = ShowLimitSection = false;
            return;
        }

        JsonElement root = FireResultJson.Root(result.DataJson);
        JsonElement d = FireResultJson.Details(root);

        bool passed = FireResultJson.Bool(root, "passed");
        string method = FireResultJson.Str(d, "method", "fiber");
        string methodLabel = method == "fiber"
            ? Loc.S("FireRCheck_MethodFiber")
            : Loc.S("FireRCheck_MethodMvp");

        VerdictText = string.Format(
            Loc.S("FireRCheck_VerdictFormat"),
            passed ? Loc.S("FireRCheck_Passed") : Loc.S("FireRCheck_NotPassed"),
            methodLabel);
        VerdictBrush = passed ? Brushes.ForestGreen : Brushes.Firebrick;

        bool converged = FireResultJson.Bool(d, "converged");
        ConvergenceText = converged ? Loc.S("ResultConvergedYes") : Loc.S("ResultConvergedNo");
        ConvergenceBrush = converged ? Brushes.Green : Brushes.Red;

        double margin = FireResultJson.Dbl(root, "margin");
        MarginText = FireResultJson.Fmt(margin, 4);

        ShowLimitSection = d.TryGetProperty("factor", out _);
        FactorText = FireResultJson.Fmt(FireResultJson.Dbl(d, "factor"), 4);
        UtilizationText = FireResultJson.Fmt(FireResultJson.Dbl(d, "utilization"), 4);
        GoverningText = MapGoverning(FireResultJson.Str(d, "governing", "none"));

        NTargetText = FireResultJson.Fmt(FireResultJson.Dbl(d, "N_target"), 3);
        MxTargetText = FireResultJson.Fmt(FireResultJson.Dbl(d, "Mx_target"), 3);
        MyTargetText = FireResultJson.Fmt(FireResultJson.Dbl(d, "My_target"), 3);
        NLimitText = FireResultJson.Fmt(FireResultJson.Dbl(d, "N_limit"), 3);
        MxLimitText = FireResultJson.Fmt(FireResultJson.Dbl(d, "Mx_limit"), 3);
        MyLimitText = FireResultJson.Fmt(FireResultJson.Dbl(d, "My_limit"), 3);
        EpsContourMinText = FireResultJson.FmtEps(FireResultJson.Dbl(d, "eps_contour_min"));
        EpsCuText = FireResultJson.FmtEps(FireResultJson.Dbl(d, "eps_cu"));

        FireSectionNameText = FireResultJson.Str(d, "fire_section_name");
        FireCurveText = MapFireCurve(FireResultJson.Str(d, "fire_curve", "iso834"));
        FireDurationText = FireResultJson.Fmt(FireResultJson.Dbl(d, "fire_duration_min"), 2);
        double crit = FireResultJson.Dbl(root, "critical_time_min",
            FireResultJson.Dbl(d, "critical_time_min"));
        CriticalTimeText = crit > 0 ? FireResultJson.Fmt(crit, 2) : "—";
        AggregateTypeText = MapAggregate(FireResultJson.Str(d, "aggregate_type", "silicate"));
        MethodText = methodLabel;

        ShowGammaSection = d.TryGetProperty("gamma_bt_min", out _);
        GammaBtMinText = FireResultJson.Fmt(FireResultJson.Dbl(d, "gamma_bt_min"), 4);
        GammaBtAvgText = FireResultJson.Fmt(FireResultJson.Dbl(d, "gamma_bt_avg"), 4);
        GammaBtMaxText = FireResultJson.Fmt(FireResultJson.Dbl(d, "gamma_bt_max"), 4);
        GammaStCMinText = FireResultJson.Fmt(FireResultJson.Dbl(d, "gamma_st_c_min"), 4);
        GammaStTMinText = FireResultJson.Fmt(FireResultJson.Dbl(d, "gamma_st_t_min"), 4);

        int nConc = (int)FireResultJson.Dbl(d, "n_concrete_elements");
        int nRebar = (int)FireResultJson.Dbl(d, "n_rebar_elements");
        ElementCountsText = nConc > 0 || nRebar > 0
            ? string.Format(Loc.S("FireRCheck_ElementCountsFormat"), nConc, nRebar)
            : "—";

        IterationsText = FireResultJson.Str(d, "iterations", "—");
        NewtonIterationsText = FireResultJson.Str(d, "newton_iterations", "—");
    }

    static string MapGoverning(string gov) => gov switch
    {
        "concrete" => Loc.S("FireRCheck_GovConcrete"),
        "rebar" => Loc.S("FireRCheck_GovRebar"),
        "both" => Loc.S("FireRCheck_GovBoth"),
        _ => "—"
    };

    static string MapFireCurve(string curve) => curve switch
    {
        "iso834" => Loc.S("FireSection_CurveIso834"),
        "hydrocarbon" => Loc.S("FireSection_CurveHydrocarbon"),
        "slow" => Loc.S("FireSection_CurveSlow"),
        _ => curve
    };

    static string MapAggregate(string agg) => agg switch
    {
        "silicate" => Loc.S("FireResult_AggregateSilicate"),
        "carbonate" => Loc.S("FireResult_AggregateCarbonate"),
        _ => agg
    };
}
