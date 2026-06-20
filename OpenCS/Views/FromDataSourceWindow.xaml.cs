using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using OpenCS.ViewModels;
using OpenCS.Utilites;
using CScore;

namespace OpenCS.Views
{
   /// <summary>
   /// Логика взаимодействия для FromDataSourceWindow.xaml
   /// </summary>
   public partial class FromDataSourceWindow : Window
   {    
      public FromDataSourceWindow(MaterialVM material, int tabIndex = 0)
      {
         InitializeComponent();
         DataSourceVM vm = new() { Material = material };
         DataContext = vm;
         switch (tabIndex)
         {
            case 1: vm.RfsteelTabIsSelected = true; break;
            case 2: vm.SteelTabIsSelected = true; break;
         }
         vm.Select();
      }
   }
}
