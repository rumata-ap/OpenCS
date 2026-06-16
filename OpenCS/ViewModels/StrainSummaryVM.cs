using CScore;
using OpenCS.Utilites;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;

namespace OpenCS.ViewModels
{
    /// <summary>ViewModel вкладки «Сводка» результата задачи strain_state.</summary>
    public class StrainSummaryVM : ViewModelBase
    {
        // ── Заголовок ─────────────────────────────────────────────────
        public string TaskTag     { get; }
        public string CreatedText { get; }
        public string StatusText  { get; }
        public Brush  StatusBrush { get; }

        // ── Плоскость деформаций ───────────────────────────────────────
        public string Eps0Text   { get; }
        public string KyText     { get; }
        public string KzText     { get; }

        // ── Усилия ────────────────────────────────────────────────────
        public string NText  { get; }
        public string MxText { get; }
        public string MyText { get; }

        // ── Экстремальные деформации ───────────────────────────────────
        public string EpsMinText { get; }
        public string EpsMaxText { get; }
        public bool   HasExtremes { get; }

        // ── Жёсткости ─────────────────────────────────────────────────
        public string XcText   { get; }
        public string YcText   { get; }
        public string EAText   { get; }
        public string EIy0Text { get; }
        public string EIz0Text { get; }
        public string EIycText { get; }
        public string EIzcText { get; }
        public bool   HasStiffness { get; }

        // ── Упругие жёсткости ─────────────────────────────────────────
        public string EAelText   { get; }
        public string EIyelText  { get; }
        public string EIzelText  { get; }
        public string PhiEAText  { get; }
        public string PhiEIyText { get; }
        public string PhiEIzText { get; }

        // ── Арматура ──────────────────────────────────────────────────
        public ObservableCollection<RebarRow> RebarRows { get; } = [];
        public bool HasRebar => RebarRows.Count > 0;

        // ── Сходимость ─────────────────────────────────────────────────
        public string IterationsText { get; }
        public string ResidualText   { get; }

        public record RebarRow(int Num, string X, string Y, string Eps, string Sigma);

