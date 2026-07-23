using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CScore.Fem;
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
        double fx = FemUnitConverter.KiloNewtonsToNewtons(Parse(fxBox));
        double fy = FemUnitConverter.KiloNewtonsToNewtons(Parse(fyBox));
        double fz = FemUnitConverter.KiloNewtonsToNewtons(Parse(fzBox));
        double mx = FemUnitConverter.KiloNewtonMetersToNewtonMeters(Parse(mxBox));
        double my = FemUnitConverter.KiloNewtonMetersToNewtonMeters(Parse(myBox));
        double mz = FemUnitConverter.KiloNewtonMetersToNewtonMeters(Parse(mzBox));
        var skipped = editor.ApplyLoadToSelection(fx, fy, fz, mx, my, mz);
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

    void MemberLoadDistributionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingMemberLoad) UpdateMemberLoadVisibility();
    }

    bool IsMemberLoadPoint => (memberLoadDistributionCombo.SelectedValue as string) == "point";

    void UpdateMemberLoadVisibility()
    {
        var pointVisibility = IsMemberLoadPoint ? Visibility.Visible : Visibility.Collapsed;
        var nonPointVisibility = IsMemberLoadPoint ? Visibility.Collapsed : Visibility.Visible;
        memberLoadEndOffsetPanel.Visibility = nonPointVisibility;
        memberLoadEndHeaderText.Visibility = nonPointVisibility;
        memberLoadQxEndBox.Visibility = nonPointVisibility;
        memberLoadQyEndBox.Visibility = nonPointVisibility;
        memberLoadQzEndBox.Visibility = nonPointVisibility;
        memberLoadMomentGrid.Visibility = pointVisibility;
    }

    void ApplyMemberLoad_Click(object sender, RoutedEventArgs e)
    {
        if (Editor is not { } editor) return;
        if (editor.SelectedLoadCase == null || editor.SelectedLoadMember == null) return;

        double Parse(TextBox box) => double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
            ? value : 0;
        var coordinateSystem = memberLoadCoordinateCombo.SelectedValue as string ?? "local";
        var distributionType = memberLoadDistributionCombo.SelectedValue as string ?? "uniform";
        bool isPoint = distributionType == "point";
        var qxStart = FemUnitConverter.KiloNewtonsToNewtons(Parse(memberLoadQxStartBox));
        var qyStart = FemUnitConverter.KiloNewtonsToNewtons(Parse(memberLoadQyStartBox));
        var qzStart = FemUnitConverter.KiloNewtonsToNewtons(Parse(memberLoadQzStartBox));
        var qxEnd = isPoint ? 0 : distributionType == "uniform" ? qxStart : FemUnitConverter.KiloNewtonsToNewtons(Parse(memberLoadQxEndBox));
        var qyEnd = isPoint ? 0 : distributionType == "uniform" ? qyStart : FemUnitConverter.KiloNewtonsToNewtons(Parse(memberLoadQyEndBox));
        var qzEnd = isPoint ? 0 : distributionType == "uniform" ? qzStart : FemUnitConverter.KiloNewtonsToNewtons(Parse(memberLoadQzEndBox));
        var endOffset = isPoint ? 0 : Parse(memberLoadEndOffsetBox);
        var mx = isPoint ? FemUnitConverter.KiloNewtonMetersToNewtonMeters(Parse(memberLoadMxBox)) : 0;
        var my = isPoint ? FemUnitConverter.KiloNewtonMetersToNewtonMeters(Parse(memberLoadMyBox)) : 0;
        var mz = isPoint ? FemUnitConverter.KiloNewtonMetersToNewtonMeters(Parse(memberLoadMzBox)) : 0;
        if (!editor.ApplyMemberLoad(
                Parse(memberLoadStartOffsetBox), endOffset, coordinateSystem, distributionType,
                qxStart, qyStart, qzStart, qxEnd, qyEnd, qzEnd, mx, my, mz))
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
            memberLoadQxStartBox.Text = Format(FemUnitConverter.NewtonsToKiloNewtons(load?.QxStart ?? 0));
            memberLoadQyStartBox.Text = Format(FemUnitConverter.NewtonsToKiloNewtons(load?.QyStart ?? 0));
            memberLoadQzStartBox.Text = Format(FemUnitConverter.NewtonsToKiloNewtons(load?.QzStart ?? 0));
            memberLoadQxEndBox.Text = Format(FemUnitConverter.NewtonsToKiloNewtons(load?.QxEnd ?? 0));
            memberLoadQyEndBox.Text = Format(FemUnitConverter.NewtonsToKiloNewtons(load?.QyEnd ?? 0));
            memberLoadQzEndBox.Text = Format(FemUnitConverter.NewtonsToKiloNewtons(load?.QzEnd ?? 0));
            memberLoadMxBox.Text = Format(FemUnitConverter.NewtonMetersToKiloNewtonMeters(load?.Mx ?? 0));
            memberLoadMyBox.Text = Format(FemUnitConverter.NewtonMetersToKiloNewtonMeters(load?.My ?? 0));
            memberLoadMzBox.Text = Format(FemUnitConverter.NewtonMetersToKiloNewtonMeters(load?.Mz ?? 0));
            UpdateMemberLoadVisibility();
        }
        finally { _updatingMemberLoad = false; }
    }

    static string Format(double value) => value.ToString("G15", CultureInfo.CurrentCulture);
}
