using System.Runtime.InteropServices;

namespace OpenCS.Services;

/// <summary>WPF-реализация копирования текста в буфер обмена.</summary>
public sealed class WpfTextClipboardService : ITextClipboardService
{
    /// <inheritdoc />
    /// <remarks>Через <see cref="Utilites.UiClipboard"/> (три попытки); буфер так и не освободился —
    /// <see cref="ExternalException"/>, как у <c>Clipboard.SetText</c>: вызывающие показывают её сообщение.</remarks>
    public void SetText(string text)
    {
        if (!Utilites.UiClipboard.TrySetText(text))
            throw new ExternalException(Utilites.Loc.S("ClipboardBusy"));
    }
}
