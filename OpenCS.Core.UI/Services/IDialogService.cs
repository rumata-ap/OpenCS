namespace OpenCS.Services
{
   /// <summary>Кнопки диалога сообщения.</summary>
   public enum MsgButtons { Ok, YesNo }

   /// <summary>Результат диалога сообщения.</summary>
   public enum MsgResult { None, Ok, Yes, No }

   /// <summary>Абстракция диалоговых окон сообщений (WPF MessageBox / MsBox.Avalonia).</summary>
   public interface IDialogService
   {
      /// <summary>Подтверждение (Да/Нет). Возвращает true при «Да».</summary>
      bool Confirm(string messageKey, string titleKey);
      /// <summary>Предупреждение (ОК).</summary>
      void ShowWarning(string messageKey, string titleKey);
      /// <summary>Ошибка (ОК).</summary>
      void ShowError(string messageKey, string titleKey);
      /// <summary>Информация (ОК).</summary>
      void ShowInfo(string messageKey, string titleKey);
      /// <summary>Ошибка (ОК) с форматированным сообщением из ресурса.</summary>
      void ShowErrorFormatted(string formatKey, string titleKey, params object[] args);
      /// <summary>Диалог выбора формата и режима экспорта эпюры разреза. Возвращает null при отмене.</summary>
      OpenCS.ViewModels.SectionCutExportOptions? ShowSectionCutExportDialog();
   }
}
