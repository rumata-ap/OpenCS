using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

using CScore;

namespace OpenCS.Views
{
   public partial class DeleteForceSetsDialog : Window
   {
      public List<ForceSet> SelectedSets { get; private set; } = [];

      public DeleteForceSetsDialog(IEnumerable<ForceSet> forceSets)
      {
         InitializeComponent();
         Owner = Application.Current.MainWindow;
         ForceSetsList.ItemsSource = forceSets;
         ForceSetsList.SelectionChanged += OnSelectionChanged;
         UpdateCount();
      }

      void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateCount();

      void UpdateCount()
      {
         int count = ForceSetsList.SelectedItems.Count;
         DeleteBtn.IsEnabled = count > 0;
         CountText.Text = count > 0
            ? string.Format((string)FindResource("DeleteSelectedForceSetsCount"), count)
            : "";
      }

      void Delete_Click(object sender, RoutedEventArgs e)
      {
         SelectedSets = new List<ForceSet>(ForceSetsList.SelectedItems.Cast<ForceSet>());
         DialogResult = true;
      }
   }
}
