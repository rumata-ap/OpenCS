using System.Collections.ObjectModel;
using System.Windows.Media;
using CScore;
using OpenCS.OpenSees.CScore;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Сводка состояния сечения по записанным волокнам OpenSees или по записанным деформациям CSfea. Реализует все
/// привязки StrainSummaryView/StrainSummaryBody; группы η/жёсткости/сходимости скрыты.</summary>
public class FemSectionSummaryVM
{
    public string TaskTag { get; }
    public string CreatedText { get; }
    public string StatusText { get; }
    public Brush StatusBrush { get; }

    public string Eps0Text { get; }
    public string KyText { get; }
    public string KzText { get; }

    public string NText { get; }
    public string MxText { get; }
    public string MyText { get; }

    public bool EtaEnabled => false;
    public string EtaModeText => "—";
    public string MxOriginalText => "—";
    public string MyOriginalText => "—";
    public string L0xText => "—";
    public string HxText => "—";
    public string SlendernessXText => "—";
    public string DxText => "—";
    public string NcrXText => "—";
    public string EtaXText => "—";
    public string L0yText => "—";
    public string HyText => "—";
    public string SlendernessYText => "—";
    public string DyText => "—";
    public string NcrYText => "—";
    public string EtaYText => "—";
    public bool EtaUnstable => false;
    public bool EtaExtrapolationFailed => false;
    public bool ShowEtaTrajectory => false;
    public string EtaXTrajectoryText => "—";
    public string EtaYTrajectoryText => "—";

    public bool HasExtremes { get; }
    public string EpsMinText { get; }
    public string EpsMaxText { get; }

    public bool HasStiffness => false;
    public string XcText => "—";
    public string YcText => "—";
    public string EAText => "—";
    public string EIy0Text => "—";
    public string EIz0Text => "—";
    public string EIycText => "—";
    public string EIzcText => "—";

    public string EAelText => "—";
    public string EIyelText => "—";
    public string EIzelText => "—";
    public string PhiEAText => "—";
    public string PhiEIyText => "—";
    public string PhiEIzText => "—";

    public bool HasRebar => RebarRows.Count > 0;
    public ObservableCollection<StrainSummaryVM.RebarRow> RebarRows { get; } = [];

    public bool ShowConvergence => false;
    public string IterationsText => "—";
    public string ResidualText => "—";

    public bool ShowRebarAreaNote => RebarAreaNote != "";
    public string RebarAreaNote { get; } = "";

    public FemSectionSummaryVM(
        FemSectionStateRequest request,
        FemRecordedSectionSummary summary,
        IReadOnlyDictionary<int, (double StressPa, double Strain)> recorded)
    {
        TaskTag = string.Format(Loc.S("FemSectionStateSummaryTitle"),
            request.Location.SourceMemberTag, request.Location.IntegrationPoint);
        CreatedText = request.PositionLabel;
        StatusText = request.StepLabel;
        StatusBrush = request.Converged ? Brushes.Green : Brushes.Red;

        Eps0Text = $"{summary.Plane.e0:+0.000000;-0.000000}";
        KyText = $"{summary.Plane.ky:+0.0000e+00;-0.0000e+00}  1/м";
        KzText = $"{summary.Plane.kz:+0.0000e+00;-0.0000e+00}  1/м";

        NText = $"{summary.N / 1000:+0.000;-0.000} {Loc.S("UnitKN")}";
        MxText = $"{summary.Mx / 1000:+0.000;-0.000} {Loc.S("UnitKNm")}";
        MyText = $"{summary.My / 1000:+0.000;-0.000} {Loc.S("UnitKNm")}";

        HasExtremes = recorded.Count > 0;
        EpsMinText = HasExtremes ? $"{summary.EpsMin:+0.00000;-0.00000}" : "—";
        EpsMaxText = HasExtremes ? $"{summary.EpsMax:+0.00000;-0.00000}" : "—";

        foreach (var r in summary.Rebar)
            RebarRows.Add(new StrainSummaryVM.RebarRow(
                r.Num,
                (r.X * 1000).ToString("F1"),
                (r.Y * 1000).ToString("F1"),
                $"{r.Eps:+0.00000;-0.00000}",
                $"{r.SigmaMpa:+0.0;-0.0}"));
    }

    /// <summary>
    /// Сводка сечения стержня CSfea в точке (<see cref="Services.FemCsfeaBarSectionState"/>): плоскость и усилия интеграла
    /// фибр в осях сечения (рядом — записанные), расхождение — в состоянии, высота сжатой зоны, крайние εb, σb, εs, σs и
    /// допущение о памяти трещин — в примечании.
    /// </summary>
    public FemSectionSummaryVM(string title, string stepLabel, Services.FemCsfeaBarSectionPoint s)
    {
        TaskTag = title;
        CreatedText = stepLabel;
        double d = s.Discrepancy;
        StatusText = string.Format(Loc.S("FemCsfeaSectionEquilibrium"), (d * 100).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture));
        StatusBrush = d < 0.05 ? Brushes.Green : Brushes.DarkOrange;

        Eps0Text = $"{s.Plane.e0:+0.000000;-0.000000}";
        KyText = $"{s.Plane.ky:+0.0000e+00;-0.0000e+00}  1/м";
        KzText = $"{s.Plane.kz:+0.0000e+00;-0.0000e+00}  1/м";

        string Pair(double v, double rec, string unit) =>
            $"{v:+0.000;-0.000} {Loc.S(unit)}  ({string.Format(Loc.S("FemCsfeaSectionRecorded"), rec.ToString("+0.000;-0.000", System.Globalization.CultureInfo.CurrentCulture))})";
        NText = Pair(s.N, s.RecordedN, "UnitKN");
        MxText = Pair(s.Mx, s.RecordedMx, "UnitKNm");
        MyText = Pair(s.My, s.RecordedMy, "UnitKNm");

        HasExtremes = true;
        EpsMinText = $"{s.EpsMin:+0.00000;-0.00000}";
        EpsMaxText = $"{s.EpsMax:+0.00000;-0.00000}";

        foreach (var r in s.Rebar)
            RebarRows.Add(new StrainSummaryVM.RebarRow(r.Num, (r.X * 1000).ToString("F1"), (r.Y * 1000).ToString("F1"),
                $"{r.Eps:+0.00000;-0.00000}", $"{r.SigmaMpa:+0.0;-0.0}"));

        var c = System.Globalization.CultureInfo.CurrentCulture;
        RebarAreaNote = string.Format(c, Loc.S("FemCsfeaSectionNote"), s.CompressedHeight * 1000, s.EpsB, s.SigmaB, s.EpsS, s.SigmaS)
                        + (s.Cracked ? " " + Loc.S("FemCsfeaSectionCracked") : "")
                        + (s.CrackFromFlag ? " " + Loc.S("FemCsfeaSectionCrackFromFlag") : "")
                        + (s.HasPsi ? " " + string.Format(c, Loc.S("FemCsfeaSectionPsi"), s.PsiMin) : "")
                        + " " + Loc.S("FemCsfeaSectionAssumption");
    }
}
