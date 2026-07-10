namespace CScore.Import;

/// <summary>Результат разбора текстового формата SCAD.</summary>
public class ScadImportResult
{
    public ScadSchemaData? Data { get; set; }
    public List<string>    Warnings { get; } = [];
    public string?         Error { get; set; }

    public bool Success => Error == null;
}
