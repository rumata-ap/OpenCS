namespace OpenCS.Services
{
   /// <summary>
   /// Сервис файловых диалогов. Абстрагирует OpenFileDialog/SaveFileDialog от ViewModel.
   /// </summary>
   public interface IFileDialogService
   {
      string? OpenFile(string filter = null, string title = null);
      string? SaveFile(string filter = null, string defaultExt = null, string title = null);
   }
}