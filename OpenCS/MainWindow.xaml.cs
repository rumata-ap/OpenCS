using CScore;

using OpenCS.Services;
using OpenCS.ViewModels;
using OpenCS.Views;

using System.Windows;
using System.Windows.Controls;

namespace OpenCS
{
   /// <summary>
   /// Interaction logic for MainWindow.xaml
   /// </summary>
   public partial class MainWindow : Window
   {
      public AppViewModel vm;

      public MainWindow()
      {
         InitializeComponent();

         var logService = new LogService();
         var fileDialogService = new WpfFileDialogService();

         DataContext = new AppViewModel(logService, fileDialogService);
         vm = (AppViewModel)DataContext;
      }

      private void ContourDel_Click(object sender, RoutedEventArgs e)
      {
         vm.DelContourCommand.Execute(null);
      }

      private void ConcreteAdd_Click(object sender, RoutedEventArgs e)
      {
         vm.NewMaterialCommand.Execute(null);
      }

      private void ConcreteDel_Click(object sender, RoutedEventArgs e)
      {
         vm.DelMaterialCommand.Execute(null);
      }

      private void structureTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
      {
         if (e.NewValue is Material materialItem)
         {
            vm.CurrentMaterial = materialItem;
         }
         if (e.NewValue is ContourVM contourItem)
         {
            vm.CurrentContour = contourItem;
         }
         if (e.NewValue is MaterialChars chars)
         {
            var parent = vm.Materials.FirstOrDefault(m => m.MaterialChars.Contains(chars));
            if (parent != null)
            {
               var charVM = new MaterialCharsVM(chars, parent.Tag);
               vm.CurrentPage = new MaterialCharsPage(charVM, parent, vm);
            }
         }
          if (e.NewValue is Diagramm diagramItem)
          {
             vm.CurrentDiagram = diagramItem;
          }

          if(e.NewValue is TreeViewItem treeViewItem)
          {
             if (treeViewItem.Name == "ContoursTreeItem")
             {
                vm.CurrentPage = new ContoursView(vm);
             }
             if(treeViewItem.DataContext is RCFiberRegion region)
             {
                if(vm.treeItem!= null && vm.treeItem.IsSelected)
                   vm.treeItem.IsSelected = false;
                vm.CurrentRCfiberRegion = region;
                vm.treeItem = treeViewItem;
             }

          }

        }

      void DeleteDiagram_Click(object sender, RoutedEventArgs e)
      {
         var item = sender as MenuItem;
         if (item == null) return;
         var target = (item.Parent as ContextMenu)?.PlacementTarget as FrameworkElement;
         var ctx = item.CommandParameter as Diagramm ?? target?.DataContext as Diagramm;
         if (ctx == null) return;
         var res = MessageBox.Show("Удалить диаграмму?", "Подтверждение",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
         if (res != MessageBoxResult.Yes) return;
         vm.db.DeleteDiagram(ctx);
         vm.Diagrams.Remove(ctx);
         vm.DiagramsLive.Remove(ctx);
         vm.LogService.Info($"Диаграмма '{ctx.Tag}' удалена");
      }

      void DeleteAllDiagrams_Click(object sender, RoutedEventArgs e)
      {
         var res = MessageBox.Show("Удалить все диаграммы?", "Подтверждение",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
         if (res != MessageBoxResult.Yes) return;

         foreach (var d in vm.Diagrams.ToList())
         {
            vm.db.DeleteDiagram(d);
            vm.Diagrams.Remove(d);
         }
         vm.DiagramsLive.Clear();
         vm.LogService.Info("Все диаграммы удалены");
      }
   }
}