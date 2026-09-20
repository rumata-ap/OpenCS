namespace OpenCS.Reporting.Formula;

/// <summary>Базовый узел переносимого математического выражения.</summary>
public abstract record MathNode
{
    /// <summary>Последовательность узлов.</summary>
    public sealed record Sequence(IReadOnlyList<MathNode> Items) : MathNode;

    /// <summary>Обычный текстовый фрагмент.</summary>
    public sealed record Text(string Value) : MathNode;

    /// <summary>Математический оператор или символ.</summary>
    public sealed record Operator(string Value) : MathNode;

    /// <summary>Дробь.</summary>
    public sealed record Fraction(MathNode Numerator, MathNode Denominator) : MathNode;

    /// <summary>Квадратный корень.</summary>
    public sealed record Radical(MathNode Radicand) : MathNode;

    /// <summary>Основание с нижним и/или верхним индексом.</summary>
    public sealed record Script(MathNode Base, MathNode? Subscript, MathNode? Superscript) : MathNode;
}
