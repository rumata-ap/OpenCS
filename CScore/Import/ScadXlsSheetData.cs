namespace CScore.Import;

/// <summary>Лист отчёта SCAD в памяти (для тестов и после чтения Excel).</summary>
public sealed class ScadXlsSheetData
{
    public string Name { get; init; } = "";
    /// <summary>Ячейки [строка][столбец]; отсутствующие = "".</summary>
    public IReadOnlyList<IReadOnlyList<string>> Cells { get; init; } = [];
}
