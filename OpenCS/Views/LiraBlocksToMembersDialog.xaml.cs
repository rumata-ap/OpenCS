using System.Windows;
using OpenCS.ViewModels;

namespace OpenCS.Views
{
   /// <summary>Выбор кБ ЛИРЫ для преобразования в конструктивные элементы схемы.</summary>
   public partial class LiraBlocksToMembersDialog : Window
   {
      readonly LiraBlocksToMembersVM _vm;

      public LiraBlocksToMembersDialog(LiraBlocksToMembersVM vm)
      {
         InitializeComponent();
         Owner = Application.Current.MainWindow;
         DataContext = _vm = vm;
      }

      void Convert_Click(object sender, RoutedEventArgs e)
      {
         if (_vm.SelectedCount == 0) return;
         DialogResult = true;
      }
   }
}
