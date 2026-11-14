using System.Windows;
using CScore;
using OpenCS.Tasks;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Окно параметров задачи «Продавливание плиты (СП 63)»: форма слева, живой рисунок и результат справа.</summary>
public partial class PunchingParamsDialog : Window
{
    readonly AppViewModel? _app;
    readonly CalcTask _task;
    readonly PunchingParamsVM _vm;

    /// <summary>Нажато «Сохранить и рассчитать».</summary>
    public bool RunRequested { get; private set; }

    /// <summary>Создаёт окно для задачи проекта.</summary>
    public PunchingParamsDialog(AppViewModel app, CalcTask task)
        : this(task, new PunchingParamsVM(task, app.Materials ?? [], app.PlateSections ?? [],
            app.BarForceSets?.FirstOrDefault(f => f.Id == task.ForceSetId)?.Items.FirstOrDefault(i => i.Id == task.ForceItemId)))
    {
        _app = app;
    }

    /// <summary>Создаёт окно по готовой VM без сохранения в базу (проверка разметки).</summary>
    public PunchingParamsDialog(CalcTask task, PunchingParamsVM vm)
    {
        InitializeComponent();
        _task = task;
        _vm = vm;
        DataContext = vm;
        Title = $"{Title} — {task.Tag}";
    }

    void Save()
    {
        _task.ParamsJson = _vm.ToJson();
        _app?.db.SaveCalcTask(_task);
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        Save();
        DialogResult = true;
    }

    void SaveAndRun_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.HasErrors) return;
        Save();
        RunRequested = true;
        DialogResult = true;
    }

    /// <summary>
    /// Открывает окно параметров; при «Сохранить и рассчитать» выполняет задачу и показывает результат.
    /// Возвращает true, если параметры сохранены.
    /// </summary>
    public static bool Edit(AppViewModel app, CalcTask task, Window? owner)
    {
        var dlg = new PunchingParamsDialog(app, task) { Owner = owner ?? Application.Current?.MainWindow };
        if (dlg.ShowDialog() != true) return false;
        if (dlg.RunRequested)
            _ = CalcTaskExecutor.RunAsync(app, task, showResult: r => app.CurrentPage = new CalcResultView(r, app));
        return true;
    }
}
