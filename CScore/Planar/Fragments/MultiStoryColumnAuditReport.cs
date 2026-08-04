namespace CScore.Planar.Fragments
{
    /// <summary>Аудит результата многоэтажной колонны: 4 группы критериев (геометрия/сетка,
    /// баланс сил, сходимость/энергетика, СП 63.13330), последняя — по shell-слоям всех
    /// перекрытий И фибрам всех сегментов колонны вместе.</summary>
    public class MultiStoryColumnAuditReport
    {
        public FragmentAuditVerdict Verdict { get; set; } = FragmentAuditVerdict.Invalid;
        public List<string> Issues { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();

        public MultiStoryColumnAuditReport Audit(MultiStoryColumnFragment fragment, MultiStoryColumnResult result)
        {
            var report = new MultiStoryColumnAuditReport();
            if (result is null)
            {
                report.Verdict = FragmentAuditVerdict.Invalid;
                report.Issues.Add("Результат расчёта равен null.");
                return report;
            }

            // Группа 1: геометрия и сетка.
            foreach (var diagnostic in result.MeshDiagnostics)
                report.Issues.Add($"Сетка: {diagnostic}");
            foreach (var diagnostic in result.AssemblyDiagnostics)
                report.Issues.Add($"Сборка: {diagnostic}");

            // Группа 2: баланс сил.
            foreach (var diagnostic in result.BoundaryDiagnostics)
                report.Issues.Add($"Граничные воздействия: {diagnostic}");
            if (result.ForceBalance is { } balance)
            {
                if (!double.IsFinite(balance.RelativeUnbalance))
                    report.Issues.Add("Невязка равновесия модели не является конечной.");
                else if (balance.RelativeUnbalance > 1e-3)
                    report.Issues.Add(
                        $"Невязка равновесия модели {balance.RelativeUnbalance * 100:F2}% превышает допуск 0.1%.");
            }

            // Группа 3: сходимость и энергетика.
            foreach (var diagnostic in result.AnalysisDiagnostics)
                report.Issues.Add($"Расчёт: {diagnostic}");
            if (!result.IsConverged)
                report.Issues.Add("Нелинейный расчёт не достиг полной нагрузки.");
            if (result.EnergyConfidence == "Unavailable")
                report.Warnings.Add("Энергетическая проверка недоступна для этого результата.");

            // Группа 4: нормативные пределы СП 63.13330 (shell-слои плит и фибры колонны вместе).
            if (result.MaxConcreteCompressionStrain < -0.0035)
                report.Issues.Add(
                    $"Предельная деформация сжатия бетона {result.MaxConcreteCompressionStrain:F5} " +
                    $"превышает допуск СП 63.13330 (-0.0035).");
            if (result.MaxRebarTensileStrain > 0.025)
                report.Issues.Add(
                    $"Предельная деформация растяжения арматуры {result.MaxRebarTensileStrain:F5} " +
                    $"превышает допуск СП 63.13330 (0.025).");

            report.Verdict = report.Issues.Count > 0 ? FragmentAuditVerdict.Invalid : FragmentAuditVerdict.Valid;
            return report;
        }
    }
}
