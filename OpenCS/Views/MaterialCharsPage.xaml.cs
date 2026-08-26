using CScore;
using OpenCS.Utilites;
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
             mvm.LogService.Info(string.Format(Loc.S("CharsSaved"), charsVM.Tag, charsVM.TypeCalc));
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
             MessageBox.Show(Loc.S("NoDiagramTypes"),
                Loc.S("NoCompatibleDiagrams"), MessageBoxButton.OK, MessageBoxImage.Information);
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
               DiagrammType.L2   => materialChars.D2L(),
               DiagrammType.L3   => materialChars.D3L(),
               DiagrammType.SP63 => materialChars.DCL(mvm.CalcSettings.Sp63DescEtaMin),
               DiagrammType.EKB  => materialChars.DEKB(),
                DiagrammType.SP35 => materialChars.DSP35(),
                DiagrammType.SP16 => materialChars.DSP16(),
                _ => throw new ArgumentException(string.Format(Loc.S("DiagramTypeNotSupported"), dialog.SelectedType))
            };
         }
         catch (Exception ex)
         {
             MessageBox.Show(string.Format(Loc.S("DiagramCreateError"), ex.Message),
                Loc.S("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            return;
         }

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
           mvm.LogService.Info(string.Format(Loc.S("DiagramBuilt"), diagram.Tag));
          mvm.CurrentDiagram = diagram;
      }

      /// <summary>
      /// Список диаграмм, доступных для типа материала. Набор берётся из
      /// <see cref="DiagrammCompatibility"/> (единый источник правды), здесь только подписи.
      /// </summary>
      static List<DiagramSelectWindow.TypeOption> GetAvailableDiagramTypes(MatType matType)
      {
         var list = new List<DiagramSelectWindow.TypeOption>();
         foreach (var type in DiagrammCompatibility.Allowed(matType))
            list.Add(new(type, DiagramTypeLabel(matType, type)));
         return list;
      }

      static string DiagramTypeLabel(MatType matType, DiagrammType type) => (matType, type) switch
      {
         (MatType.Concrete, DiagrammType.L2)   => Loc.S("DiagL2_Concrete"),
         (MatType.Concrete, DiagrammType.L3)   => Loc.S("DiagL3_Concrete"),
         (MatType.Concrete, DiagrammType.SP63) => Loc.S("DiagSP63"),
         (MatType.Concrete, DiagrammType.EKB)  => Loc.S("DiagEKB"),
         (MatType.Concrete, DiagrammType.SP35) => Loc.S("DiagSP35"),
         (MatType.ReSteelF, DiagrammType.L2)   => Loc.S("DiagL2_ReSteelF"),
         (MatType.ReSteelU, DiagrammType.L3)   => Loc.S("DiagL3_ReSteelU"),
         (MatType.Steel,    DiagrammType.L2)   => Loc.S("DiagL2_Steel"),
         (MatType.Steel,    DiagrammType.SP16) => Loc.S("DiagSP16_Steel"),
         _                                     => type.ToString()
      };

      void EditParentMaterial_Click(object sender, RoutedEventArgs e)
      {
         mvm.CurrentMaterial = parentMaterial;
      }
   }
}
