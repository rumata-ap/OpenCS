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
   /// Логика взаимодействия для RebarsPage.xaml
   /// </summary>
   public partial class RebarsPage : UserControl
   {
      public RebarsPage(AppViewModel mvm)
      {
         InitializeComponent();
         RebarsVM vm = new()
         {
            RebarsListBox = RebarsListBox,
            CirclesListBox = CirclesListBox,
         };
         DataContext = vm;
         vm.MVM = mvm;
      }
   }
}
