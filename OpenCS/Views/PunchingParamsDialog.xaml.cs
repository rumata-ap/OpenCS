using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        if (!InputsValid()) return;
        Save();
        DialogResult = true;
    }

    void SaveAndRun_Click(object sender, RoutedEventArgs e)
    {
        if (!InputsValid() || _vm.HasErrors) return;
        Save();
        RunRequested = true;
        DialogResult = true;
    }

    /// <summary>
    /// Нет полей с нераспознанным числом (красная рамка). Иначе — сообщение и фокус на первое такое поле:
    /// в модели осталось прежнее значение, и молча сохранять его нельзя.
    /// </summary>
    bool InputsValid()
    {
        var bad = FindInvalid(this);
        if (bad is null) return true;
        MessageBox.Show(this, Utilites.Loc.S("PunchingInvalidNumber", "Поле содержит нераспознанное число. Исправьте значение, выделенное красной рамкой."),
            Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        bad.Focus();
        return false;
    }

    static TextBox? FindInvalid(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBox { IsVisible: true } tb && Validation.GetHasError(tb)) return tb;
            if (FindInvalid(child) is { } found) return found;
        }
        return null;
    }

    /// <summary>Числовое поле при входе с клавиатуры выделяет весь текст — новый ввод заменяет старое значение.</summary>
    void NumBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox tb) tb.SelectAll();
    }

    /// <summary>Первый щелчок по неактивному числовому полю — фокус и выделение всего текста, а не курсор в середине.</summary>
    void NumBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox { IsKeyboardFocusWithin: false } tb)
        {
            e.Handled = true;
            tb.Focus();
        }
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
