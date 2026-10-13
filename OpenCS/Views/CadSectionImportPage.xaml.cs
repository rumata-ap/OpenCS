using System.Windows.Controls;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Страница импорта множества сечений из DXF / AutoCAD / nanoCAD (<see cref="CadSectionImportVM"/>).</summary>
public partial class CadSectionImportPage : UserControl
{
   public CadSectionImportPage(AppViewModel mvm)
   {
      InitializeComponent();
      DataContext = new CadSectionImportVM(mvm);
   }
}
