using OpenCS.Utilites;

using System.Windows;

namespace OpenCS.Views.Dialogs;

public partial class DoubleInputDialog : Window
{
    public double Value1 { get; private set; }
    public double Value2 { get; private set; }

    public DoubleInputDialog(string title, string label1, string label2,
        double default1 = 0, double default2 = 0)
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        Title = title;
        Label1.Text = label1;
        Label2.Text = label2;
        Value1Box.Text = default1.ToString("F3");
        Value2Box.Text = default2.ToString("F3");
        Value1Box.Focus();
        Value1Box.SelectAll();
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!Pars.ParseAny(Value1Box.Text, out var v1))
        {
            MessageBox.Show("Введите числовое значение.", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            Value1Box.Focus();
            Value1Box.SelectAll();
            return;
        }
        if (!Pars.ParseAny(Value2Box.Text, out var v2))
        {
            MessageBox.Show("Введите числовое значение.", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            Value2Box.Focus();
            Value2Box.SelectAll();
            return;
        }
        Value1 = v1;
        Value2 = v2;
        DialogResult = true;
    }
}
