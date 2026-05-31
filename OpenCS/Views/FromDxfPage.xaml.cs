using OpenCS.ViewModels;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace OpenCS.Views
{
   /// <summary>
   /// Логика взаимодействия для FromDxfPage.xaml
   /// </summary>
   public partial class FromDxfPage : UserControl
   {
      public FromDxfPage(AppViewModel mvm)
      {
         InitializeComponent();
         var vm = new FromDxfVM
         {
            mvm = mvm,
            ContoursListBox = ContoursDxfListBox,
            CirclesListBox = CirclesDxfListBox,
         };
         DataContext = vm;
      }
   }
}
