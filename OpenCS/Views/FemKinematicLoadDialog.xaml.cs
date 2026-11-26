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
        // Без загружений нагрузку задать некуда — сказать об этом сразу, а не молчать на «Применить».
        noLoadCasesText.Visibility = editor.Session.LoadCases.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
            // В окне, как в таблице загружения, — мм и мрад (в базе — м и рад).
            fields[i].Box.Text = ((load?.Value ?? 0) * 1e3).ToString("0.######", CultureInfo.CurrentCulture);
        }
    }

    void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (loadCaseCombo.SelectedItem is not FemLoadCase loadCase)
        {
            MessageBox.Show(this, Loc.S("FemNoLoadCasesHint"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        // Нераспознанное значение отмеченного направления — ошибка у поля; раньше оно молча удаляло заданное
        // перемещение (направления нет в values → удаление).
        var values = new Dictionary<int, double>();
        string[] keys = ["FemKinematicLoadUxMilli", "FemKinematicLoadUyMilli", "FemKinematicLoadUzMilli",
            "FemKinematicLoadRxMilli", "FemKinematicLoadRyMilli", "FemKinematicLoadRzMilli"];
        var fields = Fields();
        Helpers.FieldCheck.Clear(fields.Select(f => f.Box).ToArray());
        for (int i = 0; i < fields.Length; i++)
        {
            if (fields[i].Check.IsChecked != true) continue;
            if (!Helpers.FieldCheck.Read(this, fields[i].Box, keys[i], out double milli)) return;
            values[i + 1] = milli / 1e3;
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
