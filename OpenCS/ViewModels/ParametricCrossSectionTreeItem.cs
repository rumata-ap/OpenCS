using System.Collections.ObjectModel;
using CScore;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Обёртка дерева, которая показывает source, но раскрывает исходный CrossSection.</summary>
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
    /// <summary>Текст статуса source.</summary>
    public string Status => State.LoadStatus switch
    {
        ParametricRcDefinitionLoadStatus.Supported when State.IsStale => Loc.S("ParametricRcStatusStale"),
        ParametricRcDefinitionLoadStatus.UnsupportedFutureVersion => Loc.S("ParametricRcStatusFuture"),
        ParametricRcDefinitionLoadStatus.InvalidJson => Loc.S("ParametricRcStatusInvalid"),
        _ => Loc.S("ParametricRcStatusSupported")
    };

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

/// <summary>Папка source-записей с неподдержанным будущим форматом или ошибочным JSON.</summary>
public sealed class ParametricCrossSectionSourceStatusGroup
{
    /// <summary>Элементы папки.</summary>
    public ObservableCollection<ParametricCrossSectionTreeItem> Items { get; }
    /// <summary>Создаёт папку.</summary>
    public ParametricCrossSectionSourceStatusGroup(ObservableCollection<ParametricCrossSectionTreeItem> items)
        => Items = items;
}
