using System.Windows;

namespace OpenCS.Views.Dialogs;

public partial class TemplateTeeDialog : Window
{
    public string ContourName => NameBox.Text.Trim();
    public double WidthMm { get; private set; }
    public double HeightMm { get; private set; }
    public double TwMm { get; private set; }
    public double TfMm { get; private set; }

    public TemplateTeeDialog()
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(WidthBox.Text, out var w) || w <= 0 ||
            !double.TryParse(HeightBox.Text, out var h) || h <= 0 ||
            !double.TryParse(TwBox.Text, out var tw) || tw <= 0 ||
            !double.TryParse(TfBox.Text, out var tf) || tf <= 0)
        {
            MessageBox.Show("Введите положительные числовые значения.", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        WidthMm = w; HeightMm = h; TwMm = tw; TfMm = tf;
        DialogResult = true;
    }
}
