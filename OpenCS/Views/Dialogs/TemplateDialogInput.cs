using OpenCS.Services;
using OpenCS.Utilites;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenCS.Views.Dialogs;

/// <summary>
/// Общий ввод шаблонных диалогов контура: размеры в мм через <see cref="NumberInput"/> («,» и «.»), проверка
/// <see cref="ContourAuthoring.ValidateTemplate"/>, ошибка — красной рамкой у поля и сообщением с его подписью.
/// </summary>
static class TemplateDialogInput
{
    /// <summary>Поле размера: путь ошибки службы (<c>width</c>, <c>height</c>, <c>tw</c>, <c>tf</c>, <c>d</c>), поле и ключ подписи.</summary>
    public sealed record Field(string Path, TextBox Box, string LabelKey);

    /// <summary>
    /// Читает размеры (мм → м) и проверяет их; false — показано сообщение, фокус в поле с ошибкой.
    /// </summary>
    public static bool TryRead(Window owner, ContourTemplate kind, IReadOnlyList<Field> fields, int segments,
        out ContourTemplateSize size)
    {
        size = null!;
        var mm = new Dictionary<string, double>();
        foreach (var f in fields)
        {
            Mark(f.Box, false);
            if (!NumberInput.TryParse(f.Box.Text, out double v))
                return Fail(owner, f, Loc.S("ContourErrNotFinite"));
            mm[f.Path] = v / 1000.0;
        }
        double Get(string p) => mm.TryGetValue(p, out var v) ? v : 0;
        size = kind == ContourTemplate.Circle
            ? new ContourTemplateSize(Get("d"), Segments: segments)
            : new ContourTemplateSize(Get("width"), Get("height"), Get("tw"), Get("tf"));
        var errors = ContourAuthoring.ValidateTemplate(kind, size);
        if (errors.Count == 0) return true;
        var e = errors[0];
        var field = fields.FirstOrDefault(f => f.Path == e.Path);
        if (field is null)
        {
            MessageBox.Show(owner, e.Message, Loc.S("Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return Fail(owner, field, e.Message);
    }

    static bool Fail(Window owner, Field f, string message)
    {
        Mark(f.Box, true);
        MessageBox.Show(owner, $"{Loc.S(f.LabelKey).TrimEnd(':')}: {message}", Loc.S("Warning"),
            MessageBoxButton.OK, MessageBoxImage.Warning);
        f.Box.Focus();
        f.Box.SelectAll();
        return false;
    }

    static void Mark(TextBox box, bool error)
    {
        if (error) box.BorderBrush = Brushes.Red;
        else box.ClearValue(Control.BorderBrushProperty);
    }

    /// <summary>Имя контура: введённое или подпись шаблона.</summary>
    public static string NameOr(TextBox box, string fallbackKey) =>
        string.IsNullOrWhiteSpace(box.Text) ? Loc.S(fallbackKey) : box.Text.Trim();
}
