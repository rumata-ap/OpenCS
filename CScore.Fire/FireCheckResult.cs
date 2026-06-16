namespace CScore.Fire;

/// <summary>Результат проверки огнестойкости (R, I, E).</summary>
public sealed class FireCheckResult
{
    /// <summary>Критерий: R, I или E.</summary>
    public string Criterion { get; init; } = "R";

    /// <summary>Проверка пройдена (запас ≥ 0).</summary>
    public bool Passed { get; init; }

    /// <summary>Запас: factor − 1.</summary>
    public double Margin { get; init; }

    /// <summary>Критическое время снапшота, мин.</summary>
    public double? CriticalTimeMin { get; init; }

    /// <summary>Детали расчёта для отчёта и UI.</summary>
    public Dictionary<string, object?> Details { get; init; } = [];
}
