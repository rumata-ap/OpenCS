using CScore;
using OpenCS.ViewModels;

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace OpenCS.Views
{
   public partial class MaterialCharsPage : UserControl
   {
      readonly AppViewModel mvm;
      readonly Material parentMaterial;

      public MaterialCharsPage(MaterialCharsVM vm, Material parent, AppViewModel appVM)
      {
         InitializeComponent();
         DataContext = vm;
         mvm = appVM;
         parentMaterial = parent;
      }

      void Save_Click(object sender, RoutedEventArgs e)
      {
         if (DataContext is MaterialCharsVM charsVM)
         {
            parentMaterial.SetJson();
            mvm.db.SaveMaterial(parentMaterial);
            mvm.MaterialsSort();
            mvm.LogService.Info($"Характеристики '{charsVM.Tag}' ({charsVM.TypeCalc}) сохранены");
         }
      }

      void BuildDiagram_Click(object sender, RoutedEventArgs e)
      {
         if (DataContext is not MaterialCharsVM charsVM)
            return;

         var materialChars = charsVM.Chars;
         var available = GetAvailableDiagramTypes(materialChars.Type);
         if (available.Count == 0)
         {
            MessageBox.Show("Для данного типа материала нет доступных типов диаграмм.",
               "Нет совместимых диаграмм", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
         }

         var dialog = new DiagramSelectWindow(available);
         if (dialog.ShowDialog() != true || dialog.SelectedType == null)
            return;

         Diagramm diagram;
         try
         {
            diagram = dialog.SelectedType.Value switch
            {
               DiagrammType.L2 => materialChars.D2L(),
               DiagrammType.L3 => materialChars.D3L(),
               DiagrammType.SP63 => materialChars.DCL(),
               _ => throw new ArgumentException($"Тип диаграммы {dialog.SelectedType} не поддерживается")
            };
         }
         catch (Exception ex)
         {
            MessageBox.Show($"Ошибка создания диаграммы: {ex.Message}",
               "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
         }

          diagram.MaterialId = parentMaterial.Id;
          diagram.CalcType = materialChars.TypeCalc;
          diagram.Tag ??= $"{parentMaterial.Tag} [{dialog.SelectedType}] {materialChars.TypeCalc}";

          if (diagram.Ic != null)
          {
             var xi = diagram.Ic.X;
             var yi = diagram.Ic.Y;
             mvm.LogService.Info($"Сжатие: {xi.Length} точек, x=[{string.Join("; ", xi.Select(v => v.ToString("F6")))}], y=[{string.Join("; ", yi.Select(v => v.ToString("F2")))}]");
          }
          if (diagram.It != null)
          {
             var xi = diagram.It.X;
             var yi = diagram.It.Y;
             mvm.LogService.Info($"Растяжение: {xi.Length} точек, x=[{string.Join("; ", xi.Select(v => v.ToString("F6")))}], y=[{string.Join("; ", yi.Select(v => v.ToString("F2")))}]");
          }

          mvm.db.SaveDiagram(diagram);
          mvm.Diagrams.Add(diagram);
          mvm.DiagramsLive.Add(diagram);
          mvm.LogService.Info($"Диаграмма '{diagram.Tag}' построена");
          mvm.CurrentDiagram = diagram;
      }

      static List<DiagramSelectWindow.TypeOption> GetAvailableDiagramTypes(MatType matType)
      {
         var list = new List<DiagramSelectWindow.TypeOption>();
         switch (matType)
         {
            case MatType.Concrete:
               list.Add(new(DiagrammType.L2, "Двухлинейная (L2) — СП63.13330"));
               list.Add(new(DiagrammType.L3, "Трёхлинейная (L3) — СП63.13330"));
               list.Add(new(DiagrammType.SP63, "Криволинейная (СП63) — Приложение Г"));
               break;
            case MatType.ReSteelF:
               list.Add(new(DiagrammType.L2, "Двухлинейная (L2) — арматура с физическим пределом"));
               break;
            case MatType.ReSteelU:
               list.Add(new(DiagrammType.L3, "Трёхлинейная (L3) — арматура с условным пределом"));
               break;
            case MatType.Steel:
               list.Add(new(DiagrammType.L2, "Двухлинейная (L2) — сталь конструкционная"));
               break;
         }
         return list;
      }

      void EditParentMaterial_Click(object sender, RoutedEventArgs e)
      {
         mvm.CurrentMaterial = parentMaterial;
      }
   }
}
