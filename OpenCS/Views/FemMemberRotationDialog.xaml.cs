using System.Globalization;
using System.Windows;
using OpenCS.Utilites;

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
        Views.Helpers.FieldCheck.Clear(angleBox);
        if (!Views.Helpers.FieldCheck.Read(this, angleBox, "FemMemberRotationDeg", out var value)) return;
        _onApply(value);
        Close();
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
