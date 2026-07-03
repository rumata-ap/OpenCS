using CScore;

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
using System.Windows.Shapes;

namespace OpenCS.Views
{
   /// <summary>
   /// Логика взаимодействия для MaterialWindow.xaml
   /// </summary>
   public partial class MaterialWindow : Window
   {
      public MaterialWindow(Material material, AppViewModel mvm)
      {
         InitializeComponent();
         MaterialPage page = new(material, mvm);
         contentC.Content = page;
      }
   }
}
