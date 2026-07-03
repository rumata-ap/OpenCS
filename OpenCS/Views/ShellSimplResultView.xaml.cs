using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Views;

public partial class ShellSimplResultView : System.Windows.Controls.UserControl
{
    public ShellSimplResultView(CalcResult result, CalcTask task)
    {
        InitializeComponent();
        DataContext = new ShellSimplResultVM(result, task);
    }
}

public class ShellSimplStripRow : ViewModelBase
{
    public string Name { get; set; } = "";
    public double M_des { get; set; }
    public double N_des { get; set; }
    public double Sigma_s_MPa { get; set; }
    public string SigmaDisplay => Math.Round(Sigma_s_MPa, 2).ToString("F2");
    public double Xm { get; set; }
    public bool NoRebar { get; set; }

    // SLS
    public double Acrc_mm { get; set; }
    public string AcrcDisplay => Math.Round(Acrc_mm, 2).ToString("F2");
    public double Psi_s { get; set; }
    public double B_kNm2 { get; set; }
    public bool ShowSls { get; set; }

    // ULS
    public double Xi { get; set; }
    public double Xi_R { get; set; }
    public double M_ult { get; set; }
    public double Eta { get; set; }
    public string EtaDisplay => Eta >= 1e9 ? "∞" : Math.Round(Eta, 2).ToString("F2");
    public string Case { get; set; } = "";
    public bool ShowUls { get; set; }

    public string StatusText { get; set; } = "";
    public bool StatusOk { get; set; }
}

public class ShellSimplDirectionRow : ViewModelBase
{
    public double Alpha_deg { get; set; }
    public double M_n { get; set; }
    public double N_n { get; set; }
    public string Face => Top ? "верх" : "низ";
    public bool Top { get; set; }
    public double Sigma_s_MPa { get; set; }
    public string SigmaDisplay => Math.Round(Sigma_s_MPa, 2).ToString("F2");
    public double Acrc_mm { get; set; }
    public string AcrcDisplay => Math.Round(Acrc_mm, 2).ToString("F2");
    public double AcrcLimMm { get; set; }
    public double Eta { get; set; }
    public string EtaDisplay => Eta >= 1e9 ? "∞" : Math.Round(Eta, 2).ToString("F2");
    public bool ShowSlsCol { get; set; }
    public bool ShowUlsCol { get; set; }

    public string StatusText { get; set; } = "";
    public bool StatusOk { get; set; }
    public bool IsCritical { get; set; }
}

public class ShellSimplResultVM : ViewModelBase
{
    public string Title { get; set; } = "";
    public double Nx { get; set; } public double Ny { get; set; } public double Nxy { get; set; }
    public double Mx { get; set; } public double My { get; set; } public double Mxy { get; set; }

    public string StripHeader { get; set; } = "";
    public bool HasStrips => Strips.Count > 0;
    public ObservableCollection<ShellSimplStripRow> Strips { get; } = [];

    public bool HasCapri => Directions.Count > 0;
    public ObservableCollection<ShellSimplDirectionRow> Directions { get; } = [];

    public bool HasCriticalTop => CritTopAlpha != null;
    public string? CritTopAlpha { get; set; }
    public double? CritTopAcrc { get; set; }
    public string? CritTopAcrcDisplay => CritTopAcrc.HasValue ? Math.Round(CritTopAcrc.Value, 2).ToString("F2") : null;
    public double? CritTopSigma { get; set; }
    public string? CritTopSigmaDisplay => CritTopSigma.HasValue ? Math.Round(CritTopSigma.Value, 2).ToString("F2") : null;

    public bool HasCriticalBot => CritBotAlpha != null;
    public string? CritBotAlpha { get; set; }
    public double? CritBotAcrc { get; set; }
    public string? CritBotAcrcDisplay => CritBotAcrc.HasValue ? Math.Round(CritBotAcrc.Value, 2).ToString("F2") : null;
    public double? CritBotSigma { get; set; }
    public string? CritBotSigmaDisplay => CritBotSigma.HasValue ? Math.Round(CritBotSigma.Value, 2).ToString("F2") : null;

