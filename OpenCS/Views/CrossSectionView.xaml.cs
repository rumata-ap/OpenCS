using CScore;
using OpenCS.ViewModels;
using System.Windows.Controls;

namespace OpenCS.Views
{
   public partial class CrossSectionView : UserControl
   {
      public CrossSectionView(CrossSection section, AppViewModel app)
      {
         InitializeComponent();
         DataContext = new CrossSectionVM(section, app);
      }
   }
}
