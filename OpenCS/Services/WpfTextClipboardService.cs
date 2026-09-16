using System.Windows;

namespace OpenCS.Services;

/// <summary>WPF-реализация копирования текста в буфер обмена.</summary>
public sealed class WpfTextClipboardService : ITextClipboardService
{
    /// <inheritdoc />
    public void SetText(string text) => Clipboard.SetText(text);
}
