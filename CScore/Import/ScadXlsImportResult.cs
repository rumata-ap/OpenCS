namespace CScore.Import;

/// <summary>Результат импорта усилий из XLS SCAD.</summary>
public class ScadXlsImportResult
{
    public List<ForceSet> ForceSets { get; } = [];
    public string? Error { get; set; }
    public string? Warning { get; set; }
    public int SheetsRead { get; set; }
    public int RowsMatched { get; set; }

    public bool Success => Error == null && ForceSets.Count > 0;
}
