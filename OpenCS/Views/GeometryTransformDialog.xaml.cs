using System.Windows;
using OpenCS.Utilites;

namespace OpenCS.Views;

public enum GeometryTransformKind { Move, Scale, Rotate }

public partial class GeometryTransformDialog : Window
{
    public double Dx { get; private set; }
    public double Dy { get; private set; }
    public double Factor { get; private set; } = 1;
    public double AngleDeg { get; private set; }

    public GeometryTransformDialog(GeometryTransformKind kind, string titleResourceKey)
    {
        InitializeComponent();
        Title = (string)FindResource(titleResourceKey);

        movePanel.Visibility = kind == GeometryTransformKind.Move ? Visibility.Visible : Visibility.Collapsed;
        scalePanel.Visibility = kind == GeometryTransformKind.Scale ? Visibility.Visible : Visibility.Collapsed;
        rotatePanel.Visibility = kind == GeometryTransformKind.Rotate ? Visibility.Visible : Visibility.Collapsed;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        double Parse(System.Windows.Controls.TextBox box) => Pars.ParseAny(box.Text, out var v) ? v : 0;
        Dx = Parse(dxBox);
        Dy = Parse(dyBox);
        Factor = Parse(factorBox);
        AngleDeg = Parse(angleBox);
        DialogResult = true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
