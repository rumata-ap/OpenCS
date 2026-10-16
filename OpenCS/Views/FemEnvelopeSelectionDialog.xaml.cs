using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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

    /// <param name="current">Текущие параметры (из настроек расчёта); лимит 0 — поле пустое.</param>
    public FemEnvelopeSelectionDialog(string source, FemEnvelopeSelectionOptions current)
    {
        InitializeComponent();
        sourceText.Text = source;
        var groups = current.Groups ?? FemEnvelopeCombinations.DefaultGroups;
        foreach (var box in GroupBoxes()) box.IsChecked = groups.Contains((string)box.Tag);
        toleranceBox.Text = (current.Tolerance * 100).ToString("G", CultureInfo.CurrentCulture);
        maxBox.Text = current.MaxCount > 0 ? current.MaxCount.ToString(CultureInfo.CurrentCulture) : "";
        Loaded += (_, _) => { toleranceBox.Focus(); toleranceBox.SelectAll(); };
    }

    IEnumerable<CheckBox> GroupBoxes() => groupsPanel.Children.OfType<CheckBox>();

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
        Options = new FemEnvelopeSelectionOptions(tolerance / 100, max) { Groups = groups };
        DialogResult = true;
    }
}
