using System.Windows;

namespace OpenCS.Views;

public partial class FemNodeOffsetDialog : Window
{
    readonly Action<double, double, double> _onApply;

    public FemNodeOffsetDialog(bool isCopy, Action<double, double, double> onApply)
    {
        InitializeComponent();
        Title = (string)(isCopy ? Application.Current.FindResource("FemNodeOffsetCopyTitle")
                                : Application.Current.FindResource("FemNodeOffsetMoveTitle"));
        _onApply = onApply;
    }

    void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(dxBox.Text, out var dx)) return;
        if (!double.TryParse(dyBox.Text, out var dy)) return;
        if (!double.TryParse(dzBox.Text, out var dz)) return;
        _onApply(dx, dy, dz);
        Close();
    }
}
