using System.Windows;
using System.Windows.Controls;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Вкладка «Комбинации» редактора схемы: список комбинаций и их состав (до матрицы среза 5б).</summary>
public partial class FemCombinationsPanel : UserControl
{
    FemSchemaEditorVM? Editor => DataContext as FemSchemaEditorVM;

    public FemCombinationsPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => PopulateDefinitionName();
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
        if (Editor == null || !Pars.ParseAny(definitionCoefficientBox.Text, out var coefficient)) return;
        Editor.UpdateSelectedLoadDefinitionTermCoefficient(coefficient);
    }

    void DefinitionSelectionChanged(object sender, SelectionChangedEventArgs e) => PopulateDefinitionName();

    void PopulateDefinitionName()
        => definitionNameBox.Text = Editor?.SelectedLoadDefinition?.Tag ?? "";
}
