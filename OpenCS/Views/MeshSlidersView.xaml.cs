using System.Windows.Controls;

namespace OpenCS.Views
{
   /// <summary>
   /// Представление слайдеров для управления параметрами разбиения сечения.
   /// Содержит 4 Slider-а: Dx, Dy, Atr, Antr.
   /// DataContext привязывается к RCFiberRegionVM.
   /// </summary>
   public partial class MeshSlidersView : UserControl
   {
      public MeshSlidersView()
      {
         InitializeComponent();
      }
   }
}