using System.Runtime.InteropServices;
using System.Windows;

namespace OpenCS.Utilites;

/// <summary>Запись в буфер обмена без падения: буфер, занятый другой программой (CLIPBRD_E_CANT_OPEN), — false.</summary>
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
}
