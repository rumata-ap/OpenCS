using System.Windows;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using OpenCS.Views;

namespace OpenCS.Services
{
   /// <summary>Реализация IDialogService поверх WPF MessageBox.</summary>
   public class WpfDialogService : IDialogService
   {
      public bool Confirm(string messageKey, string titleKey)
      {
         var res = MessageBox.Show(Loc.S(messageKey), Loc.S(titleKey),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
         return res == MessageBoxResult.Yes;
      }

      public bool ConfirmFormatted(string formatKey, string titleKey, params object[] args)
      {
         var res = MessageBox.Show(string.Format(Loc.S(formatKey), args), Loc.S(titleKey),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
         return res == MessageBoxResult.Yes;
      }

      public MsgResult ConfirmCancel(string messageKey, string titleKey)
      {
         var res = MessageBox.Show(Loc.S(messageKey), Loc.S(titleKey),
            MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
         return res switch
         {
            MessageBoxResult.Yes => MsgResult.Yes,
            MessageBoxResult.No => MsgResult.No,
            _ => MsgResult.None
         };
      }

      public void ShowWarning(string messageKey, string titleKey)
         => MessageBox.Show(Loc.S(messageKey), Loc.S(titleKey),
            MessageBoxButton.OK, MessageBoxImage.Warning);

      public void ShowError(string messageKey, string titleKey)
         => MessageBox.Show(Loc.S(messageKey), Loc.S(titleKey),
            MessageBoxButton.OK, MessageBoxImage.Error);

      public void ShowInfo(string messageKey, string titleKey)
         => MessageBox.Show(Loc.S(messageKey), Loc.S(titleKey),
            MessageBoxButton.OK, MessageBoxImage.Information);

      public void ShowErrorFormatted(string formatKey, string titleKey, params object[] args)
         => MessageBox.Show(string.Format(Loc.S(formatKey), args), Loc.S(titleKey),
            MessageBoxButton.OK, MessageBoxImage.Error);

      public void ShowWarningText(string message, string title)
         => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

      public void ShowErrorText(string message, string title)
         => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

      public void ShowInfoText(string message, string title)
         => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

      public SectionCutExportOptions? ShowSectionCutExportDialog()
      {
         var win = new SectionCutExportDialogWindow
         {
            Owner = Application.Current?.MainWindow
         };
         return win.ShowDialog() == true ? win.Result : null;
      }
   }
}
