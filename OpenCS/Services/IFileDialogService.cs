namespace OpenCS.Services
{
   /// <summary>
   /// Сервис файловых диалогов. Абстрагирует OpenFileDialog/SaveFileDialog от ViewModel.
   /// </summary>
   public interface IFileDialogService
   {
      string? OpenFile(string? filter = null, string? title = null);

      /// <summary>Открыть файл; диалог начинается с файла (или каталога) <paramref name="initialPath"/>.</summary>
      string? OpenFile(string? filter, string? title, string? initialPath) => OpenFile(filter, title);
      string? SaveFile(string? filter = null, string? defaultExt = null, string? title = null);
      string? SelectFolder(string? title = null, string? initialDirectory = null);
   }
}