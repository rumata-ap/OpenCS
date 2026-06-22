using System.Windows;

namespace OpenCS.Views.Dialogs;

public partial class CircleDialog : Window
{
    public double X { get; private set; }
    public double Y { get; private set; }
    public double Radius { get; private set; }

    public CircleDialog()
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(XBox.Text, out var x))
        {
            MessageBox.Show("Введите числовое значение X.", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!double.TryParse(YBox.Text, out var y))
        {
            MessageBox.Show("Введите числовое значение Y.", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!double.TryParse(RadiusBox.Text, out var r) || r <= 0)
        {
            MessageBox.Show("Введите положительное числовое значение радиуса.", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        X = x;
        Y = y;
        Radius = r;
        DialogResult = true;
    }
}
