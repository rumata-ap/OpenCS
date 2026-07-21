using System.Globalization;
using System.Windows;

namespace OpenCS.Views;

/// <summary>Диалог задания β-угла поворота поперечного сечения стержня.</summary>
public partial class FemMemberRotationDialog : Window
{
    readonly Action<double> _onApply;

    public FemMemberRotationDialog(double rotationDeg, Action<double> onApply)
    {
        InitializeComponent();
        angleBox.Text = rotationDeg.ToString("G", CultureInfo.CurrentCulture);
        _onApply = onApply;
    }

    void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(angleBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) ||
            !double.IsFinite(value))
        {
            MessageBox.Show(
                (string)Application.Current.FindResource("FemMemberRotationInvalid"),
                (string)Application.Current.FindResource("FemMemberRotationTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _onApply(value);
        Close();
    }
}
