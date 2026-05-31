using CScore;

using System.Collections.Generic;
using System.Windows;

namespace OpenCS.Views
{
   public partial class DiagramSelectWindow : Window
   {
      public DiagrammType? SelectedType { get; private set; }

      public DiagramSelectWindow(List<TypeOption> availableTypes)
      {
         InitializeComponent();
         diagramTypeList.ItemsSource = availableTypes;
         if (availableTypes.Count > 0)
            diagramTypeList.SelectedIndex = 0;
      }

      void Ok_Click(object sender, RoutedEventArgs e)
      {
         if (diagramTypeList.SelectedItem is TypeOption option)
         {
            SelectedType = option.Type;
            DialogResult = true;
         }
      }

      void Cancel_Click(object sender, RoutedEventArgs e)
      {
         DialogResult = false;
      }

      public class TypeOption
      {
         public DiagrammType Type { get; set; }
         public string Label { get; set; } = "";

         public TypeOption(DiagrammType type, string label)
         {
            Type = type;
            Label = label;
         }
      }
   }
}
