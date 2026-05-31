using CScore;

using System.Windows.Controls;

namespace OpenCS.Views
{
   /// <summary>
   /// Логика взаимодействия для RegionPlot.xaml
   /// </summary>
   public partial class RegionPlot : UserControl
   {
      public RCFiberRegion Region { get; set; }
      public RegionPlot( Region region )
      {
         InitializeComponent();
         Region = (RCFiberRegion)region;
      }
   }
}
