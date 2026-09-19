using System.Collections.ObjectModel;
using CScore;
using OpenCS.Services;

namespace OpenCS.ViewModels;

/// <summary>Элемент дерева для параметрического ЖБ-сечения.</summary>
public sealed class ParametricCrossSectionTreeItem
{
    /// <summary>Фактическое сечение из общего пула CrossSections.</summary>
    public CrossSection Section { get; }
    /// <summary>Состояние исходной параметрической записи.</summary>
    public ParametricRcSectionState State { get; }
    /// <summary>Области фактического сечения для раскрытия узла.</summary>
    public ObservableCollection<MaterialArea> Areas { get; }
    /// <summary>Метка сечения.</summary>
    public string Tag => Section.Tag;
    /// <summary>Создаёт элемент дерева.</summary>
    public ParametricCrossSectionTreeItem(CrossSection section, ParametricRcSectionState state)
    {
        Section = section ?? throw new ArgumentNullException(nameof(section));
        State = state ?? throw new ArgumentNullException(nameof(state));
        Areas = new(section.Areas);
    }
}

/// <summary>Папка supported параметрических сечений.</summary>
public sealed class ParametricCrossSectionTreeGroup
{
    /// <summary>Элементы папки.</summary>
    public ObservableCollection<ParametricCrossSectionTreeItem> Items { get; }
    /// <summary>Создаёт папку.</summary>
    public ParametricCrossSectionTreeGroup(ObservableCollection<ParametricCrossSectionTreeItem> items)
        => Items = items;
}