        public StrainSummaryVM(CalcResult result, CrossSection section, CalcType calcType)
        {
            TaskTag     = result.TaskTag;
            CreatedText = result.Created;

            var doc  = JsonDocument.Parse(result.DataJson);
            var root = doc.RootElement;

            bool converged = root.TryGetProperty("converged", out var cv) && cv.GetBoolean();
            StatusText  = converged ? Loc.S("ResultConvergedYes") : Loc.S("ResultConvergedNo");
            StatusBrush = converged ? Brushes.Green : Brushes.Red;

            double e0 = root.TryGetProperty("e0", out var v) ? v.GetDouble() : 0;
            double ky = root.TryGetProperty("ky", out v)     ? v.GetDouble() : 0;
            double kz = root.TryGetProperty("kz", out v)     ? v.GetDouble() : 0;
            var k = new Kurvature { e0 = e0, ky = ky, kz = kz };

            Eps0Text = $"{e0:+0.000000;-0.000000}";
            KyText   = $"{ky:+0.0000e+00;-0.0000e+00}  1/м";
            KzText   = $"{kz:+0.0000e+00;-0.0000e+00}  1/м";

            // Усилия
            double nt  = root.TryGetProperty("N_target",  out v) ? v.GetDouble() : 0;
            double nr  = root.TryGetProperty("N_result",  out v) ? v.GetDouble() : 0;
            double mxt = root.TryGetProperty("Mx_target", out v) ? v.GetDouble() : 0;
            double mxr = root.TryGetProperty("Mx_result", out v) ? v.GetDouble() : 0;
            double myt = root.TryGetProperty("My_target", out v) ? v.GetDouble() : 0;
            double myr = root.TryGetProperty("My_result", out v) ? v.GetDouble() : 0;

            NText  = $"{nt:+0.000;-0.000} → {nr:+0.000;-0.000}  кН    ({Pct(nt, nr)})";
            MxText = $"{mxt:+0.000;-0.000} → {mxr:+0.000;-0.000}  кН·м  ({Pct(mxt, mxr)})";
            MyText = $"{myt:+0.000;-0.000} → {myr:+0.000;-0.000}  кН·м  ({Pct(myt, myr)})";

            // Экстремальные деформации — по вершинам контуров Hull и стержням
            var (epsMin, epsMax) = ComputeExtremeStrains(section, k);
            HasExtremes = epsMin.HasValue;
            EpsMinText  = epsMin.HasValue ? $"{epsMin.Value:+0.000000;-0.000000}" : "—";
            EpsMaxText  = epsMax.HasValue ? $"{epsMax.Value:+0.000000;-0.000000}" : "—";

            // Жёсткости
            var stiff = ComputeStiffness(section, k, calcType);
            HasStiffness = stiff != null;
            if (stiff != null)
            {
                XcText   = $"{stiff.Xc_mm:+0.0;-0.0}  мм";
                YcText   = $"{stiff.Yc_mm:+0.0;-0.0}  мм";
                EAText   = $"{stiff.EA_kN:N0}  кН";
                EIy0Text = $"{stiff.EIy0_kNm2:N2}  кН·м²";
                EIz0Text = $"{stiff.EIz0_kNm2:N2}  кН·м²";
                EIycText = $"{stiff.EIyc_kNm2:N2}  кН·м²";
                EIzcText = $"{stiff.EIzc_kNm2:N2}  кН·м²";
                EAelText   = $"{stiff.EAel_kN:N0}  кН";
                EIyelText  = $"{stiff.EIyel_kNm2:N2}  кН·м²";
                EIzelText  = $"{stiff.EIzel_kNm2:N2}  кН·м²";
                PhiEAText  = FmtRatio(stiff.PhiEA);
                PhiEIyText = FmtRatio(stiff.PhiEIy);
                PhiEIzText = FmtRatio(stiff.PhiEIz);
            }
            else
            {
                XcText = YcText = EAText = EIy0Text = EIz0Text = EIycText = EIzcText = "—";
                EAelText = EIyelText = EIzelText = PhiEAText = PhiEIyText = PhiEIzText = "—";
            }

            // Арматура — точечные фибры
            int num = 1;
            foreach (var area in section.Areas)
                foreach (var f in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
                    RebarRows.Add(new RebarRow(
                        num++,
                        $"{f.X * 1000:+0.0;-0.0}",
                        $"{f.Y * 1000:+0.0;-0.0}",
                        $"{f.Eps:+0.00000;-0.00000}",
                        $"{f.Sig / 1000.0:+0.0;-0.0}"));

            // Сходимость
            int iters = root.TryGetProperty("iterations", out v) ? v.GetInt32() : 0;
            double res = root.TryGetProperty("residual",  out v) ? v.GetDouble() : 0;
            IterationsText = iters.ToString();
            ResidualText   = $"{res:0.000e+00}  кН";
        }

        // ── Вспомогательные ───────────────────────────────────────────

        static string Pct(double target, double result)
        {
            if (Math.Abs(target) < 1e-9) return "—";
            return $"{(result - target) / Math.Abs(target) * 100:+0.00;-0.00}%";
        }

        static string FmtRatio(double v)
            => double.IsNaN(v) || double.IsInfinity(v) ? "—" : $"{v:0.000}";

        static (double? min, double? max) ComputeExtremeStrains(CrossSection section, Kurvature k)
        {
            var vals = new List<double>();
            foreach (var area in section.Areas)
            {
                if (area.Hull != null)
                {
                    var xs = area.Hull.X; var ys = area.Hull.Y;
                    for (int i = 0; i < xs.Count; i++)
                        vals.Add(k.e0 + k.ky * ys[i] + k.kz * xs[i]);
                }
                foreach (var f in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
                    vals.Add(k.e0 + k.ky * f.Y + k.kz * f.X);
            }
            if (vals.Count == 0) return (null, null);
            return (vals.Min(), vals.Max());
        }

        static StiffnessResult? ComputeStiffness(CrossSection section, Kurvature k, CalcType calcType)
        {
            // Единицы ввода: площадь [м²], координаты [м], E [МПа=Н/мм²]
            // Единицы вывода: EA [кН], EI [кН·м²], ц.т. [мм]
            double EA  = 0, ESy = 0, ESz = 0, EIy = 0, EIz = 0;
            double EAe = 0, ESye= 0, ESze= 0, EIye= 0, EIze= 0;

            foreach (var area in section.Areas)
            {
                if (!area.Diagramms.TryGetValue(calcType, out var dgr)) continue;
                // SigValue возвращает кПа; E0 = (кПа → МПа) / ε = МПа
                double E0 = Math.Abs(dgr.SigValue(1e-7)) / 1e-7 / 1000.0;

                bool hasMesh = area.Fibers.Any(f => f.TypeFiber != FiberType.point);

                if (hasMesh)
                {
                    foreach (var f in area.Fibers.Where(f => f.TypeFiber != FiberType.point))
                    {
                        // f.Sig в кПа → делим на 1000 для МПа, как и E0
                        double Es = Math.Abs(f.Eps) > 1e-9
                            ? Math.Abs(f.Sig / 1000.0 / f.Eps) : E0;
                        double amm2 = f.Area * 1e6;
                        double xmm  = f.X * 1000;
                        double ymm  = f.Y * 1000;
                        Acc(Es, E0, amm2, xmm, ymm,
                            ref EA, ref ESy, ref ESz, ref EIy, ref EIz,
                            ref EAe, ref ESye, ref ESze, ref EIye, ref EIze);
                    }
                }
                else if (area.Hull != null && area.Category == AreaCategory.Region)
                {
                    var gp = new GeoProps(area.Hull);
                    if (gp.A < 1e-12) continue;
                    double cx_m = gp.Sy / gp.A;
                    double cy_m = gp.Sx / gp.A;
                    double eps_c = k.e0 + k.ky * cy_m + k.kz * cx_m;
                    double sig_c = dgr.SigValue(eps_c) / 1000.0; // кПа → МПа
                    double Es = Math.Abs(eps_c) > 1e-9
                        ? Math.Abs(sig_c / eps_c) : E0;
                    double amm2 = gp.A * 1e6;
                    double xmm  = cx_m * 1000;
                    double ymm  = cy_m * 1000;
                    Acc(Es, E0, amm2, xmm, ymm,
                        ref EA, ref ESy, ref ESz, ref EIy, ref EIz,
                        ref EAe, ref ESye, ref ESze, ref EIye, ref EIze);
                }

                foreach (var f in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
                {
                    double Es = Math.Abs(f.Eps) > 1e-9
                        ? Math.Abs(f.Sig / 1000.0 / f.Eps) : E0;
                    double amm2 = f.Area * 1e6;
                    double xmm  = f.X * 1000;
                    double ymm  = f.Y * 1000;
                    Acc(Es, E0, amm2, xmm, ymm,
                        ref EA, ref ESy, ref ESz, ref EIy, ref EIz,
                        ref EAe, ref ESye, ref ESze, ref EIye, ref EIze);
                }
            }

            if (EA < 1e-6) return null;

            double xc = ESy / EA;
            double yc = ESz / EA;
            double EIyc = EIy - ESy * ESy / EA;
            double EIzc = EIz - ESz * ESz / EA;
            double EIyec = EAe > 1e-6 ? EIye - ESye * ESye / EAe : 0;
            double EIzec = EAe > 1e-6 ? EIze - ESze * ESze / EAe : 0;

            static double Ratio(double a, double b) =>
                b > 1e-6 ? a / b : double.NaN;

            return new StiffnessResult(
                Xc_mm:      xc,
                Yc_mm:      yc,
                EA_kN:      EA   / 1e3,
                EIy0_kNm2:  EIy  / 1e9,
                EIz0_kNm2:  EIz  / 1e9,
                EIyc_kNm2:  EIyc / 1e9,
                EIzc_kNm2:  EIzc / 1e9,
                EAel_kN:    EAe  / 1e3,
                EIyel_kNm2: EIye / 1e9,
                EIzel_kNm2: EIze / 1e9,
                PhiEA:      Ratio(EA,   EAe),
                PhiEIy:     Ratio(EIyc, EIyec),
                PhiEIz:     Ratio(EIzc, EIzec));
        }

        static void Acc(double Es, double E0, double amm2, double xmm, double ymm,
            ref double EA,  ref double ESy,  ref double ESz,
            ref double EIy, ref double EIz,
            ref double EAe, ref double ESye, ref double ESze,
            ref double EIye, ref double EIze)
        {
            EA  += Es * amm2;
            ESy += Es * amm2 * xmm;
            ESz += Es * amm2 * ymm;
            EIy += Es * amm2 * xmm * xmm;
            EIz += Es * amm2 * ymm * ymm;
            EAe  += E0 * amm2;
            ESye += E0 * amm2 * xmm;
            ESze += E0 * amm2 * ymm;
            EIye += E0 * amm2 * xmm * xmm;
            EIze += E0 * amm2 * ymm * ymm;
        }

        record StiffnessResult(
            double Xc_mm, double Yc_mm,
            double EA_kN, double EIy0_kNm2, double EIz0_kNm2,
            double EIyc_kNm2, double EIzc_kNm2,
            double EAel_kN, double EIyel_kNm2, double EIzel_kNm2,
            double PhiEA, double PhiEIy, double PhiEIz);
    }
}
