using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CScore.Combinations;
using CScore.Fem.Combinations;
using OpenCS.Utilites;

namespace OpenCS.Views;

/// <summary>
/// «Из огибающей линейного расчёта»: группы C/CL/N/NL/A, допуск к огибающей и лимит числа комбинаций в группе.
/// </summary>
public partial class FemEnvelopeSelectionDialog : Window
{
    /// <summary>Параметры отбора; заданы после OK.</summary>
    public FemEnvelopeSelectionOptions? Options { get; private set; }

    readonly bool _hasImportedLoads;

    /// <param name="current">Текущие параметры (из настроек расчёта); лимит 0 — поле пустое.</param>
    /// <param name="hasImportedLoads">В схеме есть импортированные загружения — при «нормативных» предупреждение.</param>
    public FemEnvelopeSelectionDialog(string source, FemEnvelopeSelectionOptions current, bool hasImportedLoads)
    {
        InitializeComponent();
        _hasImportedLoads = hasImportedLoads;
        sourceText.Text = source;
        valueKindBox.SelectedIndex = current.ValueKind == Sp20LoadValueKind.Design ? 1 : 0;
        var groups = current.Groups ?? FemEnvelopeCombinations.DefaultGroups;
        foreach (var box in GroupBoxes()) box.IsChecked = groups.Contains((string)box.Tag);
        toleranceBox.Text = (current.Tolerance * 100).ToString("G", CultureInfo.CurrentCulture);
        maxBox.Text = current.MaxCount > 0 ? current.MaxCount.ToString(CultureInfo.CurrentCulture) : "";
        Loaded += (_, _) => { toleranceBox.Focus(); toleranceBox.SelectAll(); };
    }

    IEnumerable<CheckBox> GroupBoxes() => groupsPanel.Children.OfType<CheckBox>();

    Sp20LoadValueKind SelectedValueKind => valueKindBox.SelectedIndex == 1 ? Sp20LoadValueKind.Design : Sp20LoadValueKind.Normative;

    void ValueKind_Changed(object sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged приходит ещё из InitializeComponent — до создания предупреждения ниже по разметке.
        if (valueKindWarning == null) return;
        valueKindWarning.Visibility = _hasImportedLoads && SelectedValueKind == Sp20LoadValueKind.Normative
            ? Visibility.Visible : Visibility.Collapsed;
    }

    void Apply_Click(object sender, RoutedEventArgs e)
    {
        var groups = GroupBoxes().Where(b => b.IsChecked == true).Select(b => (string)b.Tag).ToList();
        if (groups.Count == 0)
        {
            MessageBox.Show(this, Loc.S("FemEnvelopeSelectionNoGroups"), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        int max = 0;
        bool maxOk = string.IsNullOrWhiteSpace(maxBox.Text)
            || int.TryParse(maxBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out max) && max >= 0;
        if (!Pars.ParseAny(toleranceBox.Text, out var tolerance) || !double.IsFinite(tolerance) || tolerance < 0
            || tolerance > 100 || !maxOk)
        {
            MessageBox.Show(this, Loc.S("FemEnvelopeSelectionInvalid"), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Options = new FemEnvelopeSelectionOptions(tolerance / 100, max) { Groups = groups, ValueKind = SelectedValueKind };
        DialogResult = true;
    }
}
