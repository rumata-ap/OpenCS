namespace CScore.Fem.Import;

/// <summary>Адаптер импорта расчётной схемы из сторонней FEM-программы.</summary>
public interface IFemImporter
{
    /// <summary>Код источника: "lira" | "scad" | "robot" | "rfem" — пишется в FemSchema.SourceType
    /// и в происхождение групп (<see cref="FemMemberGroup.ImportOrigin"/>).</summary>
    string SourceType { get; }
    /// <summary>Фильтр для диалога открытия файла, например "ЛираСАПР (*.lir)|*.lir".</summary>
    string FileFilter { get; }
    /// <summary>Читает схему. Сохраняет её вызывающий — в новую схему через <c>DatabaseService.SaveFemImport</c>.</summary>
    FemImportResult Import(string filePath, IFemImportContext ctx);
}
