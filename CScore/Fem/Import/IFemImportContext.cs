namespace CScore.Fem.Import;

/// <summary>Контекст импорта — предоставляет адаптеру доступ к коллекциям проекта.</summary>
public interface IFemImportContext
{
    /// <summary>
    /// Регистрирует ForceSet с привязкой к импортируемой схеме.
    /// Возвращает сохранённый объект с присвоенным Id.
    /// </summary>
    ForceSet RegisterForceSet(string tag, int schemaId, string elementTag);
}
