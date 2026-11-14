using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CScore;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Окно результата пакета «Продавливание плиты по набору усилий (СП 63)».</summary>
public partial class PunchingBatchResultView : UserControl
{
    readonly AppViewModel? _app;
    readonly CalcResult _result;
    readonly CalcTask? _task;
    readonly PunchingBatchResultVM _vm;

    /// <summary>Создаёт окно по сохранённому результату пакета.</summary>
    public PunchingBatchResultView(CalcResult result, AppViewModel app, CalcTask? task)
    {
        InitializeComponent();
        _app = app;
        _result = result;
        _task = task;
        _vm = new PunchingBatchResultVM(result, task);
        DataContext = _vm;
        AddVariantColumns();
        ParamsButton.IsEnabled = task is not null;
        ReportButton.IsEnabled = app.ExportCalcResultReportCommand.CanExecute(result);
        CreateTaskBtn.IsEnabled = task is not null && _vm.SelectedRow is not null;
        RowsGrid.SelectionChanged += (_, _) => CreateTaskBtn.IsEnabled = _task is not null && RowsGrid.SelectedItem is not null;
    }

    /// <summary>
    /// Окно по готовой VM без приложения — проверка разметки в тестах. resources — ресурсы, которые иначе
    /// даёт App.xaml (стиль строк пакета, конвертер видимости); кнопки задачи, параметров и отчёта выключены.
    /// </summary>
    public PunchingBatchResultView(PunchingBatchResultVM vm, ResourceDictionary resources)
    {
        Resources = resources;
        InitializeComponent();
        _result = vm.Result;
        _vm = vm;
        DataContext = vm;
        AddVariantColumns();
        ParamsButton.IsEnabled = ReportButton.IsEnabled = CreateTaskBtn.IsEnabled = false;
    }

    /// <summary>Число столбцов таблицы строк (проверка разметки).</summary>
    public int RowColumnCount => RowsGrid.Columns.Count;

    /// <summary>Столбцы «Кисп: ‹контур›» — по вариантам контура этого узла, перед столбцом определяющего контура.</summary>
    void AddVariantColumns()
    {
        int at = RowsGrid.Columns.IndexOf(GoverningColumn);
        foreach (var c in _vm.VariantColumns)
        {
            var column = new DataGridTextColumn
            {
                Header = new TextBlock { Text = Utilites.Loc.S("PunchingKisp", "Кисп") + ": " + c.Header, ToolTip = c.Title, TextWrapping = TextWrapping.Wrap, MaxWidth = 110 },
                Binding = new Binding($"Ratios[{c.Index}]"),
                SortMemberPath = $"RatioValues[{c.Index}]",
                Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
                MinWidth = 60
            };
            RowsGrid.Columns.Insert(at++, column);
        }
    }

    void Report_Click(object sender, RoutedEventArgs e)
    {
        if (_app is not null && _app.ExportCalcResultReportCommand.CanExecute(_result))
            _app.ExportCalcResultReportCommand.Execute(_result);
    }

    void Params_Click(object sender, RoutedEventArgs e)
    {
        if (_app is not null && _task is not null) PunchingParamsDialog.Edit(_app, _task, Window.GetWindow(this));
    }

    void RowsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => CreateTask();

    void CreateTask_Click(object sender, RoutedEventArgs e) => CreateTask();

    /// <summary>Одиночная задача «Продавливание плиты» по выбранной строке: создаётся, считается и открывается.</summary>
    void CreateTask()
    {
        if (_app is not null && _task is not null && RowsGrid.SelectedItem is PunchingBatchRowVM row)
            BatchRowTask.CreateAndRun(_app, _task, row.Num);
    }
}
