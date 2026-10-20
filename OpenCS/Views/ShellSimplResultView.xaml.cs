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
    /// <param name="plate">Сечение задачи — для блока исходных данных; null — без него.</param>
    public ShellSimplResultView(CalcResult result, CalcTask task, PlateSection? plate = null)
    {
        InitializeComponent();
        var vm = new ShellSimplResultVM(result, task, plate);
        DataContext = vm;
        // Колонки DataGrid не наследуют DataContext — видимость колонок НДМ задаётся здесь.
        if (vm.HasConstraints)
        {
            TopConstraintsCanvas.SetData(vm.TopPoints, vm.TopAsx, vm.TopAsy);
            BotConstraintsCanvas.SetData(vm.BotPoints, vm.BotAsx, vm.BotAsy);
        }
        if (vm.IsNdm)
        {
            EpsBColumn.Visibility = EpsSColumn.Visibility = System.Windows.Visibility.Visible;
            if (!task.Kind.EndsWith("sls")) PivotColumn.Visibility = System.Windows.Visibility.Visible;
        }
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
    public string AcrcDisplay => ShellSimplNdmFormat.Acrc(Acrc_mm);
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

    // НДМ полосы
    public bool IsNdm { get; set; }
    public string Pivot { get; set; } = "";
    public double Lambda { get; set; }
    public double EpsB { get; set; }
    public double EpsS_t { get; set; }
    public string NdmDisplay => ShellSimplNdmFormat.Strip(ShowUls, Pivot, Lambda, EpsB, EpsS_t);
}

/// <summary>Подписи величин НДМ полосы: деформации в ‰, λ и шарнир — только для ПС1.</summary>
static class ShellSimplNdmFormat
{
    public static string PerMille(double eps) => (eps * 1000.0).ToString("F3");
    /// <summary>a_crc, мм; ≥ 1e9 — сечение с трещиной не уравновешивается (∞).</summary>
    public static string Acrc(double mm) => mm >= 1e9 ? "∞" : Math.Round(mm, 2).ToString("F2");
    public static string LambdaText(double lambda) => double.IsFinite(lambda) && lambda < 1e9 ? lambda.ToString("F3") : "∞";

    public static string Strip(bool uls, string pivot, double lambda, double epsB, double epsS) =>
        (uls ? $"{Loc.S("ShellSimplColPivot")}: {(pivot == "" ? "—" : pivot)}   λ: {LambdaText(lambda)}   " : "")
        + $"εb: {PerMille(epsB)} ‰   εs: {PerMille(epsS)} ‰";
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
    public string AcrcDisplay => ShellSimplNdmFormat.Acrc(Acrc_mm);
    public double AcrcLimMm { get; set; }
    public double Eta { get; set; }
    public string EtaDisplay => Eta >= 1e9 ? "∞" : Math.Round(Eta, 2).ToString("F2");
    public bool ShowSlsCol { get; set; }
    public bool ShowUlsCol { get; set; }

    public string StatusText { get; set; } = "";
    public bool StatusOk { get; set; }
    public bool IsCritical { get; set; }

    // НДМ полосы
    public string Pivot { get; set; } = "";
    public double EpsB { get; set; }
    public double EpsS_t { get; set; }
    public string EpsBDisplay => ShellSimplNdmFormat.PerMille(EpsB);
    public string EpsSDisplay => ShellSimplNdmFormat.PerMille(EpsS_t);
    /// <summary>Пара ортогональных фасеток (θ, θ + 90°): θ mod 90.</summary>
    public double PairAlpha => Alpha_deg % 90.0;
}

/// <summary>Фасетка проверки Капра–Мори через ограничения: площади — см²/м.</summary>
public sealed class ShellSimplConstraintRow
{
    public string Theta { get; init; } = "";
    public string N { get; init; } = "";
    public string M { get; init; } = "";
    public string RTop { get; init; } = "";
    public string AsNTop { get; init; } = "";
    public string EtaTop { get; init; } = "";
    public string RBot { get; init; } = "";
    public string AsNBot { get; init; } = "";
    public string EtaBot { get; init; } = "";
    public bool StatusOk { get; init; }
    public string StatusText => StatusOk ? "Выполняется" : "Не выполняется";
    public bool IsCritical { get; set; }
}