    public bool HasEtaMax => EtaMax.HasValue;
    public double? EtaMax { get; set; }
    public string EtaMaxDisplay => EtaMax.HasValue
        ? (EtaMax.Value >= 1e9 ? "∞" : Math.Round(EtaMax.Value, 2).ToString("F2"))
        : "";
    public double AcrcLimMm { get; set; }

    public string? CritTopEtaDisplay { get; set; }
    public string? CritBotEtaDisplay { get; set; }

    public bool OverallOk { get; set; }
    public string OverallStatus { get; set; } = "";
    public Brush OverallStatusBrush { get; set; } = Brushes.LightGray;

    public ShellSimplResultVM(CalcResult result, CalcTask task)
    {
        bool isSls = task.Kind.EndsWith("sls");
        bool isWa = task.Kind.StartsWith("shell_simpl_wa_");
        Title = task.Tag;

        var doc = JsonSerializer.Deserialize<JsonElement>(result.DataJson);
        var forces = doc.GetProperty("Forces");

        Nx = forces.GetProperty("Nx").GetDouble();
        Ny = forces.GetProperty("Ny").GetDouble();
        Nxy = forces.GetProperty("Nxy").GetDouble();
        Mx = forces.GetProperty("Mx").GetDouble();
        My = forces.GetProperty("My").GetDouble();
        Mxy = forces.GetProperty("Mxy").GetDouble();

        AcrcLimMm = forces.TryGetProperty("AcrcLimMm", out var al)
            ? al.GetDouble() : 0.3;

        bool allOk = true;

        if (doc.TryGetProperty("WaStrips", out var wa) && wa.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            StripHeader = "Полосы (Wood & Armer)";
            foreach (var s in wa.EnumerateArray())
            {
                var row = new ShellSimplStripRow
                {
                    Name = s.GetProperty("Name").GetString() ?? "",
                    M_des = s.GetProperty("M_des").GetDouble(),
                    N_des = s.GetProperty("N_des").GetDouble(),
                    Sigma_s_MPa = s.GetProperty("Sigma_s_MPa").GetDouble(),
                    Xm = s.GetProperty("Xm").GetDouble(),
                    NoRebar = s.GetProperty("NoRebar").GetBoolean(),
                    ShowSls = isSls, ShowUls = !isSls,
                };
                if (isSls)
                {
                    row.Acrc_mm = s.GetProperty("Acrc_mm").GetDouble();
                    row.Psi_s = s.GetProperty("Psi_s").GetDouble();
                    row.B_kNm2 = s.GetProperty("B_kNm2").GetDouble();
                    bool ok = row.NoRebar || row.Acrc_mm <= AcrcLimMm;
                    row.StatusOk = ok;
                    row.StatusText = ok ? "Выполняется" : "Не выполняется";
                    if (!ok) allOk = false;
                }
                else
                {
                    s.TryGetProperty("Xi", out var xi);
                    row.Xi = xi.ValueKind == System.Text.Json.JsonValueKind.Number ? xi.GetDouble() : 0;
                    s.TryGetProperty("Xi_R", out var xiR);
                    row.Xi_R = xiR.ValueKind == System.Text.Json.JsonValueKind.Number ? xiR.GetDouble() : 0;
                    s.TryGetProperty("M_ult", out var mu);
                    row.M_ult = mu.ValueKind == System.Text.Json.JsonValueKind.Number ? mu.GetDouble() : 0;
                    s.TryGetProperty("Eta", out var et);
                    row.Eta = et.ValueKind == System.Text.Json.JsonValueKind.Number ? et.GetDouble() : double.MaxValue;
                    s.TryGetProperty("Case", out var ca);
                    row.Case = ca.ValueKind == System.Text.Json.JsonValueKind.String ? ca.GetString() ?? "" : "";
                    bool ok = row.NoRebar || row.Eta <= 1.0;
                    row.StatusOk = ok;
                    row.StatusText = ok ? "Выполняется" : "Не выполняется";
                    if (!ok) allOk = false;
                }
                Strips.Add(row);
            }
        }

        if (doc.TryGetProperty("CapriDirs", out var dirs) && dirs.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            bool showSlsCol = isSls, showUlsCol = !isSls;
            foreach (var d in dirs.EnumerateArray())
            {
                var strip = d.GetProperty("Strip");
                bool noRebar = strip.GetProperty("NoRebar").GetBoolean();
                double acrc = isSls ? strip.GetProperty("Acrc_mm").GetDouble() : 0;
                double eta = !isSls ? strip.GetProperty("Eta").GetDouble() : 0;
                bool ok = noRebar || (isSls
                    ? (acrc <= AcrcLimMm)
                    : (eta <= 1.0));
                if (!ok) allOk = false;

                Directions.Add(new ShellSimplDirectionRow
                {
                    Alpha_deg = Math.Round(d.GetProperty("Alpha_deg").GetDouble(), 1),
                    M_n = Math.Round(d.GetProperty("M_n").GetDouble(), 4),
                    N_n = Math.Round(d.GetProperty("N_n").GetDouble(), 4),
                    Top = d.GetProperty("Top").GetBoolean(),
                    Sigma_s_MPa = strip.GetProperty("Sigma_s_MPa").GetDouble(),
                    Acrc_mm = acrc,
                    AcrcLimMm = AcrcLimMm,
                    Eta = eta,
                    ShowSlsCol = showSlsCol, ShowUlsCol = showUlsCol,
                    StatusOk = ok,
                    StatusText = ok ? "Выполняется" : "Не выполняется",
                });
            }

            // Подсветить критические направления (макс. acrc для SLS, макс. η для ULS)
            if (isSls)
            {
                var maxAcrc = Directions.Where(d => !d.StatusOk).DefaultIfEmpty().MaxBy(d => d?.Acrc_mm);
                if (maxAcrc != null) maxAcrc.IsCritical = true;
            }
            else
            {
                var maxEta = Directions.Where(d => !d.StatusOk).DefaultIfEmpty().MaxBy(d => d?.Eta);
                if (maxEta == null)
                    maxEta = Directions.DefaultIfEmpty().MaxBy(d => d?.Eta);
                if (maxEta != null) maxEta.IsCritical = true;
            }

            if (doc.TryGetProperty("CriticalTop", out var ct) && ct.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                var cs = ct.GetProperty("Strip");
                if (cs.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    CritTopAlpha = ct.GetProperty("Alpha_deg").GetDouble().ToString("F1");
                    CritTopAcrc = isSls ? cs.GetProperty("Acrc_mm").GetDouble() : null;
                    CritTopSigma = cs.GetProperty("Sigma_s_MPa").GetDouble();
                    if (!isSls)
                    {
                        double e = cs.GetProperty("Eta").GetDouble();
                        CritTopEtaDisplay = e >= 1e9 ? "∞" : Math.Round(e, 2).ToString("F2");
                    }
                }
            }
            if (doc.TryGetProperty("CriticalBot", out var cb) && cb.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                var cs = cb.GetProperty("Strip");
                if (cs.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    CritBotAlpha = cb.GetProperty("Alpha_deg").GetDouble().ToString("F1");
                    CritBotAcrc = isSls ? cs.GetProperty("Acrc_mm").GetDouble() : null;
                    CritBotSigma = cs.GetProperty("Sigma_s_MPa").GetDouble();
                    if (!isSls)
                    {
                        double e = cs.GetProperty("Eta").GetDouble();
                        CritBotEtaDisplay = e >= 1e9 ? "∞" : Math.Round(e, 2).ToString("F2");
                    }
                }
            }
        }

        if (doc.TryGetProperty("EtaMax", out var em) && em.ValueKind == System.Text.Json.JsonValueKind.Number)
            EtaMax = em.GetDouble();

        OverallOk = allOk;
        OverallStatus = allOk ? "Все условия выполнены" : "Обнаружены нарушения";
        OverallStatusBrush = allOk
            ? new SolidColorBrush(Color.FromArgb(70, 80, 180, 80))
            : new SolidColorBrush(Color.FromArgb(60, 192, 57, 43));
    }
}
