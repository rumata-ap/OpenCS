using System.Globalization;
using System.Windows;
using CScore.Fem;
using CScore.Fem.Combinations;
using OpenCS.Utilites;

namespace OpenCS.Views;

/// <summary>
/// Комбинация: имя, тип (C, CL, N, NL или свой) и коэффициенты к загружениям схемы — пустой или нулевой коэффициент
/// загружение не включает. Режим «все загружения» и сочетания СП 20 раскрываются в явные слагаемые.
/// </summary>
public partial class FemCombinationDialog : Window
{
    /// <summary>Строка загружения: подпись и коэффициент текстом.</summary>
    public sealed class TermRow(FemLoadCase loadCase, string label, string coefficient)
    {
        public FemLoadCase LoadCase { get; } = loadCase;
        public string Label { get; } = label;
        public string Coefficient { get; set; } = coefficient;
    }

    readonly List<TermRow> _rows;

    public string DefinitionTag => tagBox.Text.Trim();
    public string? CombinationType { get; private set; }
    public IReadOnlyList<FemLoadTerm> Terms { get; private set; } = [];

    /// <summary>Проверка у вызывающего (имя занято и т. п.): текст ошибки или null — закрыть.</summary>
    public Func<string, string?>? Validate { get; init; }

    public FemCombinationDialog(FemLoadDefinition definition, IReadOnlyList<FemLoadCase> loadCases)
    {
        InitializeComponent();
        var expression = definition.GetExpression();
        tagBox.Text = definition.Tag;
        typeCombo.Text = expression.CombinationType ?? definition.CombinationType ?? "";

        var coefficients = new Dictionary<int, double>();
        try
        {
            foreach (var (loadCase, coefficient) in FemLoadExpressionResolver.Terms(expression, loadCases))
                coefficients[loadCase.Id] = coefficients.GetValueOrDefault(loadCase.Id) + coefficient;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            ShowError(ex.Message);
        }
        _rows = loadCases.Select((lc, i) => new TermRow(lc, $"{i + 1}. {lc.Tag}",
            coefficients.TryGetValue(lc.Id, out var k) ? k.ToString("0.####", CultureInfo.CurrentCulture) : "")).ToList();
        termsList.ItemsSource = _rows;
        Loaded += (_, _) => { tagBox.Focus(); tagBox.SelectAll(); };
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(tagBox.Text)) { ShowError(Loc.S("FemCombinationTagTaken")); return; }
        var terms = new List<FemLoadTerm>();
        foreach (var row in _rows)
        {
            string text = row.Coefficient.Trim();
            if (text.Length == 0) continue;
            if (!Pars.ParseAny(text, out double k) || !double.IsFinite(k))
            {
                ShowError(string.Format(Loc.S("FemCombinationDialogBadCoefficient"), row.Label));
                return;
            }
            if (k != 0) terms.Add(new FemLoadTerm { LoadCaseId = row.LoadCase.Id, Coefficient = k });
        }
        if (Validate?.Invoke(tagBox.Text.Trim()) is { } error) { ShowError(error); return; }
        CombinationType = string.IsNullOrWhiteSpace(typeCombo.Text) ? null : typeCombo.Text.Trim();
        Terms = terms;
        DialogResult = true;
    }

    void ShowError(string message)
    {
        errorText.Text = message;
        errorText.Visibility = Visibility.Visible;
    }
}
