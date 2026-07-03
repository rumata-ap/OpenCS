using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OpenCS.Views
{
   /// <summary>
   /// Панель кнопок переноса элементов между списками.
   /// Содержит до трёх кнопок: «Включить один» (In), «Включить все» (InAll), «Исключить» (Out).
   /// Кнопки с неустановленными командами скрываются автоматически.
   /// </summary>
   public partial class TransferPanel : UserControl
   {
      /// <summary>Команда переноса одного элемента (стрелка влево).</summary>
      public static readonly DependencyProperty InCommandProperty =
         DependencyProperty.Register(nameof(InCommand), typeof(ICommand), typeof(TransferPanel),
            new PropertyMetadata(null, OnCommandChanged));

      /// <summary>Команда переноса всех элементов (двойная стрелка влево).</summary>
      public static readonly DependencyProperty InAllCommandProperty =
         DependencyProperty.Register(nameof(InAllCommand), typeof(ICommand), typeof(TransferPanel),
            new PropertyMetadata(null, OnCommandChanged));

      /// <summary>Команда возврата элемента (стрелка вправо).</summary>
      public static readonly DependencyProperty OutCommandProperty =
         DependencyProperty.Register(nameof(OutCommand), typeof(ICommand), typeof(TransferPanel),
            new PropertyMetadata(null, OnCommandChanged));

      public ICommand InCommand
      {
         get => (ICommand)GetValue(InCommandProperty);
         set => SetValue(InCommandProperty, value);
      }

      public ICommand InAllCommand
      {
         get => (ICommand)GetValue(InAllCommandProperty);
         set => SetValue(InAllCommandProperty, value);
      }

      public ICommand OutCommand
      {
         get => (ICommand)GetValue(OutCommandProperty);
         set => SetValue(OutCommandProperty, value);
      }

      public TransferPanel()
      {
         InitializeComponent();
      }

      private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
      {
         ((TransferPanel)d).UpdateVisibility();
      }

      private void UpdateVisibility()
      {
         UpdateRow(InCommand, InButton, InRow);
         UpdateRow(InAllCommand, InAllButton, InAllRow);
         UpdateRow(OutCommand, OutButton, OutRow);
      }

      private static void UpdateRow(ICommand command, Button button, RowDefinition row)
      {
         if (command != null)
         {
            button.Visibility = Visibility.Visible;
            row.Height = new GridLength(1, GridUnitType.Star);
         }
         else
         {
            button.Visibility = Visibility.Collapsed;
            row.Height = new GridLength(0);
         }
      }
   }
}