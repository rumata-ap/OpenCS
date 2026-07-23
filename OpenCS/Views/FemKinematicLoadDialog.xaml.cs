using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CScore.Fem;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Диалог задания статических перемещений и поворотов выбранным узлам.</summary>
public partial class FemKinematicLoadDialog : Window
{
    readonly IReadOnlyList<FemNode> _nodes;
    readonly FemSchemaEditorVM _editor;
    bool _initializing = true;

    public FemKinematicLoadDialog(IReadOnlyList<FemNode> nodes, FemSchemaEditorVM editor)
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
        var fields = Fields();
        for (int i = 0; i < fields.Length; i++)
        {
            int dof = i + 1;
            var load = _nodes.Select(node => _editor.Session.KinematicLoads.FirstOrDefault(item =>
                    item.LoadCaseId == loadCase.Id && item.NodeId == node.Id && item.Dof == dof))
                .FirstOrDefault(item => item != null);
            fields[i].Check.IsChecked = load != null;
            fields[i].Box.Text = load?.Value.ToString("G15", CultureInfo.CurrentCulture) ?? "0";
        }
    }

    void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (loadCaseCombo.SelectedItem is not FemLoadCase loadCase) return;
        var values = new Dictionary<int, double>();
        foreach (var (dof, field) in Fields().Select((field, index) => (index + 1, field)))
        {
            if (field.Check.IsChecked != true) continue;
            if (double.TryParse(field.Box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) &&
                double.IsFinite(value))
                values[dof] = value;
        }

        int applied = _editor.ApplyKinematicLoads(loadCase, _nodes, values);
        if (applied < _nodes.Count)
            MessageBox.Show(Loc.S("FemNodeLoadSkippedUnsaved"), Loc.S("FemKinematicLoadToolTip"),
                MessageBoxButton.OK, MessageBoxImage.Information);
    }

    (CheckBox Check, TextBox Box)[] Fields() =>
    [
        (uxCheck, uxBox), (uyCheck, uyBox), (uzCheck, uzBox),
        (rxCheck, rxBox), (ryCheck, ryBox), (rzCheck, rzBox)
    ];
}
