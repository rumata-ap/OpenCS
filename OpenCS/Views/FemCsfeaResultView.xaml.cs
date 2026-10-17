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
        // Столбцы длительных частей — только у пакета, где они считались.
        if (vm.Summary.BatchLong == null) batchLongColumn.Visibility = batchLongNLColumn.Visibility = Visibility.Collapsed;
        else if (!vm.Summary.BatchLong.Any(b => b.Kind == Services.FemCsfeaBatchLongKind.Deflection)) batchLongNLColumn.Visibility = Visibility.Collapsed;
        if (vm.IsCaseList)
        {
            // Список загружений/комбинаций (вариантов пакета) вместо ползунка шагов.
            casePanel.Visibility = Visibility.Visible;
            stepCaption.Visibility = stepSlider.Visibility = stepLabel.Visibility = Visibility.Collapsed;
        }
        if (vm.IsLinear)
        {
            // Линейный расчёт: шагов по λ и итераций нет — графики и колонки хода решения не показываются.
            chartsTab.Visibility = Visibility.Collapsed;
            shellStateColumn.Visibility = Visibility.Collapsed;
            foreach (var column in new[] { lambdaColumn, plannedColumn, convergedColumn, iterationsColumn, residualColumn, fieldsColumn })
                column.Visibility = Visibility.Collapsed;
        }
        if (vm.IsBatch)
        {
            // Пакет: шагов в сводке нет (ход решения вариантов — в журнале приложения) — вместо графиков и таблицы
            // шагов сводка вариантов; журнал — только отчёт.
            chartsTab.Visibility = Visibility.Collapsed;
            batchTab.Visibility = Visibility.Visible;
            tabs.SelectedItem = batchTab;
            stepsGrid.Visibility = Visibility.Collapsed;
            journalGrid.RowDefinitions[0].Height = new GridLength(0);
            journalGrid.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
            vm.ShowBatchProblemsRequested += () => tabs.SelectedItem = batchTab;
        }
        foreach (var grid in new[] { nodeGrid, beamGrid, shellGrid, reactionGrid }) AddEnvelopeColumns(grid);
        UpdateEnvelopeColumns();
        UpdateCharts();
        lambdaCanvas.StepClicked += i => _vm.SelectedStepIndex = i;
        controlCanvas.StepClicked += i => _vm.SelectedStepIndex = i;
        _vm.PropertyChanged += OnVmPropertyChanged;
        SyncGrid();
    }

    readonly List<DataGridColumn> _envelopeColumns = [];

    /// <summary>
    /// Столбцы огибающей: «Ветвь» (max/min) вторым и «Комбинации» последним; у столбцов значений — подсказка ячейки с
    /// комбинацией-источником (<c>Sources[имя свойства]</c> строки; у не-огибающей строки словаря нет — подсказки нет).
    /// </summary>
    void AddEnvelopeColumns(DataGrid grid)
    {
        foreach (var column in grid.Columns.OfType<DataGridBoundColumn>())
        {
            if (column.Binding is not System.Windows.Data.Binding { Path.Path: { } path }) continue;
            var style = new Style(typeof(DataGridCell), column.CellStyle);
            style.Setters.Add(new Setter(ToolTipProperty, new System.Windows.Data.Binding($"Sources[{path}]")));
            column.CellStyle = style;
        }
        var ext = new DataGridTextColumn
        {
            Binding = new System.Windows.Data.Binding(nameof(FemCsfeaNodeRow.Ext)), Header = TryFindResource("FemCsfeaColExt"),
        };
        var combos = new DataGridTextColumn
        {
            Binding = new System.Windows.Data.Binding(nameof(FemCsfeaNodeRow.Combos)), Header = TryFindResource("FemCsfeaColCombos"),
        };
        grid.Columns.Insert(Math.Min(1, grid.Columns.Count), ext);
        grid.Columns.Add(combos);
        _envelopeColumns.Add(ext);
        _envelopeColumns.Add(combos);
    }

    void UpdateEnvelopeColumns()
    {
        var visibility = _vm.IsEnvelopeShown ? Visibility.Visible : Visibility.Collapsed;
        foreach (var column in _envelopeColumns) column.Visibility = visibility;
    }

    void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FemCsfeaResultVM.IsEnvelopeShown)) UpdateEnvelopeColumns();
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

    void BatchGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Двойной щелчок по заголовку столбца (сортировка) — не по строке.
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(batchGrid, source) is DataGridRow
            && batchGrid.SelectedItem is FemCsfeaBatchRow row)
            _vm.ShowBatchRow(row);
    }

    void BatchShowVariant_Click(object sender, RoutedEventArgs e)
    {
        if (batchGrid.SelectedItem is FemCsfeaBatchRow row) _vm.ShowBatchRow(row);
    }

    void BatchNewFromProblems_Click(object sender, RoutedEventArgs e) =>
        (Application.Current?.MainWindow?.DataContext as AppViewModel)?.NewFemBatchFromProblems(_vm);
}
