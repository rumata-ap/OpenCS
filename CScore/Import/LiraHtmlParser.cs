using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CScore.Import
{
   /// <summary>Низкоуровневый разбор HTML-отчётов LIRA SAPR.</summary>
   internal static class LiraHtmlParser
   {
      static readonly Regex PreRx = new(@"<pre[^>]*>(.*?)</pre>",
         RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

      static readonly Regex TableRx = new(@"<TABLE\b[^>]*>(.*?)</TABLE>",
         RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

      static readonly Regex RowRx = new(@"<tr\b[^>]*>(.*?)</tr>",
         RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

      public static string ReadHtml(string path)
         => File.ReadAllText(path, Encoding.UTF8);

      public static IReadOnlyList<string> ExtractPreBlocks(string html)
      {
         var list = new List<string>();
         foreach (Match m in PreRx.Matches(html))
            list.Add(CleanText(m.Groups[1].Value));
         return list;
      }

      public static IReadOnlyList<(string Body, IReadOnlyList<IReadOnlyList<string>> Rows)> ExtractTables(string html)
      {
         var tables = new List<(string, IReadOnlyList<IReadOnlyList<string>>)>();
         foreach (Match m in TableRx.Matches(html))
         {
            var body = m.Groups[1].Value;
            var rows = new List<IReadOnlyList<string>>();
            foreach (Match rm in RowRx.Matches(body))
            {
               var cells = ParseRowCells(rm.Groups[1].Value);
               if (cells.Count > 0)
                  rows.Add(cells);
            }
            if (rows.Count > 0)
               tables.Add((body, rows));
         }
         return tables;
      }

      static List<string> ParseRowCells(string rowHtml)
      {
         var cells = new List<string>();
         int pos = 0;
         while (pos < rowHtml.Length)
         {
            int lt = rowHtml.IndexOf('<', pos);
            if (lt < 0) break;
            if (!rowHtml.AsSpan(lt).StartsWith("<th", StringComparison.OrdinalIgnoreCase)
                && !rowHtml.AsSpan(lt).StartsWith("<td", StringComparison.OrdinalIgnoreCase))
            {
               pos = lt + 1;
               continue;
            }

            int gt = rowHtml.IndexOf('>', lt);
            if (gt < 0) break;
            int next = rowHtml.IndexOf('<', gt + 1);
            if (next < 0) next = rowHtml.Length;

            cells.Add(CleanText(rowHtml.Substring(gt + 1, next - gt - 1)));
            pos = next;
         }
         return cells;
      }

      public static string CleanText(string s)
      {
         if (string.IsNullOrEmpty(s)) return "";
         s = Regex.Replace(s, @"&nbsp;?", " ", RegexOptions.IgnoreCase);
         s = s.Replace("\uFEFF", "").Replace("\u200B", "").Replace("\u200C", "").Replace("\u200D", "");
         s = Regex.Replace(s, @"\s+", " ").Trim();
         return s;
      }

      public static bool IsEmptyCell(string s)
      {
         s = CleanText(s);
         return string.IsNullOrWhiteSpace(s);
      }

      public static double ParseDouble(string s)
      {
         s = CleanText(s);
         if (string.IsNullOrEmpty(s)) return 0.0;
         s = s.Replace(',', '.');
         return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0.0;
      }

      public static string NormalizeSectionLabel(string header)
      {
         header = CleanText(header);
         if (IsEmptyCell(header)) return "";
         return Regex.Replace(header, @"\s*-\s*", "-");
      }

      public static string? ExtractSchemeName(IReadOnlyList<string> preBlocks)
      {
         foreach (var pre in preBlocks)
         {
            if (pre.Contains("основная схема", StringComparison.OrdinalIgnoreCase)
                || pre.Contains("схема", StringComparison.OrdinalIgnoreCase))
            {
               var s = CleanText(pre);
               int idx = s.IndexOf("основная", StringComparison.OrdinalIgnoreCase);
               if (idx < 0) idx = s.IndexOf("схема", StringComparison.OrdinalIgnoreCase);
               if (idx > 0)
               {
                  var name = CleanText(s[..idx]);
                  int sp = name.LastIndexOf(' ');
                  if (sp >= 0 && sp + 1 < name.Length)
                     name = name[(sp + 1)..];
                  if (!string.IsNullOrWhiteSpace(name))
                     return name;
               }
               return s;
            }
         }
         return null;
      }
   }
}
