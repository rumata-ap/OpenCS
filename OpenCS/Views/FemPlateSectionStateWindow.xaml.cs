using System.Globalization;
using System.Windows;
using System.Windows.Media;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.Views.Helpers;

namespace OpenCS.Views;

/// <summary>
/// Немодальное окно «НДС сечения» пластины по результату CSfea (спека НДС сечений, п. 2.3): сводка (усилия по слоям и
/// записанные, расхождение, трещины, ψs), эпюры ε и σ по толщине по осям x, y выдачи и по направлению главного момента,
/// напряжения арматуры по слоям.
/// </summary>
public partial class FemPlateSectionStateWindow : Window
{
    public sealed record ForceRow(string Name, string Value, string Recorded, string Strain);
    public sealed record RebarRow(int Layer, string Direction, string Z, string Area, string Eps, string Sigma, string Psi);

    public FemPlateSectionStateWindow() => InitializeComponent();

    /// <summary>Обновляет содержимое окна под выбранную пластину.</summary>
    public void ShowContent(FemCsfeaPlateSectionPoint s, string title, string stepLabel, string windowTitle)
    {
        var c = CultureInfo.CurrentCulture;
        Title = windowTitle;
        TitleText.Text = title;
        StepText.Text = stepLabel;
        double d = s.Discrepancy;
        StatusText.Text = string.Format(Loc.S("FemCsfeaSectionEquilibrium"), (d * 100).ToString("0.#", c));
        StatusText.Foreground = d < 0.05 ? Brushes.Green : Brushes.DarkOrange;

        string[] names = ["Nx", "Ny", "Nxy", "Mx", "My", "Mxy"], strains = ["εx", "εy", "γxy", "κx", "κy", "κxy"];
        ForceGrid.ItemsSource = Enumerable.Range(0, 6).Select(i =>
        {
            string unit = Loc.S(i < 3 ? "UnitKNPerM" : "UnitKNmPerM");
            return new ForceRow(names[i], $"{s.Forces[i].ToString("+0.000;-0.000", c)} {unit}", $"{s.Recorded[i].ToString("+0.000;-0.000", c)} {unit}",
                $"{strains[i]} = {s.Strains[i].ToString(i < 3 ? "+0.000000;-0.000000" : "+0.0000e+00;-0.0000e+00", c)}{(i < 3 ? "" : " 1/м")}");
        }).ToList();

        string Fraction(double v) => double.IsFinite(v) ? v.ToString("P0", c) : "—";
        string Psi(double v) => double.IsFinite(v) ? v.ToString("0.###", c) : "—";
        var note = string.Format(c, Loc.S("FemCsfeaPlateSummary"), s.DirectionDeg, s.DirectionMoment, Fraction(s.CrackBottom),
            Fraction(s.CrackTop), Fraction(s.RecordedCrackBottom), Fraction(s.RecordedCrackTop), Psi(s.PsiMin), Psi(s.RecordedPsiMin));
        if (Math.Abs(s.ForceAngleDeg) > 1e-9) note += Environment.NewLine + string.Format(c, Loc.S("FemCsfeaPlateAngleNote"), s.ForceAngleDeg);
        NoteText.Text = note + Environment.NewLine + Loc.S("FemCsfeaPlateAssumption");

        RebarGrid.ItemsSource = s.Rebar.Select(r => new RebarRow(r.Layer, Loc.S(r.AlongX ? "FemCsfeaPlateDirX" : "FemCsfeaPlateDirY"),
            (r.Z * 1000).ToString("0.0", c), (r.Area * 1e4).ToString("0.00", c), r.Eps.ToString("+0.00000;-0.00000", c),
            r.SigmaMpa.ToString("+0.0;-0.0", c), r.HasEpsCrc ? r.Psi.ToString("0.###", c) : "—")).ToList();

        FillProfiles(s);
    }

