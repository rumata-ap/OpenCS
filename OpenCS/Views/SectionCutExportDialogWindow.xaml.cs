using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class SectionCutExportDialogWindow : System.Windows.Window
{
    public SectionCutExportOptions? Result { get; private set; }

    /// <summary>Создаёт диалог; <paramref name="initial"/> — прежний выбор (null — PNG, «как на экране»).</summary>
    public SectionCutExportDialogWindow(SectionCutExportOptions? initial = null)
    {
        InitializeComponent();
        if (initial == null) return;
        FormatPng.IsChecked = initial.Format == SectionCutExportFormat.Png;
        FormatSvg.IsChecked = initial.Format == SectionCutExportFormat.Svg;
        FormatDxf.IsChecked = initial.Format == SectionCutExportFormat.Dxf;
        AsOnScreenCheck.IsChecked = initial.AsOnScreen;
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
