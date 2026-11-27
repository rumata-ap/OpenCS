using System.Windows;
using System.Windows.Controls;

namespace OpenCS.Views.Dialogs;

/// <summary>Ввод строки (имени). Пустое значение и отказ <see cref="Validate"/> — текстом под полем, окно не закрывается.</summary>
public partial class TextInputDialog : Window
{
    public string Value { get; private set; } = "";

    /// <summary>Проверка значения: текст ошибки — окно не закрывается, <c>null</c> — значение годится.</summary>
    public Func<string, string?>? Validate { get; init; }

    public TextInputDialog(string title, string label, string defaultValue = "")
    {
        InitializeComponent();
        Owner = DialogOwner.Active();
        Title = title;
        LabelText.Text = label;
        ValueBox.Text = defaultValue;
        ValueBox.TextChanged += (_, _) => ErrorBlock.Visibility = Visibility.Collapsed;
        Loaded += (_, _) => { ValueBox.Focus(); ValueBox.SelectAll(); };
    }

    /// <summary>Поле ввода — для проверок.</summary>
    public TextBox Box => ValueBox;

    /// <summary>Текст ошибки под полем; пусто — ошибки нет.</summary>
    public string ErrorText => ErrorBlock.Visibility == Visibility.Visible ? ErrorBlock.Text : "";

    /// <summary>Проверяет значение; при ошибке выводит её под полем и возвращает false.</summary>
    public bool TryAccept()
    {
        string trimmed = ValueBox.Text.Trim();
        string? error = trimmed.Length == 0 ? Utilites.Loc.S("TextInputEmpty") : Validate?.Invoke(trimmed);
        if (error != null)
        {
            ErrorBlock.Text = error;
            ErrorBlock.Visibility = Visibility.Visible;
            ValueBox.Focus();
            ValueBox.SelectAll();
            return false;
        }
        Value = trimmed;
        return true;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (TryAccept()) DialogResult = true;
    }
}
