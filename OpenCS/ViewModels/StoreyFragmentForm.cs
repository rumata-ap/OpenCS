using System.Globalization;
using CScore.Fem;
using CScore.Fem.Fragment;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Поля диалога «Фрагмент по отметкам…»: отметки z₁ &lt; z₂ (м) и имя новой схемы.</summary>
public static class StoreyFragmentForm
{
    /// <summary>Имя по умолчанию: «‹родитель› · z₁…z₂».</summary>
    public static string DefaultName(string parentTag, double z1, double z2, CultureInfo culture) =>
        $"{parentTag} · {z1.ToString("0.###", culture)}…{z2.ToString("0.###", culture)}";

    /// <summary>Запрос сборки из полей; ошибка — текст для пользователя, запрос null.</summary>
    public static StoreyFragmentRequest? TryRead(string z1Text, string z2Text, string name, out string? error)
    {
        error = null;
        if (!TryLevel(z1Text, out double z1) || !TryLevel(z2Text, out double z2))
            error = Loc.S("StoreyFragmentLevelInvalid");
        else if (z1 >= z2)
            error = Loc.S("StoreyFragmentLevelsOrder");
        else if (string.IsNullOrWhiteSpace(name))
            error = Loc.S("StoreyFragmentNameEmpty");
        else
            return new StoreyFragmentRequest(z1, z2, name.Trim());
        return null;
    }

    static bool TryLevel(string text, out double z) =>
        Pars.ParseAny(text?.Trim() ?? "", out z) && double.IsFinite(z);
}

/// <summary>Строка отчёта сборки фрагмента для окна отчёта.</summary>
public sealed record StoreyFragmentReportRow(string Level, string Message, bool IsError);

/// <summary>Диагностика сборки → строки окна отчёта: сначала ошибки, затем предупреждения.</summary>
public static class StoreyFragmentReportRows
{
    public static IReadOnlyList<StoreyFragmentReportRow> From(IEnumerable<FemValidationDiagnostic> diagnostics) =>
        diagnostics.OrderByDescending(d => d.IsError)
            .Select(d => new StoreyFragmentReportRow(
                Loc.S(d.IsError ? "StoreyFragmentReportError" : "StoreyFragmentReportWarning"), d.Message, d.IsError))
            .ToList();

    /// <summary>Текст для буфера обмена: «уровень⇥сообщение» по строке.</summary>
    public static string ToText(IEnumerable<StoreyFragmentReportRow> rows) =>
        string.Join(Environment.NewLine, rows.Select(r => $"{r.Level}\t{r.Message}"));
}
