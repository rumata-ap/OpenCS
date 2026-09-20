using OpenCS.Reporting.Formula;

namespace OpenCS.Reporting;

/// <summary>Источник математического выражения в нейтральном отчёте.</summary>
public enum MathSourceKind
{
    /// <summary>Ограниченное выражение LaTeX.</summary>
    Latex,

    /// <summary>Существующая безопасная inline-разметка.</summary>
    InlineMarkup,

    /// <summary>Обычный текст без математического преобразования.</summary>
    PlainText
}

/// <summary>Выражение с указанием формата и безопасного fallback.</summary>
public sealed record ReportMathExpression(
    string Source,
    MathSourceKind SourceKind,
    string? FallbackText = null)
{
    /// <summary>Создаёт выражение из LaTeX.</summary>
    public static ReportMathExpression Latex(string source, string? fallbackText = null)
        => new(source ?? string.Empty, MathSourceKind.Latex, fallbackText);

    /// <summary>Создаёт выражение из старой inline-разметки.</summary>
    public static ReportMathExpression Inline(string source)
        => new(source ?? string.Empty, MathSourceKind.InlineMarkup, source);

    /// <summary>Создаёт обычное текстовое выражение.</summary>
    public static ReportMathExpression Plain(string source)
        => new(source ?? string.Empty, MathSourceKind.PlainText, source);

    /// <summary>Преобразует выражение в общий математический AST.</summary>
    public MathNode ToMathNode()
        => SourceKind == MathSourceKind.Latex
            ? LatexMathParser.Parse(Source)
            : new MathNode.Text(FallbackText ?? Source);
}
