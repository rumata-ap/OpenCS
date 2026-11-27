using System.Runtime.InteropServices;
using System.Windows;

namespace OpenCS.Utilites;

/// <summary>
/// Запись текста в буфер обмена без падения: буфер, занятый другой программой (CLIPBRD_E_CANT_OPEN), — три попытки,
/// затем false. Единственное место программы, где текст пишется в буфер (страж — UiLint «clipboard»).
/// </summary>
public static class UiClipboard
{
    public static bool TrySetText(string text)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, true);
                return true;
            }
            catch (ExternalException)
            {
                Thread.Sleep(50);
            }
        }
        return false;
    }

    /// <summary>
    /// Копирование по команде пользователя: не вышло — сообщение «буфер занят» (окно-владелец — <paramref name="owner"/>
    /// или главное окно), чтобы отказ не был молчаливым.
    /// </summary>
    public static bool Copy(string text, Window? owner = null)
    {
        if (TrySetText(text)) return true;
        owner ??= Application.Current?.MainWindow;
        string title = owner?.Title is { Length: > 0 } t ? t : "OpenCS";
        if (owner != null)
            MessageBox.Show(owner, Loc.S("ClipboardBusy"), title, MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            MessageBox.Show(Loc.S("ClipboardBusy"), title, MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }
}
