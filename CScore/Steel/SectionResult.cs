using System.Collections.Generic;
using System.Linq;

namespace CScore;

/// <summary>
/// Результат проверки стального сечения по СП 16.
/// </summary>
public class SteelCheckResult
{
    public string LoadCaseName { get; set; } = "";
    public double Utilization { get; set; }
    public bool IsPassed => Utilization <= 1.0;
    public List<CheckDetail> Details { get; set; } = [];
    public CheckDetail? WorstCase => Details.OrderByDescending(d => d.Ratio).FirstOrDefault();
}

/// <summary>
/// Детали одной проверки.
/// </summary>
public class CheckDetail
{
    public string Formula { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Ссылка на пункт нормы (например, "СП 16.13330.2017, п. 9.2.2").</summary>
    public string NormReference { get; set; } = "";
    public double Applied { get; set; }
    public double Allowable { get; set; }
    public double Ratio => Allowable > 0 ? Applied / Allowable : 0;
    public bool Passed => Ratio <= 1.0;
    /// <summary>Переменные расчёта (для отчёта: φ, λ̄, η и т.д.).</summary>
    public Dictionary<string, double> Variables { get; set; } = [];
}
