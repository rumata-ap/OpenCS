using CScore;

using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using OpenCS.Views;

using System.ComponentModel;
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

          if (e.NewValue is CrossSection csItem)
          {
             vm.CurrentCrossSection = csItem;
          }

          if (e.NewValue is MaterialArea areaItem)
          {
             // Если область принадлежит сечению — открываем редактор сечения
             CrossSection? owner = vm.CrossSections.FirstOrDefault(s =>
                s.Areas.Contains(areaItem) ||
                (s is CScore.TwoStageSection tss && tss.Stage1.Areas.Contains(areaItem)));
             if (owner != null)
                vm.CurrentCrossSection = owner;
             else
                vm.CurrentMaterialArea = areaItem;
          }

          if (e.NewValue is CScore.ForceSet forceSetItem)
          {
             if (forceSetItem.Kind == "shell")
                vm.CurrentShellForceSet = forceSetItem;
             else
                vm.CurrentBarForceSet = forceSetItem;
          }

          if (e.NewValue is CScore.PlateSection plateSectionItem)
          {
             vm.CurrentPlateSection = plateSectionItem;
          }

          if(e.NewValue is TreeViewItem treeViewItem)
          {
             if (treeViewItem.Name == "ContoursTreeItem")
             {
                vm.CurrentPage = new ContoursView(vm);
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
         var res = MessageBox.Show(Loc.S("ConfirmDeleteDiagram"), Loc.S("Confirmation"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
         if (res != MessageBoxResult.Yes) return;
         vm.db.DeleteDiagram(ctx);
         vm.Diagrams.Remove(ctx);
         vm.DiagramsLive.Remove(ctx);
         vm.LogService.Info(string.Format(Loc.S("DiagramDeleted"), ctx.Tag));
      }

      void DeleteAllDiagrams_Click(object sender, RoutedEventArgs e)
      {
          var res = MessageBox.Show(Loc.S("ConfirmDeleteAllDiagrams"), Loc.S("Confirmation"),
             MessageBoxButton.YesNo, MessageBoxImage.Warning);
          if (res != MessageBoxResult.Yes) return;

          foreach (var d in vm.Diagrams.ToList())
          {
             vm.db.DeleteDiagram(d);
             vm.Diagrams.Remove(d);
          }
          vm.DiagramsLive.Clear();
          vm.LogService.Info(Loc.S("AllDiagramsDeleted"));
      }

      void Window_Closing(object sender, CancelEventArgs e)
      {
         if (!vm.ConfirmSaveIfNeeded())
            e.Cancel = true;
         else
            vm.db.Dispose();
      }

      void CopyLogEntry_Click(object sender, RoutedEventArgs e)
      {
         if (LoggerListBox.SelectedItem is LogEntry entry)
            Clipboard.SetText(entry.FormattedMessage);
      }
   }
}