using System;
using System.Globalization;
using System.Text.Json;
using System.Windows.Media;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Сводка результата задачи «Плоскость деформаций пластины».</summary>
public sealed class ShellStrainSummaryVM
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public string TaskTag      { get; }
    public string StatusText   { get; }
    public Brush  StatusBrush  { get; }

    // Мембранные деформации
    public string Eps0xText    { get; }
    public string Eps0yText    { get; }
    public string Gamma0xyText { get; }

    // Кривизны
    public string KxText  { get; }
    public string KyText  { get; }
    public string KxyText { get; }

    // Деформации на гранях
    public string FaceSectionHeader { get; }
    public string EpsXTopText { get; }
    public string EpsXBotText { get; }
    public string EpsYTopText { get; }
    public string EpsYBotText { get; }

    // Усилия: задано → найдено
    public string NxRow  { get; }
    public string NyRow  { get; }
    public string NxyRow { get; }
    public string MxRow  { get; }
    public string MyRow  { get; }
    public string MxyRow { get; }

    // Секущие жёсткости
    public string EAxSecText  { get; }
    public string EAySecText  { get; }
    public string ZcxSecText  { get; }
    public string ZcySecText  { get; }
    public string EIxcSecText { get; }
    public string EIycSecText { get; }

    // Упругие жёсткости
    public string EAxElText  { get; }
    public string EAyElText  { get; }
    public string ZcxElText  { get; }
    public string ZcyElText  { get; }
    public string EIxcElText { get; }
    public string EIycElText { get; }

    // Коэффициенты снижения
    public string PhiEAxText  { get; }
    public string PhiEAyText  { get; }
    public string PhiEIxcText { get; }
    public string PhiEIycText { get; }

    // Сходимость
    public string IterationsText { get; }
    public string ResidualText   { get; }

    // Проверка прочности (для shell_layered_uls; отсутствует у shell_strain_state)
    public bool   HasCheck      { get; }
    public string CheckHeader   { get; } = "";
    public string VerdictText   { get; } = "";
    public Brush  VerdictBrush  { get; } = Brushes.Gray;
    public string CheckFormula  { get; } = "";
    public string CheckNote     { get; } = "";

    public ShellStrainSummaryVM(CalcResult r)
    {
        TaskTag   = r.TaskTag;
        StatusText = r.Status switch
        {
            "ok"            => "Сошлось ✓",
            "not_converged" => "НЕ СОШЛОСЬ ✗",
            "fail"          => Loc.S("ShellStrainCheckVerdictFail"),
            "error"         => "Ошибка ✗",
            _               => r.Status,
        };
        StatusBrush = r.Status == "ok" ? Brushes.Green
                    : r.Status == "not_converged" ? Brushes.DarkOrange
                    : r.Status == "fail" ? Brushes.Red
                    : Brushes.Red;

        string Dash = "—";
        IterationsText = ResidualText = Dash;
        Eps0xText = Eps0yText = Gamma0xyText = Dash;
        KxText = KyText = KxyText = Dash;
        FaceSectionHeader = "Деформации на гранях";
        EpsXTopText = EpsXBotText = EpsYTopText = EpsYBotText = Dash;
        NxRow = NyRow = NxyRow = MxRow = MyRow = MxyRow = Dash;
        EAxSecText  = EAySecText  = ZcxSecText  = ZcySecText  = EIxcSecText  = EIycSecText  = Dash;
        EAxElText   = EAyElText   = ZcxElText   = ZcyElText   = EIxcElText   = EIycElText   = Dash;
        PhiEAxText  = PhiEAyText  = PhiEIxcText = PhiEIycText = Dash;

        try
        {
            var root = JsonDocument.Parse(r.DataJson).RootElement;
            if (root.TryGetProperty("error", out _)) return;

            double G(string k) => root.TryGetProperty(k, out var v) ? v.GetDouble() : 0.0;
            int    Gi(string k) => root.TryGetProperty(k, out var v) ? v.GetInt32() : 0;

            string E(double x) => x.ToString("E3", Inv);
            string F2(double x) => x.ToString("F2", Inv);
            string N1(double x) => x.ToString("N1", Inv);
            string N3(double x) => x.ToString("N3", Inv);

            // ── Деформации и кривизны ────────────────────────────────────────
            IterationsText = Gi("iterations").ToString(Inv);
            ResidualText   = E(G("residual"));
            double eps0x = G("eps0x"), eps0y = G("eps0y"), g0xy = G("gamma0xy");
            double kx = G("kx"), ky = G("ky"), kxy = G("kxy");
            double h = G("section_h");

            Eps0xText    = E(eps0x);
            Eps0yText    = E(eps0y);
            Gamma0xyText = E(g0xy);
            KxText       = E(kx);
            KyText       = E(ky);
            KxyText      = E(kxy);

            // ── Деформации на гранях ──────────────────────────────────────────
            if (h > 0)
            {
                double h2 = h / 2.0;
                FaceSectionHeader = $"Деформации на гранях  (h = {h * 1000:F0} мм)";
                EpsXTopText = E(eps0x + kx *  h2);
                EpsXBotText = E(eps0x + kx * -h2);
                EpsYTopText = E(eps0y + ky *  h2);
                EpsYBotText = E(eps0y + ky * -h2);
            }

            // ── Усилия ────────────────────────────────────────────────────────
            string ForceRow(string tk, string rk, string unit)
            {
                double tv = G(tk), rv = G(rk);
                string pct = Math.Abs(tv) > 1e-9
                    ? $"({(rv - tv) / Math.Abs(tv) * 100:+0.00;-0.00;0.00}%)"
                    : "(—)";
                return $"{tv:+0.000;-0.000;0.000} → {rv:+0.000;-0.000;0.000}  {unit}  {pct}";
            }

            NxRow  = ForceRow("Nx_target",  "Nx_result",  "кН/м");
            NyRow  = ForceRow("Ny_target",  "Ny_result",  "кН/м");
            NxyRow = ForceRow("Nxy_target", "Nxy_result", "кН/м");
            MxRow  = ForceRow("Mx_target",  "Mx_result",  "кН·м/м");
            MyRow  = ForceRow("My_target",  "My_result",  "кН·м/м");
            MxyRow = ForceRow("Mxy_target", "Mxy_result", "кН·м/м");

            // ── Секущие жёсткости ─────────────────────────────────────────────
            EAxSecText  = N1(G("EAx_sec"));
            EAySecText  = N1(G("EAy_sec"));
            ZcxSecText  = F2(G("zc_x_sec"));
            ZcySecText  = F2(G("zc_y_sec"));
            EIxcSecText = N3(G("EIxc_sec"));
            EIycSecText = N3(G("EIyc_sec"));

            // ── Упругие жёсткости ─────────────────────────────────────────────
            EAxElText   = N1(G("EAx_el"));
            EAyElText   = N1(G("EAy_el"));
            ZcxElText   = F2(G("zc_x_el"));
            ZcyElText   = F2(G("zc_y_el"));
            EIxcElText  = N3(G("EIxc_el"));
            EIycElText  = N3(G("EIyc_el"));

            // ── Коэффициенты снижения ─────────────────────────────────────────
            string Phi(string k)
            {
                double v = G(k);
                return v > 0 ? v.ToString("F4", Inv) : Dash;
            }
            PhiEAxText  = Phi("phi_EAx");
            PhiEAyText  = Phi("phi_EAy");
            PhiEIxcText = Phi("phi_EIxc");
            PhiEIycText = Phi("phi_EIyc");

            // ── Блок проверки прочности (только для shell_layered_uls) ──────────
            if (root.TryGetProperty("check", out var chk))
            {
                HasCheck = true;
                CheckHeader = Loc.S("ShellStrainCheckHeader");
                bool passed = chk.TryGetProperty("passed", out var pv) && pv.GetBoolean();
                string verdict = chk.TryGetProperty("verdict", out var vv) ? vv.GetString() ?? "" : "";
                VerdictText  = $"{CheckHeader}: {verdict}";
                VerdictBrush = passed ? Brushes.Green : Brushes.Red;
                CheckFormula = chk.TryGetProperty("formula", out var ff) ? ff.GetString() ?? "" : "";
                CheckNote    = chk.TryGetProperty("note", out var nn)    ? nn.GetString() ?? "" : "";
            }
        }
        catch { /* повреждённый JSON — поля остаются "—" */ }
    }
}
