namespace CScore.Fem.Fields;

/// <summary>Способ переноса: интерполяция по триангуляции участка, ближайшая точка, обратные расстояния, один к одному (узлы).</summary>
public enum FieldMethod { Interpolate, Nearest, Idw, Match }

/// <summary>Как получено значение цели.</summary>
public enum FieldHow { Interpolated, Nearest, Idw, Matched, None }

/// <summary>Параметры переноса; расстояния — м.</summary>
/// <param name="MaxDistance">Предельное расстояние до точки таблицы (вне облака и для IDW/ближайшей).</param>
/// <param name="PlaneTolerance">Допуск точки до плоскости участка и узлов пластины до плоскости при делении.</param>
/// <param name="MatchTolerance">Допуск поиска узла для способа «один к одному».</param>
public sealed record FieldSettings(
    FieldMethod Method = FieldMethod.Interpolate,
    double MaxDistance = 1,
    double PlaneTolerance = 0.5,
    double AngleTolDeg = 5,
    int IdwK = 8,
    double IdwPower = 2,
    double MatchTolerance = 0.5);

/// <summary>Значение цели (по столбцам значений таблицы, в единицах ввода) или null, расстояние до точки таблицы.</summary>
public sealed record FieldTargetValue(string Tag, double[]? Values, FieldHow How, double Distance);

/// <summary>Замечание переноса: код, текст, теги целей (или пусто).</summary>
public sealed record FieldDiagnostic(string Code, string Message, IReadOnlyList<string> Tags);

/// <summary>Результат переноса поля на цели.</summary>
public sealed record FieldEvaluation(
    IReadOnlyList<FieldTargetValue> Targets,
    IReadOnlyList<FieldDiagnostic> Diagnostics,
    IReadOnlyList<PlanarPatch> Patches,
    int PointCount,
    int PointsUnused);

/// <summary>Значения допусков по умолчанию от размера КЭ целей.</summary>
public static class FieldDefaults
{
    /// <summary>Медиана √площади пластин, м; 1 — если пластин нет.</summary>
    public static double MedianSize(IReadOnlyList<FieldShell> shells)
    {
        if (shells.Count == 0) return 1;
        var s = shells.Select(x => Math.Sqrt(x.Area)).OrderBy(x => x).ToList();
        return s[s.Count / 2];
    }

    /// <summary>Предельное расстояние 2 × размер КЭ, допуск до плоскости — его половина (спека § 3.1).</summary>
    public static FieldSettings For(IReadOnlyList<FieldShell> shells, FieldMethod method = FieldMethod.Interpolate)
    {
        double h = MedianSize(shells);
        return new FieldSettings(method, MaxDistance: 2 * h, PlaneTolerance: h);
    }
}