public class ShellSimplResultVM : ViewModelBase
{
    public string Title { get; set; } = "";
    public double Nx { get; set; } public double Ny { get; set; } public double Nxy { get; set; }
    public double Mx { get; set; } public double My { get; set; } public double Mxy { get; set; }
    public double Qx { get; set; } public double Qy { get; set; }

    /// <summary>Усилия строкой с единицами (кН/м, кН·м/м).</summary>
    public string ForcesText => string.Join("   ",
        $"Nx = {Nx:F2}", $"Ny = {Ny:F2}", $"Nxy = {Nxy:F2} кН/м;",
        $"Mx = {Mx:F2}", $"My = {My:F2}", $"Mxy = {Mxy:F2} кН·м/м",
        Qx != 0 || Qy != 0 ? $";   Qx = {Qx:F2}   Qy = {Qy:F2} кН/м" : "");

    /// <summary>Исходные данные: откуда задача, сечение и армирование, параметры проверки.</summary>
    public ObservableCollection<string> InputLines { get; } = [];
    public bool HasInputs => InputLines.Count > 0;

    public string StripHeader { get; set; } = "";
    public bool HasStrips => Strips.Count > 0;
    public ObservableCollection<ShellSimplStripRow> Strips { get; } = [];

    public bool HasCapri => Directions.Count > 0;
    public ObservableCollection<ShellSimplDirectionRow> Directions { get; } = [];

    public bool HasCriticalTop => CritTopAlpha != null;
    public string? CritTopAlpha { get; set; }
    public double? CritTopAcrc { get; set; }
    public string? CritTopAcrcDisplay => CritTopAcrc.HasValue ? ShellSimplNdmFormat.Acrc(CritTopAcrc.Value) : null;
    public double? CritTopSigma { get; set; }
    public string? CritTopSigmaDisplay => CritTopSigma.HasValue ? Math.Round(CritTopSigma.Value, 2).ToString("F2") : null;

    public bool HasCriticalBot => CritBotAlpha != null;
    public string? CritBotAlpha { get; set; }
    public double? CritBotAcrc { get; set; }
    public string? CritBotAcrcDisplay => CritBotAcrc.HasValue ? ShellSimplNdmFormat.Acrc(CritBotAcrc.Value) : null;
    public double? CritBotSigma { get; set; }
    public string? CritBotSigmaDisplay => CritBotSigma.HasValue ? Math.Round(CritBotSigma.Value, 2).ToString("F2") : null;

    public bool HasEtaMax => EtaMax.HasValue;
    public double? EtaMax { get; set; }
    // Кисп,max задачи ПС1 — наибольший из изгиба и поперечной силы; источник подписывается, иначе 1,03 от Q
    // читается рядом с Кисп фасеток как изгибный.
    public string? EtaMaxSource { get; set; }
    public string EtaMaxDisplay => EtaMax.HasValue
        ? (EtaMax.Value >= 1e9 ? "∞" : Math.Round(EtaMax.Value, 2).ToString("F2"))
          + (EtaMaxSource is { } src ? $" ({src})" : "")
        : "";
    public double AcrcLimMm { get; set; }

    public string? CritTopEtaDisplay { get; set; }
    public string? CritBotEtaDisplay { get; set; }

    /// <summary>Хотя бы одна полоса посчитана на НДМ.</summary>
    public bool IsNdm { get; set; }
    public string ModelNote => Loc.S(IsNdm ? "ShellSimplModelNdmNote" : "ShellSimplModelFormulasNote");

