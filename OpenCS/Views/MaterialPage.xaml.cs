using CScore;
using OpenCS.Utilites;
using OpenCS.ViewModels;

using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OpenCS.Views
{
   public partial class MaterialPage : UserControl
   {
      public MaterialPage(Material material, AppViewModel mvm, MaterialVM? existingVm = null)
      {
         InitializeComponent();
         var vm = existingVm ?? new MaterialVM() { Material = material, mvm = mvm };
         DataContext = vm;

         // Заполнить ComboBox типа материала (в блоке Custom) и базового типа
         customTypeCombo.ItemsSource = new[]
         {
            MatType.None, MatType.Concrete, MatType.ReSteelF, MatType.ReSteelU, MatType.Steel, MatType.Custom
         };
         baseTypeCombo.ItemsSource = new[]
         {
            MatType.Concrete, MatType.ReSteelF, MatType.ReSteelU, MatType.Steel
         };

         // ItemsSource для выбора диаграмм из пула проекта
         diagramC.ItemsSource  = mvm.DiagramsLive;
         diagramCL.ItemsSource = mvm.DiagramsLive;
         diagramN.ItemsSource  = mvm.DiagramsLive;
         diagramNL.ItemsSource = mvm.DiagramsLive;

         // Если материал уже Custom — восстановить выбранные диаграммы и показать блок
         if (material.Type == MatType.Custom)
         {
            baseTypeCombo.SelectedItem = material.BaseType;
            SetDiagramCombo(diagramC,  mvm, material.CustomDiagramIds.GetValueOrDefault(CalcType.C));
            SetDiagramCombo(diagramCL, mvm, material.CustomDiagramIds.GetValueOrDefault(CalcType.CL));
            SetDiagramCombo(diagramN,  mvm, material.CustomDiagramIds.GetValueOrDefault(CalcType.N));
            SetDiagramCombo(diagramNL, mvm, material.CustomDiagramIds.GetValueOrDefault(CalcType.NL));
            ShowCustomBlock(true);
         }

         // Переключать блоки при смене типа материала
         vm.PropertyChanged += (_, e) =>
         {
            if (e.PropertyName == nameof(MaterialVM.Type) ||
                e.PropertyName == nameof(MaterialVM.IsCustom))
               ShowCustomBlock(vm.IsCustom);
         };

         // Обновлять CustomDiagramIds при изменении ComboBox-ов диаграмм
         diagramC.SelectionChanged  += (_, _) => UpdateDiagramId(vm, CalcType.C,  diagramC);
         diagramCL.SelectionChanged += (_, _) => UpdateDiagramId(vm, CalcType.CL, diagramCL);
         diagramN.SelectionChanged  += (_, _) => UpdateDiagramId(vm, CalcType.N,  diagramN);
         diagramNL.SelectionChanged += (_, _) => UpdateDiagramId(vm, CalcType.NL, diagramNL);

         standardBodyBlock.SizeChanged += (_, _) => SyncHeaderScrollGutter();
         Loaded += (_, _) => SyncHeaderScrollGutter();
      }

      void ShowCustomBlock(bool custom)
      {
         customBlock.Visibility       = custom ? Visibility.Visible  : Visibility.Collapsed;
         standardHeaderBlock.Visibility = custom ? Visibility.Collapsed : Visibility.Visible;
         standardBodyBlock.Visibility   = custom ? Visibility.Collapsed : Visibility.Visible;
         SyncHeaderScrollGutter();
      }

      void SyncHeaderScrollGutter()
      {
         if (standardHeaderBlock.Visibility != Visibility.Visible)
            return;

         standardBodyBlock.UpdateLayout();
         var gutter = standardBodyBlock.ComputedVerticalScrollBarVisibility == Visibility.Visible
            ? SystemParameters.VerticalScrollBarWidth
            : 0.0;
         standardHeaderBlock.Padding = new Thickness(0, 0, gutter, 0);
      }

      static void SetDiagramCombo(ComboBox combo, AppViewModel mvm, int id)
      {
         if (id == 0) return;
         combo.SelectedItem = mvm.DiagramsLive.FirstOrDefault(d => d.Id == id);
      }

      static void UpdateDiagramId(MaterialVM vm, CalcType ct, ComboBox combo)
      {
         if (combo.SelectedItem is Diagramm d)
            vm.CustomDiagramIds[ct] = d.Id;
      }
   }
}
