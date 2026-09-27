using System.Collections.ObjectModel;
using CScore;
using OpenCS.Services;

namespace OpenCS.ViewModels;

/// <summary>Элемент дерева для параметрического МК-сечения.</summary>
public sealed class ParametricSteelSectionTreeItem
{
    /// <summary>Фактическое сечение из общего пула CrossSections.</summary>
    public CrossSection Section { get; }
    /// <summary>Состояние исходной параметрической записи.</summary>
    public ParametricSteelSectionState State { get; }
    /// <summary>Области фактического сечения для раскрытия узла.</summary>
    public ObservableCollection<MaterialArea> Areas { get; }
    /// <summary>Метка сечения.</summary>
    public string Tag => Section.Tag;
    /// <summary>Создаёт элемент дерева.</summary>
    public ParametricSteelSectionTreeItem(CrossSection section, ParametricSteelSectionState state)
    {
        Section = section ?? throw new ArgumentNullException(nameof(section));
        State = state ?? throw new ArgumentNullException(nameof(state));
        Areas = new(section.Areas);
    }
}

/// <summary>Папка «Параметрические МК» с поддержанными неустаревшими сечениями.</summary>
public sealed class ParametricSteelSectionTreeGroup
{
    /// <summary>Элементы папки.</summary>
    public ObservableCollection<ParametricSteelSectionTreeItem> Items { get; }
    /// <summary>Создаёт папку.</summary>
    public ParametricSteelSectionTreeGroup(ObservableCollection<ParametricSteelSectionTreeItem> items)
        => Items = items;
}
