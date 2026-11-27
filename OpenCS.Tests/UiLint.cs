using System.IO;
using System.Text.RegularExpressions;

namespace OpenCS.Tests;

public sealed record UiLintFinding(string File, int Line, string Rule, string Text);

/// <summary>
/// Правила статической проверки XAML (класс C спеки): литералы мимо Loc; у окна с кнопками — IsDefault и IsCancel;
/// у изменяемого по размеру окна — MinWidth и MinHeight.
/// </summary>
public static partial class UiLint
{
    // Только собственные свойства элемента: helpers:CommandIcons.Header="Ключ" — ключ ресурса, не текст.
    [GeneratedRegex(@"(?<![\w.:])(Text|Content|Header|Title|ToolTip)=""(?<v>[^""{][^""]*)""")]
    private static partial Regex LiteralRegex();

    [GeneratedRegex(@"[A-Za-zА-Яа-яЁё]{2,}")]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"^\s*(<\?xml[^>]*\?>\s*)?<Window\b(?<attrs>[^>]*)>", RegexOptions.Singleline)]
    private static partial Regex WindowRootRegex();

    public static ISet<string> LoadAllow(string repoRoot)
    {
        string path = Path.Combine(repoRoot, "tools", "ui-inventory", "lint-allow.txt");
        return File.Exists(path)
            ? File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToHashSet()
            : new HashSet<string>();
    }

    public static IEnumerable<UiLintFinding> Check(string relPath, string xaml, ISet<string> allow)
    {
        string[] lines = xaml.Split('\n');
        for (int i = 0; i < lines.Length; i++)
            foreach (Match m in LiteralRegex().Matches(lines[i]))
            {
                string v = m.Groups["v"].Value.Trim();
                if (WordRegex().IsMatch(v) && !allow.Contains(v))
                    yield return new(relPath, i + 1, "literal", v);
            }

        var root = WindowRootRegex().Match(xaml);
        if (!root.Success) yield break;
        string attrs = root.Groups["attrs"].Value;
        // Главное окно — не диалог: «OK/Отмена» у него нет, правило минимального размера остаётся.
        bool mainWindow = attrs.Contains("x:Class=\"OpenCS.MainWindow\"");
        if (!mainWindow && xaml.Contains("<Button"))
        {
            if (!xaml.Contains("IsDefault=\"True\"")) yield return new(relPath, 1, "no-default", "нет кнопки IsDefault");
            if (!xaml.Contains("IsCancel=\"True\"")) yield return new(relPath, 1, "no-cancel", "нет кнопки IsCancel");
        }
        bool fixedSize = attrs.Contains("ResizeMode=\"NoResize\"");
        if (!fixedSize && !(attrs.Contains("MinWidth=") && attrs.Contains("MinHeight=")))
            yield return new(relPath, 1, "no-min-size", "нет MinWidth/MinHeight");
    }
}
