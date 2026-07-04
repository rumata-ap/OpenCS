using System.Windows;
using System.Windows.Controls;

namespace OpenCS.Views
{
   /// <summary>Справочная панель правила знаков усилий (стержень или пластина).</summary>
   public partial class ForceSignConventionPanel : UserControl
   {
      public static readonly DependencyProperty KindProperty =
         DependencyProperty.Register(
            nameof(Kind), typeof(string), typeof(ForceSignConventionPanel),
            new PropertyMetadata("bar"));

      public ForceSignConventionPanel()
      {
         InitializeComponent();
      }

      /// <summary>Тип набора: "bar" или "shell".</summary>
      public string Kind
      {
         get => (string)GetValue(KindProperty);
         set => SetValue(KindProperty, value);
      }
   }
}
