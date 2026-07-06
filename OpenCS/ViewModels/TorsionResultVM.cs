using CScore;
using OpenCS.Utilites;
using System.Globalization;
using System.Windows.Media;

namespace OpenCS.ViewModels;

/// <summary>VM страницы результата задачи кручения.</summary>
public sealed class TorsionResultVM : ViewModelBase
{
    public string TaskTag { get; }
    public string CreatedText { get; }
    public string StatusText { get; }
    public Brush StatusBrush { get; }

    public string MethodText { get; }
    public bool ShowFemOrder { get; }
    public string FemOrderText { get; } = "";
    public string ItText { get; }
    public string GItText { get; }
    public bool HasGIt { get; }
    public string GText { get; }
    public string EText { get; }
    public bool HasMaterialG { get; }
    public string MkText { get; }
    public bool HasMk { get; }
    public string ShearCenterText { get; }
    public string TauMaxText { get; }
    public string TauUnitMaxText { get; }
    public string TwistRateText { get; }
    public bool HasTwistRate { get; }
    public string ElementsText { get; }
    public string ElementSizeText { get; }
    public bool IsSingular { get; }
    public string? ErrorText { get; }
    public bool HasError { get; }

    public bool ShowAutoConverge { get; }
    public string AutoConvergeSeriesText { get; } = "";
    public string ItOrderText { get; } = "";
    public string ItExtrapolatedText { get; } = "";
    public Brush ItExtrapolatedBrush { get; } = Brushes.Gray;
    public bool HasShearCenterOrder { get; }
    public string ShearCenterOrderText { get; } = "";
    public string ShearCenterExtrapolatedText { get; } = "";

    public TorsionPlotVM TauPlot { get; }
    public TorsionPlotVM PotentialPlot { get; }
    public bool HasPlots { get; }

    public TorsionResultVM(CalcResult r)
    {
        TaskTag = r.TaskTag ?? "";
        CreatedText = r.Created ?? "";

        var data = TorsionResultData.FromCalcResult(r);
        HasError = !string.IsNullOrEmpty(data.Error);
        ErrorText = data.Error;

        StatusText = r.Status switch
        {
            "ok" => Loc.S("TorsionStatusOk"),
            "not_converged" => Loc.S("TorsionStatusNotConverged"),
            "error" => Loc.S("TorsionStatusError"),
            _ => r.Status ?? ""
        };
        StatusBrush = r.Status switch
        {
            "ok" => Brushes.Green,
            "error" => Brushes.DarkOrange,
            _ => Brushes.DarkOrange
        };

        MethodText = data.Method switch
        {
            "bem" => Loc.S("CalcTaskKind_torsion_bem"),
            "fem" => Loc.S("CalcTaskKind_torsion_fem"),
            _ => data.Method
        };

        ShowFemOrder = data.IsFem;
        FemOrderText = data.FemOrder == "quadratic"
            ? Loc.S("TorsionFemOrder_Quadratic")
            : Loc.S("TorsionFemOrder_Linear");

        var inv = CultureInfo.InvariantCulture;
        ItText = double.IsFinite(data.ItMm4) ? data.ItMm4.ToString("N1", inv) : "—";

        HasGIt = data.GMpa > 0 && double.IsFinite(data.ItMm4);
        if (HasGIt)
        {
            double gIt = data.GMpa * data.ItMm4 * 1e-9; // кН·м²
            GItText = gIt.ToString("G4", inv);
        }
        else GItText = "—";

        HasMaterialG = data.GMpa > 0;
        GText = HasMaterialG ? $"{data.GMpa.ToString("G4", inv)} МПа ({Loc.S("TorsionGAuto")})" : "—";
        EText = data.EMpa > 0 ? $"{data.EMpa.ToString("G4", inv)} МПа" : "—";
        HasMk = data.MkKNm > 0;
        MkText = HasMk ? $"{data.MkKNm.ToString("G4", inv)} кН·м" : "—";

        ShearCenterText = data.HasShearCenter
            ? $"({data.ShearCenterXmm:F1}; {data.ShearCenterYmm:F1})"
            : (data.IsFem ? Loc.S("TorsionShearCenterFemNa") : "—");

        TauUnitMaxText = double.IsFinite(data.TauUnitMaxMm2)
            ? $"{data.TauUnitMaxMm2.ToString("G4", inv)} мм²"
            : "—";

        TauMaxText = data.HasPhysicalTau
            ? $"{data.TauMaxMpa.ToString("F2", inv)} МПа"
            : Loc.S("TorsionTauMaxNotSet");

        HasTwistRate = double.IsFinite(data.TwistRate) && data.GMpa > 0 && data.MkKNm != 0;
        TwistRateText = HasTwistRate
            ? $"{data.TwistRate.ToString("G4", inv)} 1/м"
            : "—";

        ElementsText = data.NElements > 0 ? data.NElements.ToString(inv) : "—";
        ElementSizeText = double.IsFinite(data.ElementSizeM)
            ? $"{(data.ElementSizeM * 1000).ToString("F1", inv)} мм ({data.ElementSizeM.ToString("G4", inv)} м)"
            : "—";

        IsSingular = data.Singular;

        HasPlots = data.HasFieldMesh || data.HasBoundaryField ||
                   (data.NodeXM != null && data.TauUnit != null);
        TauPlot = new TorsionPlotVM(data, TorsionFieldMode.TauUnit);
        PotentialPlot = new TorsionPlotVM(data, TorsionFieldMode.Potential);

        ShowAutoConverge = data.AutoConverge && data.ConvergenceHMm != null && data.ConvergenceItMm4 != null;
        if (ShowAutoConverge)
        {
            var hs = data.ConvergenceHMm!;
            var its = data.ConvergenceItMm4!;
            AutoConvergeSeriesText = string.Join("  →  ", hs.Zip(its,
                (h, itv) => $"h={h.ToString("G3", inv)} мм: It={itv.ToString("N0", inv)} мм⁴"));

            ItOrderText = data.ItOrder is double p
                ? $"p ≈ {p.ToString("F2", inv)}"
                : Loc.S("TorsionOrderUndetermined");

            ItExtrapolatedText = data.ItExtrapolated
                ? Loc.S("TorsionExtrapolated")
                : Loc.S("TorsionExtrapolationUnreliable");
            ItExtrapolatedBrush = data.ItExtrapolated ? Brushes.Green : Brushes.DarkOrange;

            HasShearCenterOrder = data.ShearCenterOrderX is double || data.ShearCenterOrderY is double;
            if (HasShearCenterOrder)
            {
                string px = data.ShearCenterOrderX is double pxv ? pxv.ToString("F2", inv) : "—";
                string py = data.ShearCenterOrderY is double pyv ? pyv.ToString("F2", inv) : "—";
                ShearCenterOrderText = $"p_x ≈ {px}, p_y ≈ {py}";
                ShearCenterExtrapolatedText = data.ShearCenterExtrapolated
                    ? Loc.S("TorsionExtrapolated")
                    : Loc.S("TorsionExtrapolationUnreliable");
            }
        }
    }
}
