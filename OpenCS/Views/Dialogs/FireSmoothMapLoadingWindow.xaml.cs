using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OpenCS.Views.Dialogs;

/// <summary>Модальное окно ожидания построения плавной T3-карты температуры.</summary>
public partial class FireSmoothMapLoadingWindow : Window
{
    const double MarqueeWidth = 96.0;

    public FireSmoothMapLoadingWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => StartMarquee();
    }

    void StartMarquee()
    {
        double trackWidth = track.ActualWidth > 1 ? track.ActualWidth : 352;
        var anim = new DoubleAnimation
        {
            From = -MarqueeWidth,
            To = trackWidth,
            Duration = new Duration(TimeSpan.FromSeconds(1.1)),
            RepeatBehavior = RepeatBehavior.Forever
        };
        marqueeTransform.BeginAnimation(TranslateTransform.XProperty, anim);
    }
}
