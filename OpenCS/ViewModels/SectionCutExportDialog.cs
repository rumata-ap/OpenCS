using OpenCS.Views;
using System.Windows;

namespace OpenCS.ViewModels;

public enum SectionCutExportFormat
{
    Png,
    Svg,
    Dxf
}

/// <summary>Результат диалога экспорта эпюры разреза.</summary>
public sealed record SectionCutExportOptions(SectionCutExportFormat Format, bool AsOnScreen);

/// <summary>Диалог выбора формата и режима экспорта эпюры разреза.</summary>
public static class SectionCutExportDialog
{
    public static SectionCutExportOptions? Show()
    {
        var win = new SectionCutExportDialogWindow
        {
            Owner = Application.Current?.MainWindow
        };
        return win.ShowDialog() == true ? win.Result : null;
    }
}
