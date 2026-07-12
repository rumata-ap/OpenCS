using CScore;
using OpenCS.Services;
using OpenCS.Utilites;
using System;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;

namespace OpenCS.ViewModels
{
    /// <summary>
    /// ViewModel результата двухстадийного расчёта: общая сводка +
    /// подчинённые StrainSummaryVM / SectionPlotVM для этапа 1 и составного сечения.
    /// Этап 1 — сечение до усиления под усилием этапа 1 (плоскость κ1).
    /// Этап 2 — составное сечение (= этап 1 + этап 2) под полным усилием этапа 2:
    /// волокна этапа 1 несут суммарную деформацию ε(κ1) + ε(κ2), волокна этапа 2 — ε(κ2).
    /// Это обеспечивается виртуальным <see cref="CrossSection.EnumerateAreas"/> на TwoStageSection.
    /// </summary>
    public class TwoStageSummaryVM : ViewModelBase
    {
        public string TaskTag     { get; }
        public string CreatedText { get; }
        public string StatusText  { get; }
        public Brush  StatusBrush { get; }

        // ── Замороженная плоскость этапа 1 (κ1) ─────────────────────────
        public string Stage1Eps0Text { get; }
        public string Stage1KyText   { get; }
        public string Stage1KzText   { get; }

        // ── Плоскость этапа 2 (κ2) ──────────────────────────────────────
        public string Stage2Eps0Text { get; }
        public string Stage2KyText   { get; }
        public string Stage2KzText   { get; }

        // ── Усилия этапа 1 ──────────────────────────────────────────────
        public string Stage1NText  { get; }
        public string Stage1MxText { get; }
        public string Stage1MyText { get; }

        // ── Усилия этапа 2 ──────────────────────────────────────────────
        public string Stage2NText  { get; }
        public string Stage2MxText { get; }
        public string Stage2MyText { get; }

        // ── Сходимость ──────────────────────────────────────────────────
        public string Stage1StatusText    { get; }
        public Brush  Stage1StatusBrush   { get; }
        public string Stage1IterationsText { get; }
        public string Stage1ResidualText   { get; }

        public string Stage2StatusText    { get; }
        public Brush  Stage2StatusBrush   { get; }
        public string Stage2IterationsText { get; }
        public string Stage2ResidualText   { get; }

        // ── Подчинённые VM для вкладок этапов ───────────────────────────
        public StrainSummaryVM? Stage1Summary { get; }
        public SectionPlotVM?   Stage1Stress  { get; }
        public SectionPlotVM?   Stage1Strain  { get; }

        public StrainSummaryVM? Stage2Summary { get; }
        public SectionPlotVM?   Stage2Stress  { get; }
        public SectionPlotVM?   Stage2Strain  { get; }

        public SectionCutVM? Stage1CutVM { get; }
        public SectionCutVM? Stage2CutVM { get; }

