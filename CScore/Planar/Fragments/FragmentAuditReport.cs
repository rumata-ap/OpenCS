using System;
using System.Collections.Generic;

namespace CScore.Planar.Fragments
{
    public enum FragmentAuditVerdict
    {
        Valid,
        Warning,
        Invalid
    }

    /// <summary>
    /// Автоматический отчёт проверки качества фрагмента и нормативного аудита по СП 63.13330.
    /// </summary>
    public class FragmentAuditReport
    {
        public FragmentAuditVerdict Verdict { get; set; } = FragmentAuditVerdict.Valid;
        public List<string> Issues { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();

        public FragmentAuditReport Audit(VerticalPlanarFragment fragment, VerticalPlanarFragmentResult result)
        {
            var report = new FragmentAuditReport();

            if (result == null)
            {
                report.Verdict = FragmentAuditVerdict.Invalid;
                report.Issues.Add("Результат расчёта равен null.");
                return report;
            }

            if (!result.IsConverged)
            {
                report.Verdict = FragmentAuditVerdict.Invalid;
                report.Issues.Add("Нелинейный расчёт в OpenSees не сошёлся.");
            }

            // 1. Проверка равновесия сил на срезах (допуск 1%)
            if (result.ForceUnbalanceRatio > 0.01)
            {
                report.Verdict = FragmentAuditVerdict.Invalid;
                report.Issues.Add($"Невязка сил на срезах {result.ForceUnbalanceRatio * 100:F2}% превышает допуск 1.0%.");
            }
            else if (result.ForceUnbalanceRatio > 0.005)
            {
                report.Warnings.Add($"Невязка сил на срезах {result.ForceUnbalanceRatio * 100:F2}% требует внимания.");
            }

            // 2. Проверка предела сжатия бетона по СП 63.13330 (eps_b >= -0.0035)
            if (result.MaxConcreteCompressionStrain < -0.0035)
            {
                report.Verdict = FragmentAuditVerdict.Invalid;
                report.Issues.Add($"Предельная деформация сжатия бетона {result.MaxConcreteCompressionStrain:F5} превышает допуск СП 63.13330 (-0.0035).");
            }

            // 3. Проверка предела растяжения арматуры по СП 63.13330 (eps_s <= 0.025)
            if (result.MaxRebarTensileStrain > 0.025)
            {
                report.Verdict = FragmentAuditVerdict.Invalid;
                report.Issues.Add($"Предельная деформация растяжения арматуры {result.MaxRebarTensileStrain:F5} превышает допуск СП 63.13330 (0.025).");
            }

            if (report.Verdict == FragmentAuditVerdict.Valid && report.Warnings.Count > 0)
            {
                report.Verdict = FragmentAuditVerdict.Warning;
            }

            return report;
        }
    }
}
