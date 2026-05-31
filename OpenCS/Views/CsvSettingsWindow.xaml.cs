using OpenCS.Utilites;

using System.Windows;

namespace OpenCS.Views
{
   public partial class CsvSettingsWindow : Window
   {
      readonly AppViewModel _mvm;
      readonly CsvExportSettings _settings;

      public CsvSettingsWindow(AppViewModel mvm)
      {
         InitializeComponent();
         _mvm = mvm;
         _settings = mvm.CsvSettings.Clone();
         Owner = Application.Current.MainWindow;

         LoadToUi();
         CsvSemicolon.Checked += (_, _) => _settings.Delimiter = ";";
         CsvComma.Checked += (_, _) => _settings.Delimiter = ",";
         CsvUtf8.Checked += (_, _) => _settings.Encoding = "utf-8";
         CsvWin1251.Checked += (_, _) => _settings.Encoding = "windows-1251";
      }

      void LoadToUi()
      {
         CsvSemicolon.IsChecked = _settings.Delimiter == ";";
         CsvComma.IsChecked = _settings.Delimiter == ",";
         CsvUtf8.IsChecked = _settings.Encoding == "utf-8";
         CsvWin1251.IsChecked = _settings.Encoding == "windows-1251";
      }

      void Ok_Click(object sender, RoutedEventArgs e)
      {
         _mvm.CsvSettings = _settings.Clone();
         _mvm.db.SaveCsvSettings(_mvm.CsvSettings);
         Close();
      }

      void Cancel_Click(object sender, RoutedEventArgs e)
      {
         Close();
      }
   }
}
