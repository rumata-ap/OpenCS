using System.Windows;

namespace OpenCS.Views
{
   public partial class LiraElemRangeDialog : Window
   {
      public LiraElemRangeDialog()
      {
         InitializeComponent();
         Owner = Application.Current.MainWindow;
         DataContext = this;
         RangeBox.Focus();
      }

      public string Range { get; set; } = "";

      void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

      /// <summary>
      /// Разбирает строку диапазонов в формате ЛираСАПР.
      /// Разделители: пробел или запятая. Пример: "101-103 106 116-118 121-127 143 144"
      /// </summary>
      public static List<int> ParseRange(string s)
      {
         var ids = new SortedSet<int>();
         // ЛИРА использует пробел как разделитель; поддерживаем также запятую
         foreach (var part in s.Split(new[] { ' ', ',', '\t' },
                      StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
         {
            var dash = part.IndexOf('-');
            if (dash > 0 &&
                int.TryParse(part[..dash], out int from) &&
                int.TryParse(part[(dash + 1)..], out int to))
            {
               for (int i = from; i <= to; i++) ids.Add(i);
            }
            else if (int.TryParse(part, out int single))
            {
               ids.Add(single);
            }
         }
         return [.. ids];
      }
   }
}
