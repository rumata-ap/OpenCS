using System;

namespace OpenCS.Utilites;

/// <summary>
/// Категории данных проекта, сохраняемые через <see cref="DatabaseService.SaveAll"/>.
/// Наборы усилий отслеживаются отдельно — <see cref="CScore.ForceSet.IsModified"/>.
/// </summary>
[Flags]
public enum SaveCategory
{
    None          = 0,
    Materials     = 1 << 0,
    Contours      = 1 << 1,
    Circles       = 1 << 2,
    Diagrams      = 1 << 3,
    CrossSections = 1 << 4,
    PlateSections = 1 << 5,
    FireSections  = 1 << 6,
    CalcTasks     = 1 << 7,

    /// <summary>Все категории SaveAll (кроме ForceSet).</summary>
    All = Materials | Contours | Circles | Diagrams | CrossSections | PlateSections | FireSections | CalcTasks
}
