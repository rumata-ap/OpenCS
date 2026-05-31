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
      public FromDataSourceWindow(MaterialVM material)
      {
         InitializeComponent();
         DataSourceVM vm = new() { Material = material };
         DataContext = vm;
         vm.Select();
      }
   }
}
