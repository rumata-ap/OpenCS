using System.Windows;

namespace OpenCS.Views.Dialogs;

/// <summary>Владелец модального диалога: активное окно (диалог из другого окна — поверх него), иначе главное.</summary>
public static class DialogOwner
{
    public static Window? Active()
    {
        var app = Application.Current;
        if (app == null) return null;
        return app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible) ?? app.MainWindow;
    }
}
