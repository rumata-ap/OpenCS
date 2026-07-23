using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CScore.Fem;
using CScore.Fem.Editing;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Диалог задания одной узловой нагрузки выбранным узлам 3D-схемы.</summary>
public partial class FemNodeLoadDialog : Window
{
    readonly IReadOnlyList<FemNode> _nodes;
    readonly FemSchemaEditorVM _editor;
    bool _initializing = true;

    public FemNodeLoadDialog(IReadOnlyList<FemNode> nodes, FemSchemaEditorVM editor)
    {
        InitializeComponent();
        _nodes = nodes;
        _editor = editor;
        selectionText.Text = string.Format(Loc.S("FemLoadSelectedCount"), nodes.Count);
        loadCaseCombo.ItemsSource = editor.Session.LoadCases;
        loadCaseCombo.SelectedItem = editor.SelectedLoadCase ?? editor.Session.LoadCases.FirstOrDefault();
        LoadFields();
        _initializing = false;
    }

    void LoadCaseCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing) LoadFields();
    }

    void LoadFields()
    {
        if (loadCaseCombo.SelectedItem is not FemLoadCase loadCase) return;
        var load = _nodes.Select(node => _editor.Session.NodeLoads.FirstOrDefault(item =>
                item.LoadCaseId == loadCase.Id && item.NodeId == node.Id))
            .FirstOrDefault(item => item != null);
        fxBox.Text = FemUnitConverter.NewtonsToKiloNewtons(load?.Fx ?? 0).ToString("G15", CultureInfo.CurrentCulture);
        fyBox.Text = FemUnitConverter.NewtonsToKiloNewtons(load?.Fy ?? 0).ToString("G15", CultureInfo.CurrentCulture);
        fzBox.Text = FemUnitConverter.NewtonsToKiloNewtons(load?.Fz ?? 0).ToString("G15", CultureInfo.CurrentCulture);
        mxBox.Text = FemUnitConverter.NewtonMetersToKiloNewtonMeters(load?.Mx ?? 0).ToString("G15", CultureInfo.CurrentCulture);
        myBox.Text = FemUnitConverter.NewtonMetersToKiloNewtonMeters(load?.My ?? 0).ToString("G15", CultureInfo.CurrentCulture);
        mzBox.Text = FemUnitConverter.NewtonMetersToKiloNewtonMeters(load?.Mz ?? 0).ToString("G15", CultureInfo.CurrentCulture);
    }

    void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (loadCaseCombo.SelectedItem is not FemLoadCase loadCase) return;
        double Parse(TextBox box) => double.TryParse(box.Text, NumberStyles.Float,
            CultureInfo.CurrentCulture, out var value) ? value : 0;
        double fx = FemUnitConverter.KiloNewtonsToNewtons(Parse(fxBox));
        double fy = FemUnitConverter.KiloNewtonsToNewtons(Parse(fyBox));
        double fz = FemUnitConverter.KiloNewtonsToNewtons(Parse(fzBox));
        double mx = FemUnitConverter.KiloNewtonMetersToNewtonMeters(Parse(mxBox));
        double my = FemUnitConverter.KiloNewtonMetersToNewtonMeters(Parse(myBox));
        double mz = FemUnitConverter.KiloNewtonMetersToNewtonMeters(Parse(mzBox));

        int applied = 0;
        foreach (var node in _nodes.Where(node => node.Id != 0))
        {
            _editor.Session.Execute(new SetNodeLoadCommand(loadCase.Id, node.Id, fx, fy, fz, mx, my, mz));
            applied++;
        }
        _editor.RefreshCollections();
        if (applied < _nodes.Count)
            MessageBox.Show(Loc.S("FemNodeLoadSkippedUnsaved"), Loc.S("FemNodeLoadToolTip"),
                MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
