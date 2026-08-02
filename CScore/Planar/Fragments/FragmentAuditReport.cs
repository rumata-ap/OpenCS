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
    /// Проверяет 4 группы критериев: геометрия/сетка, баланс сил на срезах, сходимость и
    /// энергетика, нормативные пределы деформаций.
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

            // Группа 1: геометрия и сетка.
            foreach (var diagnostic in result.MeshDiagnostics)
                report.Issues.Add($"Геометрия/сетка: {diagnostic}");

            // Группа 2: баланс сил на срезах (допуск 1%) + диагностики резолва воздействий.
            foreach (var diagnostic in result.BoundaryDiagnostics)
                report.Issues.Add($"Граничные воздействия: {diagnostic}");
            if (result.ForceUnbalanceRatio > 0.01)
                report.Issues.Add($"Невязка сил на срезах {result.ForceUnbalanceRatio * 100:F2}% превышает допуск 1.0%.");
            else if (result.ForceUnbalanceRatio > 0.005)
                report.Warnings.Add($"Невязка сил на срезах {result.ForceUnbalanceRatio * 100:F2}% требует внимания.");

            // Группа 3: сходимость и энергетика.
            if (!result.IsConverged)
                report.Issues.Add("Нелинейный расчёт в OpenSees не сошёлся.");
            if (result.EnergyConfidence == "Unavailable")
                report.Warnings.Add("Недостаточно данных для оценки энергетического баланса (EnergyConfidence=Unavailable).");

            // Группа 4: нормативные пределы СП 63.13330.
            if (result.MaxConcreteCompressionStrain < -0.0035)
                report.Issues.Add($"Предельная деформация сжатия бетона {result.MaxConcreteCompressionStrain:F5} превышает допуск СП 63.13330 (-0.0035).");
            if (result.MaxRebarTensileStrain > 0.025)
                report.Issues.Add($"Предельная деформация растяжения арматуры {result.MaxRebarTensileStrain:F5} превышает допуск СП 63.13330 (0.025).");

            report.Verdict = report.Issues.Count > 0
                ? FragmentAuditVerdict.Invalid
                : (report.Warnings.Count > 0 ? FragmentAuditVerdict.Warning : FragmentAuditVerdict.Valid);
            return report;
        }
    }
}
