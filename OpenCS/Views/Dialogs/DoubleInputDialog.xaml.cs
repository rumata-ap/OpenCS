using System.Globalization;
using System.Windows;
using System.Windows.Controls;

using OpenCS.Utilites;

namespace OpenCS.Views.Dialogs;

/// <summary>
/// Ввод одного или двух чисел (запятая или точка). Без второй подписи второе поле скрыто. Неверный ввод и отказ
/// <see cref="Validate"/> показываются текстом под полем; окно при этом не закрывается.
/// </summary>
public partial class DoubleInputDialog : Window
{
    public double Value1 { get; private set; }
    public double Value2 { get; private set; }

    /// <summary>
    /// Проверка разобранных значений: (номер поля 1 или 2, текст ошибки) — окно не закрывается; <c>null</c> — годится.
    /// В окне с одним полем второе значение — 0.
    /// </summary>
    public Func<double, double, (int Field, string Error)?>? Validate { get; init; }

    bool TwoFields => Label2.Visibility == Visibility.Visible;

    public DoubleInputDialog(string title, string label1, string? label2,
        double default1 = 0, double default2 = 0)
    {
        InitializeComponent();
        Owner = DialogOwner.Active();
        Title = title;
        Label1.Text = label1;
        Value1Box.Text = Format(default1);
        if (label2 == null)
        {
            Label2.Visibility = Visibility.Collapsed;
            Value2Box.Visibility = Visibility.Collapsed;
        }
        else
        {
            Label2.Text = label2;
            Value2Box.Text = Format(default2);
        }
        Value1Box.TextChanged += (_, _) => Hide(Error1);
        Value2Box.TextChanged += (_, _) => Hide(Error2);
        Loaded += (_, _) => { Value1Box.Focus(); Value1Box.SelectAll(); };
    }

    /// <summary>Окно с одним числом.</summary>
    public DoubleInputDialog(string title, string label, double defaultValue = 0)
        : this(title, label, null, defaultValue) { }

    static string Format(double v) => v.ToString("G", CultureInfo.CurrentCulture);

    static void Hide(TextBlock error) => error.Visibility = Visibility.Collapsed;

    /// <summary>Разбирает и проверяет поля; при ошибке выводит её под полем и возвращает false.</summary>
    public bool TryAccept()
    {
        Hide(Error1);
        Hide(Error2);
        if (!Parse(Value1Box, out double v1)) return Fail(1, Loc.S("NumberInputInvalid"));
        double v2 = 0;
        if (TwoFields && !Parse(Value2Box, out v2)) return Fail(2, Loc.S("NumberInputInvalid"));
        if (Validate?.Invoke(v1, v2) is { } bad) return Fail(TwoFields ? bad.Field : 1, bad.Error);
        Value1 = v1;
        Value2 = v2;
        return true;
    }

    static bool Parse(TextBox box, out double value) =>
        Pars.ParseAny(box.Text.Trim(), out value) && double.IsFinite(value);

    bool Fail(int field, string error)
    {
        var (text, box) = field == 2 ? (Error2, Value2Box) : (Error1, Value1Box);
        text.Text = error;
        text.Visibility = Visibility.Visible;
        box.Focus();
        box.SelectAll();
        return false;
    }

    /// <summary>Текст ошибки под полем (для проверок); пусто — ошибки нет.</summary>
    public string ErrorText(int field)
    {
        var text = field == 2 ? Error2 : Error1;
        return text.Visibility == Visibility.Visible ? text.Text : "";
    }

    /// <summary>Поля ввода — для проверок.</summary>
    public TextBox Box(int field) => field == 2 ? Value2Box : Value1Box;

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (TryAccept()) DialogResult = true;
    }
}