    void FillProfiles(FemCsfeaPlateSectionPoint s)
    {
        var e = s.Strains;
        double a = s.DirectionDeg * Math.PI / 180, ca = Math.Cos(a), sa = Math.Sin(a);
        double Ex(double z) => e[0] + z * e[3];
        double Ey(double z) => e[1] + z * e[4];
        double Ed(double z) => Ex(z) * ca * ca + Ey(z) * sa * sa + (e[2] + z * e[5]) * sa * ca;

        // Деформации линейны по толщине — по границам слоёв; напряжения постоянны в слое — ступенями.
        var layers = s.Layers.OrderBy(l => l.Z).ToList();
        var edges = layers.Select(l => l.Z - l.Dz / 2).Append(layers[^1].Z + layers[^1].Dz / 2).ToArray();
        var steps = layers.SelectMany(l => new[] { l.Z - l.Dz / 2, l.Z + l.Dz / 2 }).ToArray();
        double[] Step(Func<FemCsfeaPlateLayer, double> v) => layers.SelectMany(l => new[] { v(l), v(l) }).ToArray();

        // Арматура — на эпюрах своего направления: оси сечения повёрнуты к осям выдачи на −угол.
        bool Along(bool alongX, double dirDeg)
        {
            double diff = ((alongX ? -s.ForceAngleDeg : 90 - s.ForceAngleDeg) - dirDeg) % 180;
            if (diff < 0) diff += 180;
            return diff < 0.5 || diff > 179.5;
        }
        (double, double, Brush)[] Rebar(double dirDeg, bool stress) => s.Rebar.Where(r => Along(r.AlongX, dirDeg))
            .Select(r => (r.Z, stress ? r.SigmaMpa : r.Eps, (Brush)(r.AlongX ? Brushes.DarkRed : Brushes.DarkBlue))).ToArray();

        string dir = string.Format(CultureInfo.CurrentCulture, Loc.S("FemCsfeaPlateDirection"), s.DirectionDeg);
        EpsX.Profile = new ThroughThicknessProfile
        {
            Z = edges, Title = Loc.S("ShellStrainEpsXPlot"), ValueAxisLabel = "ε",
            Series = [("εx", edges.Select(Ex).ToArray(), Brushes.Crimson)], Points = Rebar(0, false),
        };
        EpsY.Profile = new ThroughThicknessProfile
        {
            Z = edges, Title = Loc.S("ShellStrainEpsYPlot"), ValueAxisLabel = "ε",
            Series = [("εy", edges.Select(Ey).ToArray(), Brushes.SteelBlue)], Points = Rebar(90, false),
        };
        EpsDir.Profile = new ThroughThicknessProfile
        {
            Z = edges, Title = "ε(z) " + dir, ValueAxisLabel = "ε",
            Series = [("ε", edges.Select(Ed).ToArray(), Brushes.DarkOrange)], Points = Rebar(s.DirectionDeg, false),
        };
        SigX.Profile = new ThroughThicknessProfile
        {
            Z = steps, Title = Loc.S("ShellStrainSigXPlot"), ValueAxisLabel = "МПа",
            Series = [("σx", Step(l => l.SigX), Brushes.Crimson)], Points = Rebar(0, true), PointsInSeparateZone = true,
        };
        SigY.Profile = new ThroughThicknessProfile
        {
            Z = steps, Title = Loc.S("ShellStrainSigYPlot"), ValueAxisLabel = "МПа",
            Series = [("σy", Step(l => l.SigY), Brushes.SteelBlue)], Points = Rebar(90, true), PointsInSeparateZone = true,
        };
        SigDir.Profile = new ThroughThicknessProfile
        {
            Z = steps, Title = "σ(z) " + dir + ", МПа", ValueAxisLabel = "МПа",
            Series = [("σ", Step(l => l.SigDir), Brushes.DarkOrange)], Points = Rebar(s.DirectionDeg, true),
            PointsInSeparateZone = true,
        };
    }
}
