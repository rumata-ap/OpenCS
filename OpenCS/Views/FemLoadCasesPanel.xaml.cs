using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class FemLoadCasesPanel : UserControl
{
    FemSchemaEditorVM? Editor => DataContext as FemSchemaEditorVM;

    public FemLoadCasesPanel() => InitializeComponent();

    void NewLoadCase_Click(object sender, RoutedEventArgs e)
        => Editor?.AddLoadCase(Loc.S("FemLoadCaseDefaultTag"), "short_term");

    void ApplyLoad_Click(object sender, RoutedEventArgs e)
    {
        if (Editor is not { } editor) return;
        double Parse(TextBox box) => double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        var skipped = editor.ApplyLoadToSelection(Parse(fxBox), Parse(fyBox), Parse(fzBox), Parse(mxBox), Parse(myBox), Parse(mzBox));
        if (skipped.Count > 0)
            MessageBox.Show(
                string.Format(Loc.S("FemNodeLoadSkippedUnsaved"), string.Join(", ", skipped)),
                Loc.S("FemNodeLoadSkippedUnsavedTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