        public TwoStageSummaryVM(CalcResult result, TwoStageSection tss,
                                  CalcType calcType, CalcSettings settings,
                                  IFileDialogService fileDialogService)
        {
            TaskTag     = result.TaskTag;
            CreatedText = result.Created;
            bool ten = settings.ResolveConcreteTension(calcType);

            using var doc  = JsonDocument.Parse(result.DataJson);
            var root = doc.RootElement;

            bool s1Ok = root.TryGetProperty("stage1_converged", out var s1c) && s1c.GetBoolean();
            bool s2Ok = root.TryGetProperty("converged",        out var s2c) && s2c.GetBoolean();

            Stage1StatusText  = s1Ok ? Loc.S("ResultConvergedYes") : Loc.S("ResultConvergedNo");
            Stage1StatusBrush = s1Ok ? Brushes.Green : Brushes.Red;
            Stage2StatusText  = s2Ok ? Loc.S("ResultConvergedYes") : Loc.S("ResultConvergedNo");
            Stage2StatusBrush = s2Ok ? Brushes.Green : Brushes.Red;

            bool totalOk = s1Ok && s2Ok;
            StatusText  = totalOk ? Loc.S("ResultConvergedYes") : Loc.S("ResultConvergedNo");
            StatusBrush = totalOk ? Brushes.Green : Brushes.Red;

            // κ1
            double s1e0 = root.TryGetProperty("stage1_e0", out var v) ? v.GetDouble() : 0;
            double s1ky = root.TryGetProperty("stage1_ky", out v)     ? v.GetDouble() : 0;
            double s1kz = root.TryGetProperty("stage1_kz", out v)     ? v.GetDouble() : 0;
            var k1 = new Kurvature { e0 = s1e0, ky = s1ky, kz = s1kz };
            Stage1Eps0Text = $"{s1e0:+0.000000;-0.000000}";
            Stage1KyText   = $"{s1ky:+0.0000e+00;-0.0000e+00}  1/м";
            Stage1KzText   = $"{s1kz:+0.0000e+00;-0.0000e+00}  1/м";

            // κ2
            double s2e0 = root.TryGetProperty("e0", out v) ? v.GetDouble() : 0;
            double s2ky = root.TryGetProperty("ky", out v) ? v.GetDouble() : 0;
            double s2kz = root.TryGetProperty("kz", out v) ? v.GetDouble() : 0;
            var k2 = new Kurvature { e0 = s2e0, ky = s2ky, kz = s2kz };
            Stage2Eps0Text = $"{s2e0:+0.000000;-0.000000}";
            Stage2KyText   = $"{s2ky:+0.0000e+00;-0.0000e+00}  1/м";
            Stage2KzText   = $"{s2kz:+0.0000e+00;-0.0000e+00}  1/м";

            // Независимая копия сечения: SetEps будет мутировать фибры, нельзя трогать
            // shared-экземпляр из AppViewModel.CrossSections.
            var clone = (TwoStageSection)tss.CloneForCalc();

            // Этап 1: отдельное сечение из областей первого этапа, плоскость κ1.
            // Области клонируются ещё раз, чтобы фибры этапа 1 имели состояние
            // только от κ1 (а не от κ1+κ2 после SetEps на составном сечении).
            var stage1Section = new CrossSection
            {
                Tag   = clone.Stage1.Tag,
                Areas = clone.Stage1.Areas.Select(a => a.CloneForCalc()).ToList()
            };
            stage1Section.SetEps(k1, calcType, ten);

            // Усилия этапа 1: вычисляем через интеграл сечения (источник истины),
            // а не из JSON — это работает и для старых результатов без полей stage1_N_*.
            // Для target берём значение из JSON если есть (внешнее заданное усилие);
            // иначе считаем target ≈ result (при сходимости они совпадают).
            var resS1 = stage1Section.Integral(k1, calcType, ten);
            bool hasS1TargetN  = root.TryGetProperty("stage1_N_target",  out v);
            bool hasS1TargetMx = root.TryGetProperty("stage1_Mx_target", out v);
            bool hasS1TargetMy = root.TryGetProperty("stage1_My_target", out v);
            double s1nt  = hasS1TargetN  ? root.GetProperty("stage1_N_target") .GetDouble() : resS1.N;
            double s1mxt = hasS1TargetMx ? root.GetProperty("stage1_Mx_target").GetDouble() : resS1.Mx;
            double s1myt = hasS1TargetMy ? root.GetProperty("stage1_My_target").GetDouble() : resS1.My;
            double s1nr  = resS1.N, s1mxr = resS1.Mx, s1myr = resS1.My;
            Stage1NText  = $"{s1nt:+0.000;-0.000} → {s1nr:+0.000;-0.000}  кН    ({Pct(s1nt, s1nr)})";
            Stage1MxText = $"{s1mxt:+0.000;-0.000} → {s1mxr:+0.000;-0.000}  кН·м  ({Pct(s1mxt, s1mxr)})";
            Stage1MyText = $"{s1myt:+0.000;-0.000} → {s1myr:+0.000;-0.000}  кН·м  ({Pct(s1myt, s1myr)})";

            // Усилия этапа 2 (полное усилие на составном сечении)
            double s2nt  = root.TryGetProperty("N_target",  out v) ? v.GetDouble() : 0;
            double s2nr  = root.TryGetProperty("N_result",  out v) ? v.GetDouble() : 0;
            double s2mxt = root.TryGetProperty("Mx_target", out v) ? v.GetDouble() : 0;
            double s2mxr = root.TryGetProperty("Mx_result", out v) ? v.GetDouble() : 0;
            double s2myt = root.TryGetProperty("My_target", out v) ? v.GetDouble() : 0;
            double s2myr = root.TryGetProperty("My_result", out v) ? v.GetDouble() : 0;
            Stage2NText  = $"{s2nt:+0.000;-0.000} → {s2nr:+0.000;-0.000}  кН    ({Pct(s2nt, s2nr)})";
            Stage2MxText = $"{s2mxt:+0.000;-0.000} → {s2mxr:+0.000;-0.000}  кН·м  ({Pct(s2mxt, s2mxr)})";
            Stage2MyText = $"{s2myt:+0.000;-0.000} → {s2myr:+0.000;-0.000}  кН·м  ({Pct(s2myt, s2myr)})";

            int s1it = root.TryGetProperty("stage1_iterations", out v) ? v.GetInt32() : 0;
            double s1res = root.TryGetProperty("stage1_residual", out v) ? v.GetDouble() : 0;
            Stage1IterationsText = s1it.ToString();
            Stage1ResidualText   = $"{s1res:0.000e+00}  кН";

            int s2it = root.TryGetProperty("iterations", out v) ? v.GetInt32() : 0;
            double s2res = root.TryGetProperty("residual", out v) ? v.GetDouble() : 0;
            Stage2IterationsText = s2it.ToString();
            Stage2ResidualText   = $"{s2res:0.000e+00}  кН";

            // ── Подчинённые VM ──────────────────────────────────────────
            // Синтетический CalcResult для этапа 1: используем вычисленные усилия,
            // а не полагаемся на наличие stage1_* полей в JSON.
            var stage1Data = new
            {
                converged  = s1Ok,
                iterations = s1it,
                residual   = s1res,
                e0 = s1e0, ky = s1ky, kz = s1kz,
                N_target  = s1nt,  Mx_target = s1mxt, My_target = s1myt,
                N_result  = s1nr,  Mx_result = s1mxr, My_result = s1myr
            };
            var stage1Result = new CalcResult
            {
                TaskId   = result.TaskId,
                TaskKind = result.TaskKind,
                TaskTag  = result.TaskTag,
                Created  = result.Created,
                Status   = s1Ok ? "ok" : "not_converged",
                DataJson = JsonSerializer.Serialize(stage1Data)
            };
            Stage1Summary = new StrainSummaryVM(stage1Result, stage1Section, calcType, settings, ten);
            Stage1Stress  = new SectionPlotVM(stage1Section, k1, calcType, SectionPlotMode.Stress, settings, ten);
            Stage1Strain  = new SectionPlotVM(stage1Section, k1, calcType, SectionPlotMode.Strain, settings, ten);

            // Этап 2: составное сечение (этап 1 + этап 2) под κ2.
            // Stage1Kurvature восстанавливается, чтобы EnumerateAreas(κ2) на clone
            // вернул для Stage1.Areas эффективную плоскость κ1+κ2 (суммарная деформация
            // в волокнах первого этапа), а для Areas — κ2 (волокна второго этапа).
            clone.Stage1Kurvature = k1;
            clone.SetEps(k2, calcType, ten);
            Stage2Summary = new StrainSummaryVM(result, clone, calcType, settings, ten);
            Stage2Stress  = new SectionPlotVM(clone, k2, calcType, SectionPlotMode.Stress, settings, ten);
            Stage2Strain  = new SectionPlotVM(clone, k2, calcType, SectionPlotMode.Strain, settings, ten);

            string titleSuffix = $"{result.TaskTag} — {tss.Tag}";
            var cut1 = new SectionCutVM(stage1Section, k1, calcType, fileDialogService)
                { WindowTitleSuffix = $"{titleSuffix} ({Loc.S("TwoStageSummary_Stage1")})" };
            var cut2 = new SectionCutVM(clone, k2, calcType, fileDialogService)
                { WindowTitleSuffix = $"{titleSuffix} ({Loc.S("TwoStageSummary_Stage2")})" };
            Stage1CutVM = cut1;
            Stage2CutVM = cut2;
            Stage1Stress!.CutVM = cut1;
            Stage1Strain!.CutVM = cut1;
            Stage2Stress!.CutVM = cut2;
            Stage2Strain!.CutVM = cut2;
        }

        static string Pct(double target, double result)
        {
            if (Math.Abs(target) < 1e-9) return "—";
            return $"{(result - target) / Math.Abs(target) * 100:+0.00;-0.00}%";
        }
    }
}
