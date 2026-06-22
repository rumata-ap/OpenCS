using System.Windows;

namespace OpenCS.Views.Dialogs;

public partial class TemplateCircleDialog : Window
{
    public string ContourName => NameBox.Text.Trim();
    public double DiameterMm { get; private set; }
    public int Segments { get; private set; }

    public TemplateCircleDialog()
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(DiameterBox.Text, out var d) || d <= 0)
        {
            MessageBox.Show("Введите положительное числовое значение диаметра.", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(SegmentsBox.Text, out var n) || n < 3)
        {
            MessageBox.Show("Число сегментов должно быть не менее 3.", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DiameterMm = d;
        Segments = n;
        DialogResult = true;
    }
}
