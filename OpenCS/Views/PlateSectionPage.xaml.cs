using CScore;
using OpenCS.ViewModels;

using System.Windows.Controls;

namespace OpenCS.Views
{
   /// <summary>Страница сечения пластины; логика — <see cref="PlateSectionVM"/>.</summary>
   public partial class PlateSectionPage : UserControl
   {
      public PlateSectionPage(AppViewModel app) : this(new PlateSection(), app) { }

      public PlateSectionPage(PlateSection ps, AppViewModel app)
      {
         InitializeComponent();
         DataContext = new PlateSectionVM(ps, app);
      }
   }
}
