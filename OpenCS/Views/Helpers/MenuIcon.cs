using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenCS.Views.Helpers;

/// <summary>
/// Иконка пункта меню по ключу ресурса из <c>FemCommandIcons.xaml</c>:
/// <c>helpers:MenuIcon.Key="CmdIcon_Delete"</c> ставит <see cref="MenuItem.Icon"/> — изображение 16×16,
/// бледное у недоступного пункта. Ключ строкой, а не StaticResource: так пункт не ломает разметку при
/// опечатке (иконки просто нет), а тест проверяет ключи по тексту XAML.
/// </summary>
public static class MenuIcon
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(MenuIcon), new PropertyMetadata(null, OnKeyChanged));

    public static string? GetKey(DependencyObject d) => (string?)d.GetValue(KeyProperty);
    public static void SetKey(DependencyObject d, string? value) => d.SetValue(KeyProperty, value);

    static void OnKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MenuItem item)
            item.Icon = e.NewValue is string key ? Create(key, item) : null;
    }

    static Style? _imageStyle;

    /// <summary>Изображение иконки по ключу; null, если ресурса нет.</summary>
    internal static Image? Create(string key, FrameworkElement? scope = null)
    {
        var source = (scope?.TryFindResource(key) ?? Application.Current?.TryFindResource(key)) as ImageSource;
        if (source == null) return null;
        var image = new Image { Source = source, Width = 16, Height = 16, Style = ImageStyle() };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    /// <summary>У недоступного пункта WPF гасит только текст — иконку гасим сами.</summary>
    static Style ImageStyle()
    {
        if (_imageStyle != null) return _imageStyle;
        var style = new Style(typeof(Image));
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.35));
        style.Triggers.Add(disabled);
        style.Seal();
        return _imageStyle = style;
    }
}
