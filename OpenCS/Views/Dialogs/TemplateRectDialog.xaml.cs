using System.Windows;

namespace OpenCS.Views.Dialogs;

public partial class TemplateRectDialog : Window
{
    public string ContourName => NameBox.Text.Trim();
    public double WidthMm { get; private set; }
    public double HeightMm { get; private set; }

    public TemplateRectDialog()
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(WidthBox.Text, out var w) || w <= 0 ||
            !double.TryParse(HeightBox.Text, out var h) || h <= 0)
        {
            MessageBox.Show("Введите положительные числовые значения.", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        WidthMm = w;
        HeightMm = h;
        DialogResult = true;
    }
}
