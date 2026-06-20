using CScore;
using CScore.Fire.Entities;

using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using OpenCS.Views;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
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

      readonly ObservableCollection<CalcTaskVM> _taskTreeItems = [];

      public MainWindow()
      {
         InitializeComponent();

         var logService = new LogService();
         var fileDialogService = new WpfFileDialogService();

         DataContext = new AppViewModel(logService, fileDialogService);
         vm = (AppViewModel)DataContext;

         vm.LogService.LogEntries.CollectionChanged += (_, _) =>
            Dispatcher.BeginInvoke(() =>
            {
               if (LoggerListBox.Items.Count > 0)
                  LoggerListBox.ScrollIntoView(LoggerListBox.Items[^1]);
            });

         Loaded += (_, _) =>
         {
            tasksNode.ItemsSource = _taskTreeItems;
            RebuildTaskTree();
            SubscribeToTaskCollections();
            vm.CalcTaskModified += RebuildTaskTree;
         };
         vm.PropertyChanged += (_, e2) =>
         {
            if (e2.PropertyName == nameof(AppViewModel.CalcTasks))
            {
               SubscribeToTaskCollections();
               RebuildTaskTree();
            }
         };
      }

      void SubscribeToTaskCollections()
      {
         vm.CalcTasks.CollectionChanged   += (_, _) => RebuildTaskTree();
         vm.CalcResults.CollectionChanged += (_, _) => RefreshResultsInTree();
      }

      void RebuildTaskTree()
      {
         _taskTreeItems.Clear();
         foreach (var ct in vm.CalcTasks)
         {
            var tvm = new CalcTaskVM(ct);
            var sec = vm.CrossSections.FirstOrDefault(s => s.Id == ct.SectionId);
            var fs  = vm.BarForceSets.FirstOrDefault(f => f.Id == ct.ForceSetId);
            var fi  = fs?.Items.FirstOrDefault(i => i.Id == ct.ForceItemId);
            tvm.SectionTag     = sec?.Tag  ?? "";
            tvm.ForceSetTag    = fs?.Tag   ?? "";
            tvm.ForceItemLabel = fi?.Label ?? "";
            foreach (var r in vm.CalcResults.Where(r => r.TaskId == ct.Id))
               tvm.Results.Add(r);
            _taskTreeItems.Add(tvm);
         }
      }

      void RefreshResultsInTree()
      {
         foreach (var tvm in _taskTreeItems)
         {
            tvm.Results.Clear();
            foreach (var r in vm.CalcResults.Where(r => r.TaskId == tvm.Model.Id))
               tvm.Results.Add(r);
         }
      }

      private void ContourDel_Click(object sender, RoutedEventArgs e)
      {
         vm.DelContourCommand.Execute(null);
      }

      private void ConcreteAdd_Click(object sender, RoutedEventArgs e)
      {
         vm.NewMaterialFromSource(0);
      }

      private void RfsteelAdd_Click(object sender, RoutedEventArgs e)
      {
         vm.NewMaterialFromSource(1);
      }

      private void SteelAdd_Click(object sender, RoutedEventArgs e)
      {
         vm.NewMaterialFromSource(2);
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

          if (e.NewValue is FireSectionDef fireSectionItem)
          {
             vm.CurrentFireSection = fireSectionItem;
          }

          if(e.NewValue is TreeViewItem treeViewItem)
          {
             if (treeViewItem.Name == "ContoursTreeItem")
             {
                vm.CurrentPage = new ContoursView(vm);
             }
             if (treeViewItem.Name == "CirclesTreeItem")
             {
                vm.CurrentPage = new CirclesView(vm);
             }
             if (treeViewItem.Name == "tasksNode")
             {
                vm.CurrentPage = new CalcTasksPage(vm);
             }
             if (treeViewItem.Name == "fireNode")
             {
                vm.CurrentPage = null!;
             }
          }

          if (e.NewValue is CalcTaskVM calcTaskVmItem)
          {
             vm.CurrentPage = new CalcTasksPage(vm, calcTaskVmItem.Model);
          }

          if (e.NewValue is CalcResult calcResultItem)
          {
             vm.CurrentPage = new CalcResultView(calcResultItem, vm);
          }
        }

      void TasksNode_Selected(object sender, RoutedEventArgs e)
      {
         vm.CurrentPage = new CalcTasksPage(vm);
         e.Handled = true;
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