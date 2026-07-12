using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CScore.Import;
using OpenCS.Utilites;

namespace OpenCS.Views;

public partial class ScadForceImportDialog : Window
{
    readonly string? _memberElements;

    public string ElementText { get; private set; } = "";
    public bool ImportAllElements { get; private set; }
    /// <summary>Толщина пластины из диалога, мм.</summary>
    public double ThicknessMm { get; private set; }

    public ScadForceImportDialog(string? initialElementsFromMember = null)
    {
        InitializeComponent();
        _memberElements = initialElementsFromMember;
        if (!string.IsNullOrWhiteSpace(initialElementsFromMember))
            ElementsBox.Text = initialElementsFromMember;
        UpdateFilterEnabled();
        UpdateOkEnabled();
    }

    void ElementsBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateOkEnabled();
    void ThicknessBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateOkEnabled();

    void AllElements_Changed(object sender, RoutedEventArgs e)
    {
        UpdateFilterEnabled();
        UpdateOkEnabled();
    }

    void FromMember_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_memberElements))
        {
            MessageBox.Show(
                Loc.S("ScadForceImportNoMember"),
                Loc.S("ImportScadErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        AllElementsCheck.IsChecked = false;
        ElementsBox.Text = _memberElements;
        UpdateFilterEnabled();
        UpdateOkEnabled();
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseThicknessMm(out double mm))
            return;
        ImportAllElements = AllElementsCheck.IsChecked == true;
        ThicknessMm = mm;
        if (!ImportAllElements)
        {
            if (!ScadElementIdParser.TryParse(ElementsBox.Text, out _, out _))
                return;
            ElementText = ElementsBox.Text.Trim();
        }
        else
        {
            ElementText = "";
        }
        DialogResult = true;
    }

    void UpdateFilterEnabled()
    {
        bool all = AllElementsCheck.IsChecked == true;
        ElementsBox.IsEnabled = !all;
        FromMemberButton.IsEnabled = !all;
    }

    void UpdateOkEnabled()
    {
        if (OkButton == null) return; // TextChanged во время InitializeComponent
        bool thicknessOk = TryParseThicknessMm(out _);
        bool elementsOk = AllElementsCheck.IsChecked == true
            || ScadElementIdParser.TryParse(ElementsBox.Text, out _, out _);
        OkButton.IsEnabled = thicknessOk && elementsOk;
    }

    bool TryParseThicknessMm(out double mm)
    {
        string s = (ThicknessBox.Text ?? "").Trim().Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out mm) && mm > 0;
    }
}
