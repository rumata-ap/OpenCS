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

      /// <summary>
      /// Обратное к <see cref="ParseRange"/>: номера сворачиваются в диапазоны «101-103 106».
      /// Нечисловые теги дописываются в конец как есть.
      /// </summary>
      public static string FormatRange(IEnumerable<string> tags)
      {
         var numbers = new SortedSet<int>();
         var other = new List<string>();
         foreach (var tag in tags)
            if (int.TryParse(tag, out int n)) numbers.Add(n);
            else other.Add(tag);

         var parts = new List<string>();
         int? from = null, to = null;
         foreach (int n in numbers)
         {
            if (to == n - 1) { to = n; continue; }
            if (from != null) parts.Add(from == to ? $"{from}" : $"{from}-{to}");
            from = to = n;
         }
         if (from != null) parts.Add(from == to ? $"{from}" : $"{from}-{to}");
         parts.AddRange(other);
         return string.Join(" ", parts);
      }
   }
}
