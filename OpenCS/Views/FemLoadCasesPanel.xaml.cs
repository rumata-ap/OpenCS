using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class FemLoadCasesPanel : UserControl
{
    FemSchemaEditorVM? Editor => DataContext as FemSchemaEditorVM;
    bool _updatingMemberLoad;

    public FemLoadCasesPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            PopulateLoadCaseParameters();
            PopulateDefinitionName();
        };
    }

    void NewLoadCase_Click(object sender, RoutedEventArgs e)
        => Editor?.AddLoadCase(Loc.S("FemLoadCaseDefaultTag"), "short_term");

    void RenameLoadCase_Click(object sender, RoutedEventArgs e)
    {
        if (Editor?.TryRenameSelectedLoadCase(loadCaseNameBox.Text) != true)
            MessageBox.Show(Loc.S("FemRenameInvalid"), Loc.S("FemRename"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            loadCaseNameBox.Text = Editor.SelectedLoadCase?.Tag ?? "";
    }

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

    void RenameDefinition_Click(object sender, RoutedEventArgs e)
    {
        if (Editor?.TryRenameSelectedLoadDefinition(definitionNameBox.Text) != true)
            MessageBox.Show(Loc.S("FemRenameInvalid"), Loc.S("FemRename"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            definitionNameBox.Text = Editor.SelectedLoadDefinition?.Tag ?? "";
    }

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
        if (!Editor.UpdateSelectedLoadCase(loadCaseTagBox.Text, sp20Type, loadCaseGroupBox.Text,
            ParseOptional(gammaUnfavBox), ParseOptional(gammaFavBox), ParseOptional(psi1Box), ParseOptional(psi2Box)))
            MessageBox.Show(Loc.S("FemRenameInvalid"), Loc.S("FemRename"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    void LoadCaseSelectionChanged(object sender, SelectionChangedEventArgs e) => PopulateLoadCaseParameters();

    void DefinitionSelectionChanged(object sender, SelectionChangedEventArgs e) => PopulateDefinitionName();

    void MemberLoadMemberChanged(object sender, SelectionChangedEventArgs e) => PopulateMemberLoad();

    void ApplyMemberLoad_Click(object sender, RoutedEventArgs e)
    {
        if (Editor is not { } editor) return;
        if (editor.SelectedLoadCase == null || editor.SelectedLoadMember == null) return;

        double Parse(TextBox box) => double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
            ? value : 0;
        var coordinateSystem = memberLoadCoordinateCombo.SelectedValue as string ?? "local";
        var distributionType = memberLoadDistributionCombo.SelectedValue as string ?? "uniform";
        var qxStart = Parse(memberLoadQxStartBox);
        var qyStart = Parse(memberLoadQyStartBox);
        var qzStart = Parse(memberLoadQzStartBox);
        var qxEnd = distributionType == "uniform" ? qxStart : Parse(memberLoadQxEndBox);
        var qyEnd = distributionType == "uniform" ? qyStart : Parse(memberLoadQyEndBox);
        var qzEnd = distributionType == "uniform" ? qzStart : Parse(memberLoadQzEndBox);
        if (!editor.ApplyMemberLoad(
                Parse(memberLoadStartOffsetBox), Parse(memberLoadEndOffsetBox), coordinateSystem, distributionType,
                qxStart, qyStart, qzStart, qxEnd, qyEnd, qzEnd))
            MessageBox.Show(Loc.S("FemMemberLoadSkippedUnsaved"), Loc.S("FemSaveBlockedTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        PopulateMemberLoad();
    }

    void DeleteMemberLoad_Click(object sender, RoutedEventArgs e)
    {
        Editor?.DeleteMemberLoad();
        PopulateMemberLoad();
    }

    void PopulateLoadCaseParameters()
    {
        if (Editor?.SelectedLoadCase is not { } loadCase)
        {
            loadCaseNameBox.Text = "";
            PopulateMemberLoad();
            return;
        }
        loadCaseNameBox.Text = loadCase.Tag;
        loadCaseTagBox.Text = loadCase.Tag;
        loadCaseTypeCombo.SelectedValue = loadCase.Sp20Type;
        loadCaseGroupBox.Text = loadCase.Sp20Group ?? "";
        gammaUnfavBox.Text = loadCase.GammaFUnfav?.ToString(CultureInfo.InvariantCulture) ?? "";
        gammaFavBox.Text = loadCase.GammaFFav?.ToString(CultureInfo.InvariantCulture) ?? "";
        psi1Box.Text = loadCase.Psi1?.ToString(CultureInfo.InvariantCulture) ?? "";
        psi2Box.Text = loadCase.Psi2?.ToString(CultureInfo.InvariantCulture) ?? "";
        PopulateMemberLoad();
    }

    void PopulateDefinitionName()
        => definitionNameBox.Text = Editor?.SelectedLoadDefinition?.Tag ?? "";

    void PopulateMemberLoad()
    {
        if (_updatingMemberLoad) return;
        _updatingMemberLoad = true;
        try
        {
            var load = Editor?.SelectedLoadMember is { } member ? Editor.FindMemberLoad(member) : null;
            memberLoadCoordinateCombo.SelectedValue = load?.CoordinateSystem ?? "local";
            memberLoadDistributionCombo.SelectedValue = load?.DistributionType ?? "uniform";
            memberLoadStartOffsetBox.Text = Format(load?.StartOffsetM ?? 0);
            memberLoadEndOffsetBox.Text = Format(load?.EndOffsetM ?? 0);
            memberLoadQxStartBox.Text = Format(load?.QxStart ?? 0);
            memberLoadQyStartBox.Text = Format(load?.QyStart ?? 0);
            memberLoadQzStartBox.Text = Format(load?.QzStart ?? 0);
            memberLoadQxEndBox.Text = Format(load?.QxEnd ?? 0);
            memberLoadQyEndBox.Text = Format(load?.QyEnd ?? 0);
            memberLoadQzEndBox.Text = Format(load?.QzEnd ?? 0);
        }
        finally { _updatingMemberLoad = false; }
    }

    static string Format(double value) => value.ToString("G15", CultureInfo.CurrentCulture);
}
