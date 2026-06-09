using CScore;
using OpenCS.ViewModels;
using System.Windows.Controls;

namespace OpenCS.Views
{
   public partial class CrossSectionPage : UserControl
   {
      public CrossSectionPage(AppViewModel app)
      {
         InitializeComponent();
         var section = new CrossSection { Tag = "Новое сечение" };
         app.CrossSections.Add(section);
         DataContext = new CrossSectionVM(section, app);
      }

      public CrossSectionPage(CrossSection section, AppViewModel app)
      {
         InitializeComponent();
         DataContext = new CrossSectionVM(section, app);
      }
   }
}
