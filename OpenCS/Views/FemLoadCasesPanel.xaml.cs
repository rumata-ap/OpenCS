using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class FemLoadCasesPanel : UserControl
{
    FemSchemaEditorVM? Editor => DataContext as FemSchemaEditorVM;

    public FemLoadCasesPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => PopulateLoadCaseParameters();
    }

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

    void NewDefinition_Click(object sender, RoutedEventArgs e)
        => Editor?.AddManualLoadDefinition(Loc.S("FemLoadDefinitionDefaultTag"));

    void DeleteDefinition_Click(object sender, RoutedEventArgs e)
        => Editor?.DeleteSelectedLoadDefinition();

    void AddCaseToDefinition_Click(object sender, RoutedEventArgs e)
        => Editor?.AddSelectedLoadCaseToDefinition();

    void DeleteDefinitionTerm_Click(object sender, RoutedEventArgs e)
        => Editor?.DeleteSelectedLoadDefinitionTerm();

    void GenerateSp20_Click(object sender, RoutedEventArgs e)
        => Editor?.GenerateSp20LoadDefinitions("fundamental");

    void SaveDefinitionTermCoefficient_Click(object sender, RoutedEventArgs e)
    {
        if (Editor == null || !double.TryParse(definitionCoefficientBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var coefficient)) return;
        Editor.UpdateSelectedLoadDefinitionTermCoefficient(coefficient);
    }

    void SaveLoadCaseParameters_Click(object sender, RoutedEventArgs e)
    {
        if (Editor == null || loadCaseTypeCombo.SelectedValue is not string sp20Type) return;
        double? ParseOptional(TextBox box) => double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
        Editor.UpdateSelectedLoadCase(loadCaseTagBox.Text, sp20Type, loadCaseGroupBox.Text,
            ParseOptional(gammaUnfavBox), ParseOptional(gammaFavBox), ParseOptional(psi1Box), ParseOptional(psi2Box));
    }

    void LoadCaseSelectionChanged(object sender, SelectionChangedEventArgs e) => PopulateLoadCaseParameters();

    void PopulateLoadCaseParameters()
    {
        if (Editor?.SelectedLoadCase is not { } loadCase) return;
        loadCaseTagBox.Text = loadCase.Tag;
        loadCaseTypeCombo.SelectedValue = loadCase.Sp20Type;
        loadCaseGroupBox.Text = loadCase.Sp20Group ?? "";
        gammaUnfavBox.Text = loadCase.GammaFUnfav?.ToString(CultureInfo.InvariantCulture) ?? "";
        gammaFavBox.Text = loadCase.GammaFFav?.ToString(CultureInfo.InvariantCulture) ?? "";
        psi1Box.Text = loadCase.Psi1?.ToString(CultureInfo.InvariantCulture) ?? "";
        psi2Box.Text = loadCase.Psi2?.ToString(CultureInfo.InvariantCulture) ?? "";
    }
}
