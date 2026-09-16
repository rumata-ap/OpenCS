using CScore;
using OpenCS.Services;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Модальное окно подготовки бетонного материала Abaqus CDP.</summary>
public partial class AbaqusCdpExportWindow : System.Windows.Window
{
    /// <summary>Создаёт окно экспорта для указанного бетона.</summary>
    public AbaqusCdpExportWindow(Material material)
    {
        InitializeComponent();
        DataContext = new AbaqusCdpExportVM(material, new WpfTextClipboardService());
    }

    void Close_Click(object sender, System.Windows.RoutedEventArgs e) => Close();
}
