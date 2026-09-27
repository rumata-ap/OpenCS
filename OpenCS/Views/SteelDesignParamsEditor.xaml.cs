using System.Windows;
using System.Windows.Controls;

namespace OpenCS.Views;

/// <summary>Редактор параметров проверки по СП 16; DataContext — <see cref="ViewModels.SteelDesignParamsEditorVM"/>.</summary>
public partial class SteelDesignParamsEditor : UserControl
{
    /// <summary>Ширина колонки подписей (узкие панели — меньше).</summary>
    public static readonly DependencyProperty LabelWidthProperty = DependencyProperty.Register(
        nameof(LabelWidth), typeof(double), typeof(SteelDesignParamsEditor), new PropertyMetadata(240.0));

    public double LabelWidth
    {
        get => (double)GetValue(LabelWidthProperty);
        set => SetValue(LabelWidthProperty, value);
    }

    public SteelDesignParamsEditor() => InitializeComponent();
}
