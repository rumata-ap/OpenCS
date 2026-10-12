using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>
/// Таблицы и графики результата расчёта схемы CSfea: перемещения узлов, усилия стержней и пластин показанного шага,
/// «λ по шагам» и «λ — перемещение контрольного узла», журнал шагов и отчёт; ползунок шага, «шаг → наборы усилий». Сам результат (деформации, мозаики, эпюры) — в 3D-виде схемы.
/// </summary>
public partial class FemCsfeaResultView : UserControl
{
    readonly FemCsfeaResultVM _vm;
    bool _syncingGrid;

    /// <summary>«Показать в 3D-виде схемы» — на шаге этого окна.</summary>
    public Action? ShowInSchema { get; init; }

    public FemCsfeaResultView(FemCsfeaResultVM vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        reportBox.Text = string.Join(Environment.NewLine, vm.ReportLines);
        if (vm.IsLinear)
        {
            // Линейный расчёт: шагов по λ и итераций нет — графики и колонки хода решения не показываются.
            chartsTab.Visibility = Visibility.Collapsed;
            shellStateColumn.Visibility = Visibility.Collapsed;
            foreach (var column in new[] { lambdaColumn, plannedColumn, convergedColumn, iterationsColumn, residualColumn, fieldsColumn })
                column.Visibility = Visibility.Collapsed;
        }
        UpdateCharts();
        lambdaCanvas.StepClicked += i => _vm.SelectedStepIndex = i;
        controlCanvas.StepClicked += i => _vm.SelectedStepIndex = i;
        _vm.PropertyChanged += OnVmPropertyChanged;
        SyncGrid();
    }

    void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FemCsfeaResultVM.SelectedStepIndex)) return;
        UpdateCharts();
        SyncGrid();
    }

    void UpdateCharts()
    {
        lambdaCanvas.SetData(_vm.LambdaPoints, _vm.SelectedStepIndex);
        controlCanvas.SetData(_vm.ControlPoints, _vm.SelectedStepIndex);
    }

    void SyncGrid()
    {
        _syncingGrid = true;
        try
        {
            if (_vm.SelectedStepIndex < _vm.StepRows.Count)
            {
                stepsGrid.SelectedIndex = _vm.SelectedStepIndex;
                if (stepsGrid.SelectedItem != null) stepsGrid.ScrollIntoView(stepsGrid.SelectedItem);
            }
        }
        finally { _syncingGrid = false; }
    }

    void StepsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingGrid && stepsGrid.SelectedIndex >= 0) _vm.SelectedStepIndex = stepsGrid.SelectedIndex;
    }

    void ShowInSchema_Click(object sender, RoutedEventArgs e) => ShowInSchema?.Invoke();
}
