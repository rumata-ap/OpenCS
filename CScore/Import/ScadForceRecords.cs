namespace CScore.Import;

/// <summary>
/// Усилия КЭ SCAD (ApiGetEffors) в единицах «т, м», первый слой: значения загружений
/// [точка][загружение][усилие] и комбинаций [точка][комбинация][усилие]; порядок усилий — <see cref="Types"/>
/// (коды TypeUs SCAD).
/// </summary>
/// <param name="ElemId">Номер КЭ SCAD.</param>
/// <param name="Kind">Стержень или оболочка.</param>
/// <param name="Types">Коды усилий в точке.</param>
/// <param name="Points">Число точек (сечений стержня; у пластины — 1, центр).</param>
/// <param name="LoadCases">Усилия от загружений.</param>
/// <param name="LoadCount">Число загружений.</param>
/// <param name="Combinations">Усилия от комбинаций загружений (пусто — не читались).</param>
/// <param name="CombinationCount">Число комбинаций.</param>
public sealed record ScadElementForces(int ElemId, ScadElementKind Kind, byte[] Types, int Points,
    double[] LoadCases, int LoadCount, double[] Combinations, int CombinationCount)
{
    /// <summary>Усилия точки <paramref name="point"/> (с 0) от загружения <paramref name="load"/> (с 0).</summary>
    public ReadOnlySpan<double> LoadCase(int point, int load) =>
        LoadCases.AsSpan((point * LoadCount + load) * Types.Length, Types.Length);

    /// <summary>Усилия точки <paramref name="point"/> (с 0) от комбинации <paramref name="combination"/> (с 0).</summary>
    public ReadOnlySpan<double> Combination(int point, int combination) =>
        Combinations.AsSpan((point * CombinationCount + combination) * Types.Length, Types.Length);
}

/// <summary>Строка РСУ SCAD (ApiElemRsuStr).</summary>
/// <param name="Point">Точка / сечение, с 1.</param>
/// <param name="Group">GroupRsu: 0 — расчётные, 1 — расчётные длительные, 2 — нормативные, 3 — нормативные длительные.</param>
/// <param name="Criterion">Номер критерия.</param>
/// <param name="Us">Усилия в порядке TypeUs КЭ (т, м).</param>
public sealed record ScadRsuRow(int Point, int Group, int Criterion, double[] Us);

/// <summary>РСУ КЭ SCAD (ApiGetRsu); коды усилий — те же, что у ApiGetEffors этого КЭ.</summary>
public sealed record ScadRsuElement(int ElemId, ScadElementKind Kind, byte[] Types, IReadOnlyList<ScadRsuRow> Rows);

/// <summary>Имена загружений и комбинаций проекта SCAD (индекс 0 — № 1); пустая строка — имени нет.</summary>
public sealed record ScadResultCatalog(IReadOnlyList<string> LoadNames, IReadOnlyList<string> CombinationNames);
