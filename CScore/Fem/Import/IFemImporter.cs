namespace CScore.Fem.Import;

/// <summary>Адаптер импорта расчётной схемы из сторонней FEM-программы.</summary>
public interface IFemImporter
{
    /// <summary>Код источника: "lira" | "robot" | "rfem"</summary>
    string SourceType { get; }
    /// <summary>Фильтр для диалога открытия файла, например "ЛираСАПР (*.lir)|*.lir".</summary>
    string FileFilter { get; }
    FemSchema Import(string filePath, IFemImportContext ctx);
}
