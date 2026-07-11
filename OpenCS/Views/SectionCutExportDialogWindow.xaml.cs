using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class SectionCutExportDialogWindow : System.Windows.Window
{
    public SectionCutExportOptions? Result { get; private set; }

    public SectionCutExportDialogWindow()
    {
        InitializeComponent();
    }

    void OnOk(object sender, System.Windows.RoutedEventArgs e)
    {
        var format = FormatSvg.IsChecked == true ? SectionCutExportFormat.Svg
            : FormatDxf.IsChecked == true ? SectionCutExportFormat.Dxf
            : SectionCutExportFormat.Png;
        Result = new SectionCutExportOptions(format, AsOnScreenCheck.IsChecked == true);
        DialogResult = true;
        Close();
    }
}
