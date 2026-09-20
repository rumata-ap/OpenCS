namespace OpenCS.Reporting.Formula;

/// <summary>Безопасный parser ограниченного подмножества LaTeX для отчётов.</summary>
public static class LatexMathParser
{
    /// <summary>Разбирает выражение; неизвестные команды сохраняются как текст.</summary>
    public static MathNode Parse(string? source)
        => new Parser(source ?? string.Empty).Parse();

    sealed class Parser
    {
        readonly string source;
        int position;

        public Parser(string source) => this.source = source;

        public MathNode Parse() => ParseSequence(null);

        MathNode ParseSequence(char? closing)
        {
            var items = new List<MathNode>();
            while (position < source.Length)
            {
                if (closing.HasValue && source[position] == closing.Value)
                {
                    position++;
                    break;
                }

                if (source[position] is '^' or '_')
                {
                    char marker = source[position++];
                    MathNode script = ParseAtom();
                    if (items.Count == 0)
                    {
                        items.Add(new MathNode.Text(marker + RenderPlain(script)));
                        continue;
                    }

                    var baseNode = items[^1];
                    items[^1] = marker == '_'
                        ? MergeScript(baseNode, script, isSubscript: true)
                        : MergeScript(baseNode, script, isSubscript: false);
                    continue;
                }

                items.Add(ParseAtom());
            }

            return items.Count == 1 ? items[0] : new MathNode.Sequence(items);
        }

        MathNode ParseAtom()
        {
            if (position >= source.Length)
                return new MathNode.Text(string.Empty);

            char current = source[position];
            if (current == '{')
            {
                position++;
                return ParseSequence('}');
            }

            if (current == '\\')
                return ParseCommand();

            position++;
            return new MathNode.Text(current.ToString());
        }

        MathNode ParseCommand()
        {
            position++;
            int start = position;
            while (position < source.Length && char.IsLetter(source[position]))
                position++;
            string command = source[start..position];
            if (command.Length == 0)
                return new MathNode.Text("\\");

            if (command is "frac" or "sqrt")
            {
                MathNode first = ParseRequiredGroupOrAtom();
                if (command == "sqrt")
                    return new MathNode.Radical(first);

                MathNode second = ParseRequiredGroupOrAtom();
                return new MathNode.Fraction(first, second);
            }

            return command switch
            {
                "cdot" => new MathNode.Operator("·"),
                "le" => new MathNode.Operator("≤"),
                "ge" => new MathNode.Operator("≥"),
                "times" => new MathNode.Operator("×"),
                "sum" => new MathNode.Operator("∑"),
                "alpha" => new MathNode.Operator("α"),
                "beta" => new MathNode.Operator("β"),
                "gamma" => new MathNode.Operator("γ"),
                "delta" => new MathNode.Operator("δ"),
                "epsilon" => new MathNode.Operator("ε"),
                "xi" => new MathNode.Operator("ξ"),
                "eta" => new MathNode.Operator("η"),
                "sigma" => new MathNode.Operator("σ"),
                "rho" => new MathNode.Operator("ρ"),
                "phi" => new MathNode.Operator("φ"),
                "pi" => new MathNode.Operator("π"),
                _ => new MathNode.Text("\\" + command)
            };
        }

        MathNode ParseRequiredGroupOrAtom()
        {
            while (position < source.Length && char.IsWhiteSpace(source[position]))
                position++;
            return ParseAtom();
        }

        static MathNode MergeScript(MathNode baseNode, MathNode script, bool isSubscript)
        {
            if (baseNode is MathNode.Script existing)
                return isSubscript
                    ? existing with { Subscript = script }
                    : existing with { Superscript = script };

            return isSubscript
                ? new MathNode.Script(baseNode, script, null)
                : new MathNode.Script(baseNode, null, script);
        }

        static string RenderPlain(MathNode node) => node switch
        {
            MathNode.Text text => text.Value,
            MathNode.Operator op => op.Value,
            _ => string.Empty
        };
    }
}
