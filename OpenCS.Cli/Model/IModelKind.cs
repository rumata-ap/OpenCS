using System.Text.Json.Nodes;
using OpenCS.Services;

namespace OpenCS.Cli.Model;

/// <summary>Изменение проекта из плана apply: вид, ключ объекта, создание или обновление, запись.</summary>
sealed record ModelChange(string Kind, string Key, bool Create, Action<HeadlessProjectContext> Commit);

/// <summary>Вид данных документа opencs-model (раздел верхнего уровня, например «materials»).</summary>
interface IModelKind
{
    string Name { get; }

    /// <summary>Имя JSON-схемы среди встроенных ресурсов (Model/Schemas).</summary>
    string SchemaResource { get; }

    JsonArray Export(HeadlessProjectContext ctx, Action<string> warn);

    /// <summary>
    /// Проверяет и разрешает элементы без записи в проект; ошибки — в <paramref name="errors"/>. Весь документ
    /// (<paramref name="doc"/>) — для ссылок на объекты других разделов, создаваемые тем же документом.
    /// </summary>
    IReadOnlyList<ModelChange> Plan(JsonArray items, JsonObject doc, HeadlessProjectContext ctx, ModelErrors errors);

    /// <summary>
    /// Раздел документа — один объект, а не массив (настройки проекта). Выгрузка и план работают с массивом из одного
    /// элемента; документ пишет и читает сам объект.
    /// </summary>
    bool IsObject => false;

    /// <summary>JSON-схема, собранная в коде (вместо ресурса); null — из <see cref="SchemaResource"/>.</summary>
    string? GeneratedSchema() => null;
}
