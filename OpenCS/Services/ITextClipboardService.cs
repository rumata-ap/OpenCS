namespace OpenCS.Services;

/// <summary>Абстракция записи текста в системный буфер обмена.</summary>
public interface ITextClipboardService
{
    /// <summary>Помещает текст в системный буфер обмена.</summary>
    void SetText(string text);
}