    /// <summary>Капра–Мори через ограничения: r(θ) и As,n(θ) по граням.</summary>
    public bool HasConstraints => Constraints.Count > 0;
    public ObservableCollection<ShellSimplConstraintRow> Constraints { get; } = [];
    public OpenCS.Views.Helpers.CapraMauryDemandPoint[] TopPoints { get; private set; } = [];
    public OpenCS.Views.Helpers.CapraMauryDemandPoint[] BotPoints { get; private set; } = [];
    /// <summary>Сетки сечения, см²/м (для кривой As,n(θ)).</summary>
    public double TopAsx { get; private set; }
    public double TopAsy { get; private set; }
    public double BotAsx { get; private set; }
    public double BotAsy { get; private set; }

    /// <summary>Поперечная сила (ПС1); null — Qx = Qy = 0 или задача ПС2.</summary>
    public PlateShearBlock? Shear { get; }
    public bool HasShear => Shear != null;

    public bool OverallOk { get; set; }
    public string OverallStatus { get; set; } = "";
    public Brush OverallStatusBrush { get; set; } = Brushes.LightGray;

    public ShellSimplResultVM(CalcResult result, CalcTask task, PlateSection? plate = null)
    {
        bool isSls = task.Kind.EndsWith("sls");
        bool isWa = task.Kind.StartsWith("shell_simpl_wa_");
        Title = task.Tag;
        FillInputs(task, plate, isSls);

        var doc = JsonSerializer.Deserialize<JsonElement>(result.DataJson);
        var forces = doc.GetProperty("Forces");

        Nx = forces.GetProperty("Nx").GetDouble();
        Ny = forces.GetProperty("Ny").GetDouble();
        Nxy = forces.GetProperty("Nxy").GetDouble();
        Mx = forces.GetProperty("Mx").GetDouble();
        My = forces.GetProperty("My").GetDouble();
        Mxy = forces.GetProperty("Mxy").GetDouble();
        if (forces.TryGetProperty("Qx", out var qx) && qx.ValueKind == JsonValueKind.Number) Qx = qx.GetDouble();
        if (forces.TryGetProperty("Qy", out var qy) && qy.ValueKind == JsonValueKind.Number) Qy = qy.GetDouble();

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
                if (IsNdmStrip(s))
                {
                    IsNdm = row.IsNdm = true;
                    row.Pivot = Str(s, "Pivot");
                    row.Lambda = Num(s, "Lambda");
                    row.EpsB = Num(s, "EpsB");
                    row.EpsS_t = Num(s, "EpsS_t");
                }
                if (isSls)
                {
                    row.Acrc_mm = s.GetProperty("Acrc_mm").GetDouble();
                    row.Psi_s = s.GetProperty("Psi_s").GetDouble();
                    row.B_kNm2 = s.GetProperty("B_kNm2").GetDouble();
                    bool ok = row.Acrc_mm <= AcrcLimMm;
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
                    bool ok = row.Eta <= 1.0;
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
                // ПС2: Кисп фасетки — acrc / acrc,ult (как в строке проверки по КЭ); без арматуры на грани — 0.
                double eta = !isSls ? strip.GetProperty("Eta").GetDouble()
                    : AcrcLimMm <= 1e-12 ? 0 : acrc >= 1e9 ? double.MaxValue : acrc / AcrcLimMm;
                bool ok = isSls ? acrc <= AcrcLimMm : eta <= 1.0;
                if (!ok) allOk = false;

                if (IsNdmStrip(strip)) IsNdm = true;
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
                    Pivot = Str(strip, "Pivot"),
                    EpsB = Num(strip, "EpsB"),
                    EpsS_t = Num(strip, "EpsS_t"),
                });
            }

            // НДМ: фасетки парами (θ, θ + 90°) — соседними строками.
            if (IsNdm)
            {
                var paired = Directions.OrderBy(r => Math.Round(r.PairAlpha, 6)).ThenBy(r => r.Alpha_deg).ToList();
                Directions.Clear();
                foreach (var r in paired) Directions.Add(r);
            }

            // Подсветить критические направления (макс. acrc для SLS, макс. Кисп для ULS)
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

        // Критическое направление грани: если решатель его не дал (всё проходит), — фасетка с наибольшим Кисп.
        if (Directions.Count > 0)
        {
            void Fill(bool top)
            {
                if (top ? CritTopAlpha != null : CritBotAlpha != null) return;
                if (Directions.Where(d => d.Top == top).MaxBy(d => d.Eta) is not { } w) return;
                string eta = w.EtaDisplay;
                if (top) { CritTopAlpha = w.Alpha_deg.ToString("F1"); CritTopSigma = w.Sigma_s_MPa; CritTopAcrc = isSls ? w.Acrc_mm : null; CritTopEtaDisplay = eta; }
                else { CritBotAlpha = w.Alpha_deg.ToString("F1"); CritBotSigma = w.Sigma_s_MPa; CritBotAcrc = isSls ? w.Acrc_mm : null; CritBotEtaDisplay = eta; }
            }
            Fill(true);
            Fill(false);
        }

        if (doc.TryGetProperty("EtaMax", out var em) && em.ValueKind == System.Text.Json.JsonValueKind.Number)
            EtaMax = em.GetDouble();

        // Через ограничения итог — Кисп = max r/As,n (таблица фасеток по проекции — для справки).
        if (doc.TryGetProperty("CapriConstraints", out var cons) && cons.ValueKind == JsonValueKind.Array
            && cons.GetArrayLength() > 0)
        {
            LoadConstraints(cons);
            allOk = (EtaMax ?? 0.0) <= 1.0;
        }

        // Кисп,Q входит в Кисп,max задачи ПС1.
        Shear = PlateShearBlock.From(doc, "Shear", design: false);
        if (Shear is { Ok: false }) allOk = false;
        if (EtaMax is double etaMax && doc.TryGetProperty("Shear", out var sh) && sh.ValueKind == JsonValueKind.Object
            && sh.TryGetProperty("Eta", out var shEta) && shEta.ValueKind == JsonValueKind.Number)
            EtaMaxSource = shEta.GetDouble() >= etaMax - 1e-12 ? "поперечная сила, Кисп,Q" : "изгиб";

        OverallOk = allOk;
        OverallStatus = allOk ? "Все условия выполнены" : "Обнаружены нарушения";

        static bool IsNdmStrip(JsonElement s) => Str(s, "Model") == "ndm";
        static string Str(JsonElement s, string name) =>
            s.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        static double Num(JsonElement s, string name) =>
            s.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0.0;
        OverallStatusBrush = allOk
            ? new SolidColorBrush(Color.FromArgb(70, 80, 180, 80))
            : new SolidColorBrush(Color.FromArgb(60, 192, 57, 43));
    }

    /// <summary>Исходные данные задачи: источник (проверка по КЭ или пакет), сечение и армирование, параметры.</summary>
    void FillInputs(CalcTask task, PlateSection? plate, bool isSls)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(task.ParamsJson) ? "{}" : task.ParamsJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("femCheckSource", out var f) && f.ValueKind == JsonValueKind.Object)
            {
                string S(string name) => f.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                string elem = f.TryGetProperty("elem", out var e) && e.ValueKind == JsonValueKind.Number ? $"КЭ {e.GetInt32()}" : "строка без КЭ";
                string source = S("source") switch
                {
                    "assigned" => Loc.S("FemCheckSourceAssigned"),
                    "selected" => Loc.S("FemCheckSourceSelected"),
                    "layout" => Loc.S("FemCheckSourceLayout"),
                    _ => Loc.S("FemCheckSourceSection"),
                };
                InputLines.Add(string.Format(Loc.S("ShellSimplInputFromFemCheck"), elem, S("set"), S("label"), source));
            }
            else if (root.TryGetProperty("batchSource", out var b) && b.ValueKind == JsonValueKind.Object
                     && b.TryGetProperty("num", out var num) && num.ValueKind == JsonValueKind.Number)
                InputLines.Add(string.Format(Loc.S("ShellSimplInputFromBatch"), num.GetInt32()));

            if (plate != null)
            {
                InputLines.Add(string.Format(Loc.S("ShellSimplInputSection"), plate.Tag, plate.H * 1000.0));
                foreach (var l in plate.RebarLayers)
                    InputLines.Add(string.Format(Loc.S("ShellSimplInputLayer"), l.Name,
                        l.Asx * 1e4, l.Zsx * 1000.0, l.Asy * 1e4, l.Zsy * 1000.0));
                if (plate.RebarLayers.Count == 0) InputLines.Add(Loc.S("ShellSimplInputNoRebar"));
            }

            var p = OpenCS.Tasks.ShellSimplParams.Parse(task.ParamsJson);
            InputLines.Add(isSls
                ? string.Format(Loc.S("ShellSimplInputSls"), p.Phi1, p.Phi2, p.AcrcLimMm, p.StepDeg)
                : string.Format(Loc.S("ShellSimplInputUls"), p.StepDeg));
        }
        catch (JsonException) { }
    }

    void LoadConstraints(JsonElement cons)
    {
        const double Cm2 = 1e4;
        static string A(double v) => (v * Cm2).ToString("F2");
        static string E(double v) => v >= 1e9 ? "∞" : v.ToString("F2");

        var items = cons.EnumerateArray().ToList();
        TopPoints = new OpenCS.Views.Helpers.CapraMauryDemandPoint[items.Count];
        BotPoints = new OpenCS.Views.Helpers.CapraMauryDemandPoint[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            var x = items[i];
            double theta = x.GetProperty("ThetaDeg").GetDouble();
            double rTop = x.GetProperty("RTop").GetDouble(), rBot = x.GetProperty("RBot").GetDouble();
            double pTop = x.GetProperty("AsNTop").GetDouble(), pBot = x.GetProperty("AsNBot").GetDouble();
            double eTop = x.GetProperty("EtaTop").GetDouble(), eBot = x.GetProperty("EtaBot").GetDouble();
            // Красные точки графика — фасетки, где требование больше заданной площади.
            TopPoints[i] = new(theta, rTop * Cm2, pTop * Cm2, eTop > 1.0);
            BotPoints[i] = new(theta, rBot * Cm2, pBot * Cm2, eBot > 1.0);
            Constraints.Add(new ShellSimplConstraintRow
            {
                Theta = theta.ToString("0.##"),
                N = x.GetProperty("N").GetDouble().ToString("F1"),
                M = x.GetProperty("M").GetDouble().ToString("F2"),
                RTop = A(rTop), AsNTop = A(pTop), EtaTop = E(eTop),
                RBot = A(rBot), AsNBot = A(pBot), EtaBot = E(eBot),
                StatusOk = Math.Max(eTop, eBot) <= 1.0,
            });
        }
        var worst = Constraints.Zip(items, (r, x) => (r, eta: x.GetProperty("Eta").GetDouble())).MaxBy(p => p.eta);
        worst.r.IsCritical = true;

        // Сетки по приведённым площадям: As,n(0°) = Asx, Asy — из фасетки с наибольшим sin²θ.
        static (double Asx, double Asy) Mesh(List<JsonElement> items, string name)
        {
            double asx = 0.0, vFar = 0.0, c2Far = 1.0, s2Far = 0.0;
            foreach (var x in items)
            {
                double a = x.GetProperty("ThetaDeg").GetDouble() * Math.PI / 180.0;
                double c2 = Math.Cos(a) * Math.Cos(a), s2 = Math.Sin(a) * Math.Sin(a);
                double v = x.GetProperty(name).GetDouble();
                if (Math.Abs(a) < 1e-12) asx = v;
                if (s2 > s2Far) { s2Far = s2; c2Far = c2; vFar = v; }
            }
            double asy = s2Far > 1e-12 ? (vFar - asx * c2Far) / s2Far : 0.0;
            return (asx * Cm2, Math.Max(0.0, asy) * Cm2);
        }
        (TopAsx, TopAsy) = Mesh(items, "AsNTop");
        (BotAsx, BotAsy) = Mesh(items, "AsNBot");
    }
}
