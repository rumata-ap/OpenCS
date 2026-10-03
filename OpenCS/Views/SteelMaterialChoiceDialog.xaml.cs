using System.Globalization;
using System.Windows;

namespace OpenCS.Views
{
   /// <summary>
   /// Выбор стали для стальных сечений стержней импортированной схемы: стальной материал проекта либо
   /// новая марка из справочника (последний пункт списка). Также — общий выбор одного пункта из списка
   /// (армирование создаваемых сечений стержней).
   /// </summary>
   public partial class SteelMaterialChoiceDialog : Window
   {
      /// <param name="prompt">Пояснение над списком.</param>
      /// <param name="items">Подписи пунктов: материалы проекта, затем «создать из справочника».</param>
      /// <param name="selected">Пункт по умолчанию.</param>
      /// <param name="title">Заголовок окна; null — «Сталь стальных сечений».</param>
      /// <param name="number">Числовое поле под списком (подпись, значение, пункт, при котором оно видно, допустимый
      /// диапазон); null — без поля.</param>
      public SteelMaterialChoiceDialog(string prompt, IReadOnlyList<string> items, int selected, string? title = null,
         (string Label, double Value, int ForIndex, double Min, double Max)? number = null)
      {
         InitializeComponent();
         Owner = Application.Current.MainWindow;
         if (title != null) Title = title;
         PromptText.Text = prompt;
         Choices.ItemsSource = items;
         Choices.SelectedIndex = selected;
         if (number is { } n)
         {
            _number = n;
            NumberLabel.Text = n.Label;
            NumberBox.Text = n.Value.ToString("0.##", CultureInfo.CurrentCulture);
            void Sync() => NumberPanel.Visibility = Choices.SelectedIndex == n.ForIndex ? Visibility.Visible : Visibility.Collapsed;
            Choices.SelectionChanged += (_, _) => Sync();
            Sync();
         }
         Choices.Focus();
      }

      readonly (string Label, double Value, int ForIndex, double Min, double Max)? _number;

      /// <summary>Значение числового поля; null — поля нет.</summary>
      public double? Number { get; private set; }

      static bool TryParse(string text, out double value) =>
         double.TryParse(text.Trim().Replace('.', ','), NumberStyles.Float, CultureInfo.GetCultureInfo("ru-RU"), out value);

      /// <summary>Выбранный пункт; −1 — ничего.</summary>
      public int SelectedIndex => Choices.SelectedIndex;

      void Ok_Click(object sender, RoutedEventArgs e)
      {
         if (Choices.SelectedIndex < 0) return;
         if (_number is { } n)
         {
            if (!TryParse(NumberBox.Text, out double v) || v < n.Min || v > n.Max)
            {
               if (Choices.SelectedIndex == n.ForIndex)
               {
                  NumberBox.Focus();
                  NumberBox.SelectAll();
                  return;
               }
               v = n.Value;
            }
            Number = v;
         }
         DialogResult = true;
      }
   }
}
