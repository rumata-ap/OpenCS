using System.Globalization;
using System.Windows;
using CScore.Fem.Combinations;
using OpenCS.Utilites;

namespace OpenCS.Views;

/// <summary>
/// «Из огибающей линейного расчёта»: допуск к огибающей и лимит числа комбинаций при отборе формул.
/// </summary>
public partial class FemEnvelopeSelectionDialog : Window
{
    /// <summary>Параметры отбора; заданы после OK.</summary>
    public FemEnvelopeSelectionOptions? Options { get; private set; }

    /// <param name="tolerancePercent">Допуск, % (из настроек расчёта).</param>
    /// <param name="maxCount">Лимит; 0 — без ограничения (поле пустое).</param>
    public FemEnvelopeSelectionDialog(string source, double tolerancePercent, int maxCount)
    {
        InitializeComponent();
        sourceText.Text = source;
        toleranceBox.Text = tolerancePercent.ToString("G", CultureInfo.CurrentCulture);
        maxBox.Text = maxCount > 0 ? maxCount.ToString(CultureInfo.CurrentCulture) : "";
        Loaded += (_, _) => { toleranceBox.Focus(); toleranceBox.SelectAll(); };
    }

    void Apply_Click(object sender, RoutedEventArgs e)
    {
        int max = 0;
        bool maxOk = string.IsNullOrWhiteSpace(maxBox.Text)
            || int.TryParse(maxBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out max) && max >= 0;
        if (!Pars.ParseAny(toleranceBox.Text, out var tolerance) || !double.IsFinite(tolerance) || tolerance < 0
            || tolerance > 100 || !maxOk)
        {
            MessageBox.Show(this, Loc.S("FemEnvelopeSelectionInvalid"), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Options = new FemEnvelopeSelectionOptions(tolerance / 100, max);
        DialogResult = true;
    }
}
