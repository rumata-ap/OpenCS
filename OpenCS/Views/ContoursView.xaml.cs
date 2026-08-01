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

using OpenCS.ViewModels;

namespace OpenCS.Views
{
   /// <summary>
   /// Логика взаимодействия для ContoursView.xaml
   /// </summary>
   public partial class ContoursView : UserControl, OpenCS.ViewModels.IContentPage
   {
      public ContoursView(AppViewModel mvm)
      {
         InitializeComponent();
         DataContext = mvm;
      }
   }
}
