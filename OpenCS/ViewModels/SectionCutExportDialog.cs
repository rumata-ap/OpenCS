using OpenCS.Utilites;
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
    /// <summary>Показывает диалог с прежним выбором из <paramref name="remember"/> и записывает туда новый (по OK).</summary>
    public static SectionCutExportOptions? Show(SectionCutWindowSettings? remember = null)
    {
        var win = new SectionCutExportDialogWindow(remember == null ? null : Recall(remember))
        {
            Owner = Application.Current?.MainWindow
        };
        if (win.ShowDialog() != true || win.Result == null) return null;
        if (remember != null) Store(remember, win.Result);
        return win.Result;
    }

    /// <summary>Прежний выбор из настроек; неизвестный формат — PNG.</summary>
    public static SectionCutExportOptions Recall(SectionCutWindowSettings s) => new(
        s.ExportFormat?.ToLowerInvariant() switch
        {
            "svg" => SectionCutExportFormat.Svg,
            "dxf" => SectionCutExportFormat.Dxf,
            _ => SectionCutExportFormat.Png
        }, s.ExportAsOnScreen);

    /// <summary>Записывает выбор в настройки.</summary>
    public static void Store(SectionCutWindowSettings s, SectionCutExportOptions options)
    {
        s.ExportFormat = options.Format.ToString().ToLowerInvariant();
        s.ExportAsOnScreen = options.AsOnScreen;
    }
}
