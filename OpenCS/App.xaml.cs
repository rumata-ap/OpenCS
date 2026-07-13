using System.Windows;
using System.Windows.Threading;

namespace OpenCS
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
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
            MessageBox.Show(msg, "Необработанное исключение",
                MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
