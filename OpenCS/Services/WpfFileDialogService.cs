using Microsoft.Win32;

namespace OpenCS.Services
{
   /// <summary>
   /// WPF-реализация файловых диалогов через OpenFileDialog/SaveFileDialog.
   /// </summary>
   public class WpfFileDialogService : IFileDialogService
   {
      public string? OpenFile(string? filter = null, string? title = null)
      {
         var dialog = new OpenFileDialog
         {
            Filter = filter ?? string.Empty,
            Title = title ?? string.Empty
         };
         return dialog.ShowDialog() == true ? dialog.FileName : null;
      }

      public string? SaveFile(string? filter = null, string? defaultExt = null, string? title = null)
      {
         var dialog = new SaveFileDialog
         {
            Filter = filter ?? string.Empty,
            DefaultExt = defaultExt ?? string.Empty,
            Title = title ?? string.Empty
         };
         return dialog.ShowDialog() == true ? dialog.FileName : null;
      }

      public string? SelectFolder(string? title = null, string? initialDirectory = null)
      {
         var dialog = new OpenFolderDialog
         {
            Title = title ?? string.Empty,
            InitialDirectory = initialDirectory ?? string.Empty
         };
         return dialog.ShowDialog() == true ? dialog.FolderName : null;
      }
   }
}