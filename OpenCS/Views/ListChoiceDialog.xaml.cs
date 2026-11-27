using System.Globalization;
using System.Windows;
using OpenCS.Utilites;

namespace OpenCS.Views
{
   /// <summary>
   /// Выбор одного пункта из списка с необязательным числовым полем под ним (поле видно при одном пункте). Применения:
   /// сталь стальных сечений стержней импорта (материал проекта или новая марка из справочника) и армирование создаваемых
   /// ЖБ-сечений стержней (без / заданное / подобранное с допуском унификации, %).
   /// </summary>
   public partial class ListChoiceDialog : Window
   {
      /// <param name="prompt">Пояснение над списком.</param>
      /// <param name="items">Подписи пунктов.</param>
      /// <param name="selected">Пункт по умолчанию.</param>
      /// <param name="title">Заголовок окна; null — «Сталь стальных сечений».</param>
      /// <param name="number">Числовое поле под списком (подпись, значение, пункт, при котором оно видно, допустимый
      /// диапазон); null — без поля.</param>
      public ListChoiceDialog(string prompt, IReadOnlyList<string> items, int selected, string? title = null,
         (string Label, double Value, int ForIndex, double Min, double Max)? number = null)
      {
         InitializeComponent();
         Owner = Application.Current?.MainWindow;
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

      /// <summary>Выбранный пункт; −1 — ничего.</summary>
      public int SelectedIndex => Choices.SelectedIndex;

      /// <summary>Число поля в диапазоне (запятая и точка — обе); иначе текст ошибки.</summary>
      public static bool TryReadNumber(string text, double min, double max, out double value, out string? error)
      {
         error = null;
         if (double.TryParse(text.Trim().Replace('.', ','), NumberStyles.Float, CultureInfo.GetCultureInfo("ru-RU"), out value)
             && double.IsFinite(value) && value >= min && value <= max)
            return true;
         error = string.Format(Loc.S("ListChoiceNumberRange"), min.ToString("0.##", CultureInfo.CurrentCulture),
            max.ToString("0.##", CultureInfo.CurrentCulture));
         return false;
      }

      void Ok_Click(object sender, RoutedEventArgs e)
      {
         if (Choices.SelectedIndex < 0) return;
         if (_number is { } n)
         {
            if (!TryReadNumber(NumberBox.Text, n.Min, n.Max, out double v, out string? error))
            {
               if (Choices.SelectedIndex == n.ForIndex)
               {
                  NumberErrorText.Text = error;
                  NumberErrorText.Visibility = Visibility.Visible;
                  NumberBox.Focus();
                  NumberBox.SelectAll();
                  return;
               }
               v = n.Value; // поле скрыто — прежнее значение
            }
            Number = v;
         }
         DialogResult = true;
      }

      void NumberBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
      {
         if (NumberErrorText != null) NumberErrorText.Visibility = Visibility.Collapsed;
      }
   }
}
