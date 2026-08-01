using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace OpenCS
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            OpenCS.Utilites.Loc.ResourceResolver =
                key => Application.Current?.TryFindResource(key) as string ?? null;
            OpenCS.Services.UiServices.Dialogs = new OpenCS.Services.WpfDialogService();
            OpenCS.Services.UiServices.Dispatcher = new OpenCS.Services.WpfDispatcherService();
            OpenCS.Services.UiServices.Pages = new OpenCS.Services.WpfAppPageFactory();
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            // Явная загрузка HelixToolkit при старте — иначе при первом открытии
            // FemSchemaView3D WPF иногда не находит сборку по XmlnsDefinition
            // (типично при запуске не из bin/Debug после неполной пересборки).
            try
            {
                _ = typeof(HelixToolkit.Wpf.HelixViewport3D);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Не удалось загрузить HelixToolkit.Wpf (3D-просмотрщик схем).\n" +
                    "Пересоберите OpenCS (Debug) и запускайте exe из\n" +
                    "OpenCS\\bin\\Debug\\net9.0-windows\\OpenCS.exe\n\n" + ex.Message,
                    "HelixToolkit",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            var msg = e.Exception.ToString();
            try
            {
                var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "opencs_debug.log");
                System.IO.File.AppendAllText(logPath,
                    $"[{DateTime.Now:O}] DispatcherUnhandledException: {msg}\n");
            }
            catch
            {
                // Обработчик ошибки не должен завершать приложение, если журнал недоступен.
            }
            MessageBox.Show(msg, "Необработанное исключение",
                MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
