using System.Collections.Generic;

namespace CScore.Planar.Fragments
{
    /// <summary>
    /// Аудит результата floor junction: verdict не может быть Valid при наличии любых blocking
    /// diagnostics (сетка, сборка, boundary pipeline, расчёт), невязки mapping внешней границы
    /// или если расчёт не достиг полной нагрузки.
    /// </summary>
    public class FloorJunctionAuditReport
    {
        /// <summary>Вердикт по итогам аудита (Valid/Warning/Invalid). По умолчанию Invalid:
        /// Valid выставляется только методом <c>Audit</c> для чистого и сошедшегося результата.</summary>
        public FragmentAuditVerdict Verdict { get; set; } = FragmentAuditVerdict.Invalid;
        /// <summary>Блокирующие замечания (любое из них делает verdict Invalid).</summary>
        public List<string> Issues { get; set; } = new List<string>();
        /// <summary>Предупреждения, не влияющие на допустимость результата.</summary>
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>Проводит аудит результата floor junction по blocking diagnostics и сходимости.</summary>
        public FloorJunctionAuditReport Audit(FloorJunctionFragment fragment, FloorJunctionResult result)
        {
            var report = new FloorJunctionAuditReport();
            if (result == null)
            {
                report.Verdict = FragmentAuditVerdict.Invalid;
                report.Issues.Add("Результат расчёта равен null.");
                return report;
            }

            foreach (var diagnostic in result.MeshDiagnostics)
                report.Issues.Add($"Сетка: {diagnostic}");
            foreach (var diagnostic in result.AssemblyDiagnostics)
                report.Issues.Add($"Сборка: {diagnostic}");
            foreach (var diagnostic in result.BoundaryDiagnostics)
                report.Issues.Add($"Граничные воздействия: {diagnostic}");
            foreach (var diagnostic in result.AnalysisDiagnostics)
                report.Issues.Add($"Расчёт: {diagnostic}");
            if (!result.IsConverged)
                report.Issues.Add("Нелинейный расчёт не достиг полной нагрузки.");
            if (!double.IsFinite(result.BoundaryForceUnbalanceRatio))
                report.Issues.Add("Невязка mapping внешней границы не является конечной.");
            else if (result.BoundaryForceUnbalanceRatio > 1e-3)
                report.Issues.Add(
                    $"Невязка mapping внешней границы {FormatUnbalance(result.BoundaryForceUnbalanceRatio)} " +
                    $"превышает допуск 0.1%.");

            report.Verdict = report.Issues.Count > 0
                ? FragmentAuditVerdict.Invalid
                : FragmentAuditVerdict.Valid;
            return report;
        }

        static string FormatUnbalance(double ratio) => $"{ratio * 100:F2}%";
    }
}
