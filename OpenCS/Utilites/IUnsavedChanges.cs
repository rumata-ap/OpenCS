using System.ComponentModel;

namespace OpenCS.Utilites;

/// <summary>
/// Страница с правками, которые пишутся в проект только по «Сохранить» (материал, диаграмма). Главное окно
/// спрашивает о сохранении при уходе со страницы и при выходе; заголовок помечается «*», пока <see cref="IsDirty"/>.
/// Изменение <see cref="IsDirty"/> уведомляется через <see cref="INotifyPropertyChanged"/>.
/// </summary>
public interface IUnsavedChanges : INotifyPropertyChanged
{
    /// <summary>Есть правки, не записанные в проект.</summary>
    bool IsDirty { get; }

    /// <summary>Обозначение объекта для вопроса о сохранении.</summary>
    string DisplayName { get; }

    /// <summary>Проверить и записать; false — проверка не прошла (ошибки — в журнале), правки остаются.</summary>
    bool SaveChanges();

    /// <summary>Отбросить правки — вернуть объект к записанному состоянию.</summary>
    void DiscardChanges();
}
