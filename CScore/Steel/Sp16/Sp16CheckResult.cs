namespace CScore.Sp16;

/// <summary>Статус проверки.</summary>
public enum CheckStatus
{
    /// <summary>Условие выполнено.</summary>
    Ok = 0,
    /// <summary>Условие не выполнено.</summary>
    Fail = 1,
    /// <summary>Проверка неприменима (с указанием причины в <see cref="Sp16CheckResult.Note"/>).</summary>
    NotApplicable = 2,
}

/// <summary>
/// Результат одной проверки по СП 16: пункт и номер формулы нормы, коэффициент использования
/// (левая часть условия вида «… ≤ 1»), при наличии — действующее и предельное значения,
/// переменные расчёта и примечания.
/// </summary>
public sealed class Sp16CheckResult
{
    /// <summary>Пункт нормы, например «8.4.1».</summary>
    public string Clause { get; init; } = "";
    /// <summary>Номер формулы или таблицы, например «(69)» или «табл. 9».</summary>
    public string Formula { get; init; } = "";
    /// <summary>Описание проверки.</summary>
    public string Description { get; init; } = "";
    /// <summary>Коэффициент использования (левая часть условия «≤ 1»).</summary>
    public double Utilization { get; init; }
    /// <summary>Действующее значение (для отображения), NaN — нет.</summary>
    public double Applied { get; init; } = double.NaN;
    /// <summary>Предельное значение (для отображения), NaN — нет.</summary>
    public double Allowable { get; init; } = double.NaN;
    /// <summary>Статус.</summary>
    public CheckStatus Status { get; init; }
    /// <summary>Переменные расчёта в порядке вывода.</summary>
    public List<KeyValuePair<string, double>> Variables { get; init; } = [];
    /// <summary>Примечания, предупреждения, причина неприменимости.</summary>
    public List<string> Notes { get; init; } = [];

    /// <summary>Условие «коэффициент использования ≤ 1».</summary>
    public static Sp16CheckResult Of(string clause, string formula, string description, double utilization,
        IEnumerable<(string Name, double Value)>? vars = null, double applied = double.NaN, double allowable = double.NaN,
        IEnumerable<string>? notes = null) => new()
        {
            Clause = clause, Formula = formula, Description = description,
            Utilization = utilization, Applied = applied, Allowable = allowable,
            Status = utilization <= 1.0 + 1e-12 ? CheckStatus.Ok : CheckStatus.Fail,
            Variables = (vars ?? []).Select(v => new KeyValuePair<string, double>(v.Name, v.Value)).ToList(),
            Notes = (notes ?? []).Where(n => !string.IsNullOrEmpty(n)).ToList(),
        };

    /// <summary>Условие «действующее ≤ предельное».</summary>
    public static Sp16CheckResult Limit(string clause, string formula, string description, double applied, double allowable,
        IEnumerable<(string Name, double Value)>? vars = null, IEnumerable<string>? notes = null) =>
        Of(clause, formula, description, allowable > 0 ? applied / allowable : (applied > 0 ? double.PositiveInfinity : 0),
            vars, applied, allowable, notes);

    /// <summary>Неприменимая проверка с причиной.</summary>
    public static Sp16CheckResult NotApplicableFor(string clause, string formula, string description, string reason) => new()
    {
        Clause = clause, Formula = formula, Description = description, Status = CheckStatus.NotApplicable,
        Notes = [reason],
    };
}
