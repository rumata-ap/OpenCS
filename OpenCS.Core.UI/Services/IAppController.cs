namespace OpenCS.Services
{
   /// <summary>Доступ к платформенному окружению приложения (главное окно, язык, завершение).
   /// Реализуется GUI-проектом (WPF: Application.Current; Avalonia: TopLevel/Window).</summary>
   public interface IAppController
   {
      /// <summary>Главное окно приложения (Owner для диалогов). null до создания окна.</summary>
      object? OwnerWindow { get; }

      /// <summary>Переключает словарь локализации приложения (0 = русский, 1 = английский).</summary>
      void SetLanguageDictionary(int lang);

      /// <summary>Закрывает главное окно (штатный выход).</summary>
      void CloseMainWindow();

      /// <summary>Аварийное завершение приложения (главное окно недоступно).</summary>
      void Shutdown();
   }
}
