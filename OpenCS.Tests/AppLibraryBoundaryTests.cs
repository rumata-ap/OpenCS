using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Граница библиотеки приложения (Этап 0): OpenCS.App собирается под net10.0 без WPF и должна
/// оставаться такой — её используют консоль и будущая оболочка Avalonia.
/// </summary>
public class AppLibraryBoundaryTests
{
    static readonly string[] UiAssemblies =
    [
        "PresentationFramework", "PresentationCore", "WindowsBase", "System.Xaml",
        "System.Windows.Forms", "HelixToolkit.Wpf", "Interop.LiraSaprRes"
    ];

    [Fact]
    public void AppAssembly_DoesNotReferenceUiAssemblies()
    {
        var app = typeof(DatabaseService).Assembly;
        Assert.Equal("OpenCS.App", app.GetName().Name);

        var references = app.GetReferencedAssemblies().Select(r => r.Name).ToList();
        var leaked = references.Where(name => UiAssemblies.Contains(name, StringComparer.OrdinalIgnoreCase)).ToList();
        Assert.True(leaked.Count == 0, "OpenCS.App ссылается на UI-сборки: " + string.Join(", ", leaked));
    }

    [Fact]
    public void CliAssembly_DoesNotReferenceUiAssemblies()
    {
        var cli = typeof(OpenCS.Cli.CliApp).Assembly;
        Assert.Equal("OpenCS.Cli", cli.GetName().Name);

        var leaked = cli.GetReferencedAssemblies().Select(r => r.Name)
            .Where(name => UiAssemblies.Contains(name, StringComparer.OrdinalIgnoreCase) || name == "OpenCS").ToList();
        Assert.True(leaked.Count == 0, "OpenCS.Cli ссылается на UI-сборки: " + string.Join(", ", leaked));
    }

    [Fact]
    public void StringDictionaries_AreEmbeddedInAppAssembly()
    {
        var names = typeof(Loc).Assembly.GetManifestResourceNames();
        Assert.Contains("OpenCS.Strings.ru-RU.xaml", names);
        Assert.Contains("OpenCS.Strings.en-US.xaml", names);
    }

    [Fact]
    public void DataSource_IsCopiedNextToConsumer()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "DataSource");
        Assert.True(File.Exists(Path.Combine(dir, "Sortamenty.db3")), "нет DataSource/Sortamenty.db3 в выходе потребителя");
        Assert.True(Directory.GetFiles(dir, "*.csv").Length >= 28, "не все справочники DataSource скопированы");
    }
}
