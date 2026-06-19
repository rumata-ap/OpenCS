namespace CScore.Import
{
   internal static class LiraForceKeys
   {
      public static readonly HashSet<string> Bar = new(StringComparer.OrdinalIgnoreCase)
         { "N", "MX", "MY", "QZ", "MZ", "QY" };

      public static readonly HashSet<string> Shell = new(StringComparer.OrdinalIgnoreCase)
         { "NX", "NY", "TXY", "MX", "MY", "MXY", "QX", "QY" };

      public static readonly HashSet<string> Displacement = new(StringComparer.OrdinalIgnoreCase)
         { "RX", "RY", "RUX", "RUY", "RUZ" };

      /// <summary>Ключи LIRA, не переносимые в набор усилий OpenCS.</summary>
      public static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
         { "RZ" }; // реактивный отпор грунта (оболочки)

      public static string Normalize(string key)
         => LiraHtmlParser.CleanText(key).ToUpperInvariant();

      public static bool IsIgnored(string key)
      {
         key = Normalize(key);
         return Ignored.Contains(key) || Displacement.Contains(key);
      }

      public static LiraElementKind? DetectKind(IEnumerable<string> rowKeys)
      {
         bool bar = false, shell = false;
         foreach (var raw in rowKeys)
         {
            var k = Normalize(raw);
            if (IsIgnored(k)) continue;
            // MX/MY/QY общие — для типа используем только различающие ключи
            if (k is "N" or "QZ" or "MZ") bar = true;
            if (k is "NX" or "NY" or "TXY" or "MXY" or "QX") shell = true;
         }
         if (bar && shell) return null;
         if (shell) return LiraElementKind.Shell;
         if (bar) return LiraElementKind.Bar;
         return LiraElementKind.Unknown;
      }

      public static bool IsForceRow(string firstCell)
      {
         var k = Normalize(firstCell);
         if (IsIgnored(k)) return false;
         return Bar.Contains(k) || Shell.Contains(k);
      }
   }
}
