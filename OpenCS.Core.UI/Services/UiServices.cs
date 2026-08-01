namespace OpenCS.Services
{
   /// <summary>Статический доступ к платформенным сервисам UI. Регистрируется GUI-проектом
   /// при старте (WPF: в App.OnStartup; Avalonia: в App.axaml.cs) по аналогии с Loc.ResourceResolver.</summary>
   public static class UiServices
   {
      /// <summary>Диалоговые окна сообщений (WPF MessageBox / MsBox.Avalonia).</summary>
      public static IDialogService Dialogs { get; set; } = null!;

      /// <summary>Маршалинг в UI-поток (WPF Dispatcher / Avalonia Dispatcher.UIThread).</summary>
      public static IDispatcherService Dispatcher { get; set; } = null!;

      /// <summary>Фабрика страниц и диалогов (WPF сейчас / Avalonia позже).</summary>
      public static IAppPageFactory Pages { get; set; } = null!;

      /// <summary>Платформенное окружение приложения (главное окно, язык, завершение).</summary>
      public static IAppController AppController { get; set; } = null!;
   }
}
