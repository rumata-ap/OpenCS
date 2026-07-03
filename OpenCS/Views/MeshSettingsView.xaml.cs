using System.Windows.Controls;

namespace OpenCS.Views
{
   /// <summary>
   /// Представление параметров разбиения сечения: ортогональное деление (nX, nY)
   /// и триангуляция (area, angle). DataContext привязывается к RCFiberRegionVM.
   /// </summary>
   public partial class MeshSettingsView : UserControl
   {
      public MeshSettingsView()
      {
         InitializeComponent();
      }
   }
}