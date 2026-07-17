namespace CScore.Import;

/// <summary>Результат импорта РСУ из бинарного файла SCAD RSU2.</summary>
public class ScadRsu2ImportResult
{
    public List<ForceSet> ForceSets { get; } = [];
    public string? Error { get; set; }
    public bool Success => Error == null && ForceSets.Count > 0;
}
