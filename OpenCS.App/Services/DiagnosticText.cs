using System.Globalization;
using CScore.Fem;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Текст диагностики CScore на языке интерфейса: у <see cref="FemValidationDiagnostic.Text"/> шаблон <c>Diag_&lt;ключ&gt;</c>
/// из ресурсов текущего языка с теми же аргументами (числа — по культуре языка), вложенные тексты — так же; ключа в
/// ресурсах нет — русский шаблон каталога. Без <c>Text</c> — <see cref="FemValidationDiagnostic.Message"/>.
/// </summary>
public static class DiagnosticText
{
    public const string KeyPrefix = "Diag_";

    public static string Format(FemValidationDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        return diagnostic.Text is { } text ? Format(text) : diagnostic.Message;
    }

    public static string Format(DiagText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var culture = Loc.Language is { } language ? CultureInfo.GetCultureInfo(language) : CultureInfo.CurrentCulture;
        return Format(text, culture);
    }

    static string Format(DiagText text, CultureInfo culture)
    {
        string? template = text.Key != DiagText.JoinKey && Loc.TryGet(KeyPrefix + text.Key, out var t) ? t : null;
        try
        {
            return text.Format(culture, template, nested => Format(nested, culture));
        }
        catch (FormatException)
        {
            // Шаблон перевода разошёлся с аргументами — русский текст, а не исключение в окне отчёта.
            return text.Format(culture, null, nested => Format(nested, culture));
        }
    }
}
