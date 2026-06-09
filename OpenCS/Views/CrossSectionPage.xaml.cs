using CScore;
using System.Windows.Controls;

namespace OpenCS.Views
{
   public partial class CrossSectionPage : UserControl
   {
      public CrossSectionPage(AppViewModel app)
      {
         InitializeComponent();
      }

      public CrossSectionPage(CrossSection section, AppViewModel app)
      {
         InitializeComponent();
      }
   }
}
