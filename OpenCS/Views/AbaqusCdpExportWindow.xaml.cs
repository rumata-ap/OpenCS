using CScore;
using OpenCS.Services;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Модальное окно подготовки материала Abaqus: CDP для бетона, *Plastic для стали и арматуры.</summary>
public partial class AbaqusCdpExportWindow : System.Windows.Window
{
    /// <summary>Создаёт окно экспорта для указанного бетона, стали или арматуры.</summary>
    public AbaqusCdpExportWindow(Material material)
    {
        InitializeComponent();
        var vm = new AbaqusCdpExportVM(material, new WpfTextClipboardService());
        if (vm.IsSteel)
            SetResourceReference(TitleProperty, "AbaqusSteelExportTitle");
        DataContext = vm;
    }

    void Close_Click(object sender, System.Windows.RoutedEventArgs e) => Close();
}
