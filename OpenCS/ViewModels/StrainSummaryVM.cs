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

        // ── Влияние прогиба (η, п. 8.1.15 СП63.13330) ──────────────────
        public bool   EtaEnabled { get; }
        public string EtaModeText { get; }
        public string MxOriginalText { get; }
        public string MyOriginalText { get; }
        public string L0xText { get; }
        public string HxText  { get; }
        public string SlendernessXText { get; }
        public string DxText  { get; }
        public string NcrXText { get; }
        public string EtaXText { get; }
        public string L0yText { get; }
        public string HyText  { get; }
        public string SlendernessYText { get; }
        public string DyText  { get; }
        public string NcrYText { get; }
        public string EtaYText { get; }
        public bool   EtaUnstable { get; }
        public bool   EtaExtrapolationFailed { get; }

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
        public bool   ShowConvergence { get; }

        /// <summary>Примечание об учёте замещения площади бетона арматурой.</summary>
        public bool   ShowRebarAreaNote { get; }
        public string RebarAreaNote    { get; }

        public record RebarRow(int Num, string X, string Y, string Eps, string Sigma);

        public StrainSummaryVM(CalcResult result, CrossSection section, CalcType calcType, CalcSettings? settings = null, bool ten = true)
        {
            int gridDensity = settings?.GridDensity ?? 20;
            ShowRebarAreaNote = ShouldShowRebarAreaNote(section, settings);
            RebarAreaNote     = ShowRebarAreaNote ? Loc.S("ResultRebarAreaReductionNote") : "";
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

            // η (п. 8.1.15 СП63.13330) — присутствует, только если задача считала поправку
            EtaEnabled = root.TryGetProperty("eta", out var etaEl) && etaEl.ValueKind != JsonValueKind.Null;
            if (EtaEnabled)
            {
                string mode = etaEl.TryGetProperty("mode", out var modeEl) ? modeEl.GetString() ?? "formula" : "formula";
                EtaModeText = mode == "iterative" ? Loc.S("ResultEtaModeIterative") : Loc.S("ResultEtaModeFormula");

                double mxOrig = etaEl.TryGetProperty("mxOriginal", out var mxoEl) ? mxoEl.GetDouble() : 0;
                double myOrig = etaEl.TryGetProperty("myOriginal", out var myoEl) ? myoEl.GetDouble() : 0;
                MxOriginalText = $"{mxOrig:+0.000;-0.000}  кН·м";
                MyOriginalText = $"{myOrig:+0.000;-0.000}  кН·м";

                bool slenderX = etaEl.TryGetProperty("slenderX", out var sxEl) && sxEl.GetBoolean();
                bool slenderY = etaEl.TryGetProperty("slenderY", out var syEl) && syEl.GetBoolean();
                bool stableX  = !etaEl.TryGetProperty("stableX", out var stxEl) || stxEl.GetBoolean();
                bool stableY  = !etaEl.TryGetProperty("stableY", out var styEl) || styEl.GetBoolean();
                double etaXv  = etaEl.TryGetProperty("etaX", out var exEl) ? exEl.GetDouble() : 1.0;
                double etaYv  = etaEl.TryGetProperty("etaY", out var eyEl) ? eyEl.GetDouble() : 1.0;
                double? ncrX  = etaEl.TryGetProperty("ncrX", out var nxEl) && nxEl.ValueKind != JsonValueKind.Null ? nxEl.GetDouble() : null;
                double? ncrY  = etaEl.TryGetProperty("ncrY", out var nyEl) && nyEl.ValueKind != JsonValueKind.Null ? nyEl.GetDouble() : null;
                bool extrapFailedX = etaEl.TryGetProperty("extrapolationFailedX", out var efxEl) && efxEl.GetBoolean();
                bool extrapFailedY = etaEl.TryGetProperty("extrapolationFailedY", out var efyEl) && efyEl.GetBoolean();

                double l0x = etaEl.TryGetProperty("l0x", out var l0xEl) ? l0xEl.GetDouble() : 0;
                double hx  = etaEl.TryGetProperty("hx",  out var hxEl)  ? hxEl.GetDouble()  : 0;
                double? slendernessX = etaEl.TryGetProperty("slendernessX", out var slxEl) && slxEl.ValueKind != JsonValueKind.Null ? slxEl.GetDouble() : null;
                double? dX = etaEl.TryGetProperty("dX", out var dxEl) && dxEl.ValueKind != JsonValueKind.Null ? dxEl.GetDouble() : null;

                double l0y = etaEl.TryGetProperty("l0y", out var l0yEl) ? l0yEl.GetDouble() : 0;
                double hy  = etaEl.TryGetProperty("hy",  out var hyEl)  ? hyEl.GetDouble()  : 0;
                double? slendernessY = etaEl.TryGetProperty("slendernessY", out var slyEl) && slyEl.ValueKind != JsonValueKind.Null ? slyEl.GetDouble() : null;
                double? dY = etaEl.TryGetProperty("dY", out var dyEl) && dyEl.ValueKind != JsonValueKind.Null ? dyEl.GetDouble() : null;

                L0xText = $"{l0x:0.00}  м";
                HxText  = $"{hx:0.00}  м";
                SlendernessXText = FormatSlenderness(slendernessX, slenderX);
                DxText  = dX.HasValue ? $"{dX.Value:F1}  кН·м²" : "—";
                NcrXText = ncrX.HasValue ? $"{ncrX.Value:F0}  кН" : "—";
                EtaXText = FormatEta(etaXv, slenderX, stableX);

                L0yText = $"{l0y:0.00}  м";
                HyText  = $"{hy:0.00}  м";
                SlendernessYText = FormatSlenderness(slendernessY, slenderY);
                DyText  = dY.HasValue ? $"{dY.Value:F1}  кН·м²" : "—";
                NcrYText = ncrY.HasValue ? $"{ncrY.Value:F0}  кН" : "—";
                EtaYText = FormatEta(etaYv, slenderY, stableY);

                EtaUnstable = (slenderX && !stableX) || (slenderY && !stableY);
                EtaExtrapolationFailed = mode == "iterative" && ((slenderX && extrapFailedX) || (slenderY && extrapFailedY));
            }
            else
            {
                EtaModeText = MxOriginalText = MyOriginalText = "—";
                L0xText = HxText = SlendernessXText = DxText = NcrXText = EtaXText = "—";
                L0yText = HyText = SlendernessYText = DyText = NcrYText = EtaYText = "—";
                EtaUnstable = EtaExtrapolationFailed = false;
            }

            // Экстремальные деформации — по вершинам контуров Hull и стержням
            var (epsMin, epsMax) = ComputeExtremeStrains(section, k);
            HasExtremes = epsMin.HasValue;
            EpsMinText  = epsMin.HasValue ? $"{epsMin.Value:+0.000000;-0.000000}" : "—";
            EpsMaxText  = epsMax.HasValue ? $"{epsMax.Value:+0.000000;-0.000000}" : "—";

            // Жёсткости
            var stiff = ComputeStiffness(section, k, calcType, gridDensity, ten);
            HasStiffness = stiff != null;
            if (stiff != null)
            {
                XcText   = $"{stiff.Xc_mm:+0.0;-0.0}  мм";
                YcText   = $"{stiff.Yc_mm:+0.0;-0.0}  мм";
                EAText   = $"{stiff.EA_kN:F0}  кН";
                EIy0Text = $"{stiff.EIy0_kNm2:F2}  кН·м²";
                EIz0Text = $"{stiff.EIz0_kNm2:F2}  кН·м²";
                EIycText = $"{stiff.EIyc_kNm2:F2}  кН·м²";
                EIzcText = $"{stiff.EIzc_kNm2:F2}  кН·м²";
                EAelText   = $"{stiff.EAel_kN:F0}  кН";
                EIyelText  = $"{stiff.EIyel_kNm2:F2}  кН·м²";
                EIzelText  = $"{stiff.EIzel_kNm2:F2}  кН·м²";
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
            foreach (var (area, _) in section.EnumerateAreas(k))
                foreach (var f in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
                    RebarRows.Add(new RebarRow(
                        num++,
                        $"{f.X * 1000:+0.0;-0.0}",
                        $"{f.Y * 1000:+0.0;-0.0}",
                        $"{f.Eps:+0.00000;-0.00000}",
                        $"{f.Sig / 1000.0:+0.0;-0.0}"));

            // Сходимость (только для strain_state — у limit_force свои поля в сводке)
            int iters = root.TryGetProperty("iterations", out v) ? v.GetInt32() : 0;
            ShowConvergence = root.TryGetProperty("residual", out v);
            double res = ShowConvergence ? v.GetDouble() : 0;
            IterationsText = iters.ToString();
            ResidualText   = $"{res:0.000e+00}  кН";
        }

        // ── Вспомогательные ───────────────────────────────────────────

        /// <summary>Показывать примечание, если включена глобальная опция и в сечении есть арматура в бетоне.</summary>
        public static bool ShouldShowRebarAreaNote(CrossSection section, CalcSettings? settings)
            => settings?.RebarDifferentialDiagram == true
               && section.Areas.Any(a => a.HostAreaId != null);

        static string Pct(double target, double result)
        {
            if (Math.Abs(target) < 1e-9) return "—";
            return $"{(result - target) / Math.Abs(target) * 100:+0.00;-0.00}%";
        }

        static string FmtRatio(double v)
            => double.IsNaN(v) || double.IsInfinity(v) ? "—" : $"{v:0.000}";

        /// <summary>Значение η для одной оси (п. 8.1.15 СП63.13330).</summary>
        static string FormatEta(double eta, bool slender, bool stable)
        {
            if (!slender) return "1.000";
            if (!stable)  return Loc.S("ResultEtaInstable");
            return $"{eta:0.000}";
        }

        /// <summary>Гибкость l0/h с пометкой, применяется ли поправка (порог 14, п. 8.1.2).</summary>
        static string FormatSlenderness(double? ratio, bool slender)
        {
            if (!ratio.HasValue) return "—";
            string suffix = slender ? " > 14" : $" ≤ 14 ({Loc.S("ResultEtaNotRequired")})";
            return $"{ratio.Value:0.0}{suffix}";
        }

        static (double? min, double? max) ComputeExtremeStrains(CrossSection section, Kurvature k)
        {
            var vals = new List<double>();
            foreach (var (area, ka) in section.EnumerateAreas(k))
            {
                if (area.Hull != null)
                {
                    var xs = area.Hull.X; var ys = area.Hull.Y;
                    for (int i = 0; i < xs.Count; i++)
                        vals.Add(ka.e0 + ka.ky * ys[i] + ka.kz * xs[i]);
                }
                foreach (var f in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
                    vals.Add(ka.e0 + ka.ky * f.Y + ka.kz * f.X);
            }
            if (vals.Count == 0) return (null, null);
            return (vals.Min(), vals.Max());
        }

        static StiffnessResult? ComputeStiffness(CrossSection section, Kurvature k, CalcType calcType, int gridDensity = 20, bool ten = true)
        {
            // Единицы ввода: площадь [м²], координаты [м], E [МПа=Н/мм²]
            // Единицы вывода: EA [кН], EI [кН·м²], ц.т. [мм]
            double EA  = 0, ESy = 0, ESz = 0, EIy = 0, EIz = 0;
            double EAe = 0, ESye= 0, ESze= 0, EIye= 0, EIze= 0;

            foreach (var (area, ka) in section.EnumerateAreas(k))
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
                    var hullMm = area.Hull.X
                        .Zip(area.Hull.Y, (x, y) => (X: x * 1000, Y: y * 1000))
                        .SkipLast(1).ToList();
                    var holesMm = area.Holes.Select(h =>
                        h.X.Zip(h.Y, (x, y) => (X: x * 1000, Y: y * 1000))
                           .SkipLast(1).ToList()).ToList();
                    double hmXMin = hullMm.Min(p => p.X), hmXMax = hullMm.Max(p => p.X);
                    double hmYMin = hullMm.Min(p => p.Y), hmYMax = hullMm.Max(p => p.Y);
                    double nmStep = Math.Max(hmXMax - hmXMin, hmYMax - hmYMin) / Math.Max(gridDensity, 1);
                    if (nmStep < 1.0) nmStep = 1.0;
                    var nmXs = BuildSteps(hmXMin, hmXMax, nmStep);
                    var nmYs = BuildSteps(hmYMin, hmYMax, nmStep);
                    for (int xi = 0; xi < nmXs.Count - 1; xi++)
                    for (int yi = 0; yi < nmYs.Count - 1; yi++)
                    {
                        var cell = GridSplit.ClipByRect(hullMm,
                            nmXs[xi], nmXs[xi + 1], nmYs[yi], nmYs[yi + 1]);
                        if (cell.Count < 3) continue;
                        double cx_mm = cell.Average(p => p.X);
                        double cy_mm = cell.Average(p => p.Y);
                        if (holesMm.Any(h => PointInPolyMm(cx_mm, cy_mm, h))) continue;
                        double eps_c = ka.e0 + ka.ky * (cy_mm / 1000) + ka.kz * (cx_mm / 1000);
                        double sig_c = dgr.SigValue(eps_c, ten) / 1000.0;
                        double Es = Math.Abs(eps_c) > 1e-9 ? Math.Abs(sig_c / eps_c) : E0;
                        double cellAmm2 = PolygonAreaMm2(cell);
                        Acc(Es, E0, cellAmm2, cx_mm, cy_mm,
                            ref EA, ref ESy, ref ESz, ref EIy, ref EIz,
                            ref EAe, ref ESye, ref ESze, ref EIye, ref EIze);
                    }
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
                EAel_kN:    EAe   / 1e3,
                EIyel_kNm2: EIyec / 1e9,
                EIzel_kNm2: EIzec / 1e9,
                PhiEA:      Ratio(EA,   EAe),
                PhiEIy:     Ratio(EIyc, EIyec),
                PhiEIz:     Ratio(EIzc, EIzec));
        }

        static List<double> BuildSteps(double lo, double hi, double step)
        {
            var result = new List<double> { lo };
            int iLo = (int)Math.Ceiling(lo / step);
            int iHi = (int)Math.Floor(hi / step);
            for (int i = iLo; i <= iHi; i++)
            {
                double v = i * step;
                if (v > lo + step * 0.01 && v < hi - step * 0.01)
                    result.Add(v);
            }
            result.Add(hi);
            return result;
        }

        static double PolygonAreaMm2(List<(double X, double Y)> verts)
        {
            double area = 0;
            int n = verts.Count;
            for (int i = 0; i < n; i++)
            {
                var a = verts[i]; var b = verts[(i + 1) % n];
                area += (a.X * b.Y - b.X * a.Y);
            }
            return Math.Abs(area) * 0.5;
        }

        static bool PointInPolyMm(double px, double py, List<(double X, double Y)> verts)
        {
            int n = verts.Count; bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = verts[i].X, yi = verts[i].Y;
                double xj = verts[j].X, yj = verts[j].Y;
                if (((yi > py) != (yj > py)) &&
                    (px < (xj - xi) * (py - yi) / (yj - yi) + xi))
                    inside = !inside;
            }
            return inside;
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
