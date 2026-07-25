using System.Windows;
using OpenCS.Utilites;

namespace OpenCS.Views;

public partial class FemFragmentOffsetDialog : Window
{
    public double Dx { get; private set; }
    public double Dy { get; private set; }
    public double Dz { get; private set; }

    public FemFragmentOffsetDialog() => InitializeComponent();

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        double Parse(System.Windows.Controls.TextBox box) => Pars.ParseAny(box.Text, out var v) ? v : 0;
        Dx = Parse(dxBox); Dy = Parse(dyBox); Dz = Parse(dzBox);
        DialogResult = true;
    }
}
