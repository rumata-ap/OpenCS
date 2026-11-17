using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CScore.Fem;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Окно параметров проверки продавливания по МКЭ-схеме (<c>punching_check</c>).</summary>
public partial class PunchingFemCheckDialog : Window
{
    readonly PunchingFemCheckVM _vm;

    /// <param name="existing">Изменяемая проверка; null — новая.</param>
    /// <param name="schema">Схема новой проверки; null — первая в проекте.</param>
    public PunchingFemCheckDialog(DatabaseService db, FemCheck? existing, FemSchema? schema = null)
    {
        InitializeComponent();
        Owner = Application.Current?.MainWindow is { IsLoaded: true } w && w != this ? w : null;
        Cursor = Cursors.Wait;
        try { _vm = new PunchingFemCheckVM(db, existing, schema); }
        finally { Cursor = null; }
        DataContext = _vm;
        ResultCheck = existing;
    }

    /// <summary>Загрузка *.asp ЛИРЫ к схеме (как в меню схемы) для кнопки «Приложить *.asp…»; true — приложен.</summary>
    public Func<FemSchema, bool>? AttachLiraAsp { get => _vm.AttachLiraAsp; init => _vm.AttachLiraAsp = value; }

    /// <summary>Проверка после «Сохранить» (новая — созданная здесь, без Id).</summary>
    public FemCheck? ResultCheck { get; private set; }
    /// <summary>Пользователь выбрал «Сохранить и рассчитать».</summary>
    public bool RunRequested { get; private set; }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!Apply()) return;
        DialogResult = true;
    }

    void SaveAndRun_Click(object sender, RoutedEventArgs e)
    {
        if (!Apply()) return;
        RunRequested = true;
        DialogResult = true;
    }

    bool Apply()
    {
        if (FindInvalid(this) is { } bad)
        {
            MessageBox.Show(this, Loc.S("PunchingInvalidNumber", "Поле содержит нераспознанное число. Исправьте значение, выделенное красной рамкой."),
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            bad.Focus();
            return false;
        }
        var errors = _vm.Validate();
        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join("\n", errors), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        var check = ResultCheck ?? new FemCheck();
        _vm.ApplyTo(check);
        ResultCheck = check;
        return true;
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

    void NumBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox tb) tb.SelectAll();
    }

    /// <summary>Первый щелчок по неактивному числовому полю — фокус и выделение всего текста.</summary>
    void NumBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox { IsKeyboardFocusWithin: false } tb)
        {
            e.Handled = true;
            tb.Focus();
        }
    }
}
