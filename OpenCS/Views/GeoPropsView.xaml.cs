using System.Windows.Controls;
using CScore;

namespace OpenCS.Views
{
   /// <summary>
   /// Представление геометрических характеристик сечения (Area, EA, ESx, ESy, EIx, EIy, EIxy, Xc, Yc).
   /// Привязывается к свойству GeoProps через DataContext.
   /// </summary>
   public partial class GeoPropsView : UserControl
   {
      public GeoPropsView()
      {
         InitializeComponent();
      }

      /// <summary>
      /// DependencyProperty для привязки объекта GeoProps.
      /// </summary>
      public static readonly System.Windows.DependencyProperty GeoPropsProperty =
         System.Windows.DependencyProperty.Register(nameof(GeoProps), typeof(GeoProps), typeof(GeoPropsView),
            new System.Windows.FrameworkPropertyMetadata(null, OnGeoPropsChanged));

      /// <summary>
      /// Геометрические характеристики сечения.
      /// </summary>
      public GeoProps GeoProps
      {
         get => (GeoProps)GetValue(GeoPropsProperty);
         set => SetValue(GeoPropsProperty, value);
      }

      private static void OnGeoPropsChanged(System.Windows.DependencyObject d, System.Windows.DependencyPropertyChangedEventArgs e)
      {
         var control = (GeoPropsView)d;
         control.DataContext = e.NewValue;
      }
   }
}