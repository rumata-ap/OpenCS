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
        if (loadCaseCombo.SelectedItem is not FemLoadCase loadCase)
        {
            MessageBox.Show(this, Loc.S("FemNoLoadCasesHint"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        // Нераспознанное значение — ошибка у поля, а не молчаливый 0.
        Helpers.FieldCheck.Clear(fxBox, fyBox, fzBox, mxBox, myBox, mzBox);
        if (!Helpers.FieldCheck.Read(this, fxBox, "FemNodePropsFx", out var fxK)
            || !Helpers.FieldCheck.Read(this, fyBox, "FemNodePropsFy", out var fyK)
            || !Helpers.FieldCheck.Read(this, fzBox, "FemNodePropsFz", out var fzK)
            || !Helpers.FieldCheck.Read(this, mxBox, "FemNodePropsMx", out var mxK)
            || !Helpers.FieldCheck.Read(this, myBox, "FemNodePropsMy", out var myK)
            || !Helpers.FieldCheck.Read(this, mzBox, "FemNodePropsMz", out var mzK)) return;
        double fx = FemUnitConverter.KiloNewtonsToNewtons(fxK);
        double fy = FemUnitConverter.KiloNewtonsToNewtons(fyK);
        double fz = FemUnitConverter.KiloNewtonsToNewtons(fzK);
        double mx = FemUnitConverter.KiloNewtonMetersToNewtonMeters(mxK);
        double my = FemUnitConverter.KiloNewtonMetersToNewtonMeters(myK);
        double mz = FemUnitConverter.KiloNewtonMetersToNewtonMeters(mzK);

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
