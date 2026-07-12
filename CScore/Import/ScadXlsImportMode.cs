namespace CScore.Import;

/// <summary>Режим импорта усилий из XLS-отчёта SCAD.</summary>
public enum ScadXlsImportMode
{
    LoadCases,
    Rsu,
    /// <summary>Величины усилий от комбинаций загружений (РСН).</summary>
    Combinations,
}
