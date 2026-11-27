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
            // Строки кода — из таблицы Loc; язык совпадает со словарём App.xaml (ru подключён последним).
            Utilites.Loc.SetLanguage(Utilites.Loc.Russian);
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
                    string.Format(Utilites.Loc.S("AppHelixLoadFailed"), ex.Message),
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
            MessageBox.Show(msg, Utilites.Loc.S("AppUnhandledException"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
