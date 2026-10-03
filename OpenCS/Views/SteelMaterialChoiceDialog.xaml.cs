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
      public SteelMaterialChoiceDialog(string prompt, IReadOnlyList<string> items, int selected, string? title = null)
      {
         InitializeComponent();
         Owner = Application.Current.MainWindow;
         if (title != null) Title = title;
         PromptText.Text = prompt;
         Choices.ItemsSource = items;
         Choices.SelectedIndex = selected;
         Choices.Focus();
      }

      /// <summary>Выбранный пункт; −1 — ничего.</summary>
      public int SelectedIndex => Choices.SelectedIndex;

      void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = Choices.SelectedIndex >= 0;
   }
}
