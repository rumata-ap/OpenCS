namespace CScore.Fem.Editing;

/// <summary>Обратимая команда редактирования FEM-схемы.</summary>
public interface IFemEditCommand
{
    void Do(FemSchemaEditSession session);
    void Undo(FemSchemaEditSession session);
}
