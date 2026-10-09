using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenCS.Views.Dialogs;

/// <summary>Окно текстового отчёта: текст только для чтения (выделяется и копируется), кнопки «Копировать» и «Закрыть».</summary>
public sealed class TextReportWindow : Window
{
    public TextReportWindow(string title, string text)
    {
        Title = title;
        Width = 760;
        Height = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var box = new TextBox
        {
            Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"),
            // Общий стиль TextBox приложения (App.xaml) — однострочное поле высотой 22 с текстом по центру.
            Height = double.NaN, VerticalAlignment = VerticalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top, HorizontalContentAlignment = HorizontalAlignment.Left,
            TextAlignment = TextAlignment.Left, Padding = new Thickness(4),
        };
        var copy = new Button { Padding = new Thickness(12, 4, 12, 4), MinWidth = 90 };
        copy.SetResourceReference(ContentProperty, "Copy");
        copy.Click += (_, _) => Clipboard.SetText(text);
        var close = new Button { Padding = new Thickness(12, 4, 12, 4), MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        close.SetResourceReference(ContentProperty, "Close");
        close.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(copy);
        buttons.Children.Add(close);
        var root = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(box);
        Content = root;
    }
}
