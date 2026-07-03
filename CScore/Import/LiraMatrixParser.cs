namespace CScore.Import
{
   /// <summary>Разбор матричных таблиц: загружения и РСН.</summary>
   internal static class LiraMatrixParser
   {
      sealed class BlockState
      {
         public string Name = "";
         public readonly Dictionary<string, Dictionary<string, double>> Columns = new();
      }

      public static (List<(string Name, LiraElementKind Kind, Dictionary<string, Dictionary<string, double>> Columns)> Blocks, string? Error)
         Parse(IReadOnlyList<IReadOnlyList<IReadOnlyList<string>>> tableRowsList, LiraImportMode mode)
      {
         var merged = new Dictionary<string, BlockState>(StringComparer.Ordinal);
         LiraElementKind? fileKind = null;

         foreach (var rows in tableRowsList)
         {
            if (!IsForceMatrixTable(rows, mode))
               continue;

            var sectionKeys = rows
               .Where(r => r.Count > 0 && LiraForceKeys.IsForceRow(r[0]))
               .Select(r => r[0]);
            var sectionKind = LiraForceKeys.DetectKind(sectionKeys);
            if (sectionKind == null)
               return ([], "Файл содержит смешанные стержневые и оболочечные таблицы. Экспортируйте их отдельно.");
            if (sectionKind == LiraElementKind.Unknown)
               continue;

            if (fileKind == null) fileKind = sectionKind;
            else if (fileKind != sectionKind)
               return ([], "Файл содержит смешанные стержневые и оболочечные таблицы. Экспортируйте их отдельно.");

            string[]? columns = null;
            BlockState? current = null;

            foreach (var row in rows)
            {
               if (row.Count == 0) continue;
               var c0 = LiraHtmlParser.CleanText(row[0]);

               if (IsColumnHeaderRow(row))
               {
                  columns = ExtractColumnLabels(row);
                  current = null;
                  continue;
               }

               if (columns == null) continue;

               if (IsNodeRow(c0, row))
                  continue;

               if (IsBlockHeaderRow(row, mode, out var blockName))
               {
                  if (current != null && !string.IsNullOrWhiteSpace(current.Name))
                     MergeBlock(merged, current);

                  current = merged.TryGetValue(blockName, out var existing)
                     ? existing
                     : new BlockState { Name = blockName };
                  merged[blockName] = current;
                  continue;
               }

               if (!LiraForceKeys.IsForceRow(c0))
                  continue;

               if (current == null) continue;

               var key = LiraForceKeys.Normalize(c0);
               if (LiraForceKeys.IsIgnored(key)) continue;
               int n = Math.Min(columns.Length, row.Count - 1);
               for (int i = 0; i < n; i++)
               {
                  if (LiraHtmlParser.IsEmptyCell(columns[i])) continue;
                  if (!current.Columns.TryGetValue(columns[i], out var dict))
                  {
                     dict = new Dictionary<string, double>(StringComparer.Ordinal);
                     current.Columns[columns[i]] = dict;
                  }
                  dict[key] = LiraHtmlParser.ParseDouble(row[i + 1]);
               }
            }

            if (current != null && !string.IsNullOrWhiteSpace(current.Name))
               MergeBlock(merged, current);
         }

         if (merged.Count == 0)
            return ([], "Не найдена таблица усилий в ожидаемом формате.");

         var kindFinal = fileKind ?? LiraElementKind.Bar;
         var list = merged.Values
            .Where(b => b.Columns.Count > 0)
            .Select(b => (b.Name, kindFinal, b.Columns))
            .ToList();
         return (list, null);
      }

      static void MergeBlock(Dictionary<string, BlockState> merged, BlockState block)
      {
         if (!merged.TryGetValue(block.Name, out var target))
         {
            merged[block.Name] = block;
            return;
         }
         foreach (var (col, dict) in block.Columns)
            target.Columns[col] = dict;
      }

      static bool IsForceMatrixTable(IReadOnlyList<IReadOnlyList<string>> rows, LiraImportMode mode)
      {
         foreach (var row in rows)
         {
            if (row.Count == 0) continue;
            if (IsColumnHeaderRow(row)) return true;
            if (mode == LiraImportMode.LoadCases && row.Any(c => c.Contains("ЗАГРУЖЕНИЕ", StringComparison.OrdinalIgnoreCase)))
               return true;
            if (mode == LiraImportMode.Rsn && row.Any(c => c.Contains("Основное", StringComparison.OrdinalIgnoreCase) || c.Contains("Особое", StringComparison.OrdinalIgnoreCase)))
               return true;
         }
         return false;
      }

      static bool IsColumnHeaderRow(IReadOnlyList<string> row)
      {
         if (row.Count < 2) return false;
         var c0 = LiraHtmlParser.CleanText(row[0]);
         if (c0.Length > 1 && c0.EndsWith('_') && char.IsDigit(c0[0]))
            return true;

         for (int i = 1; i < row.Count; i++)
         {
            if (IsSectionColumnLabel(LiraHtmlParser.CleanText(row[i])))
               return true;
         }
         return false;
      }

      /// <summary>Метка сечения в заголовке столбца: 127-1, 10825-C.</summary>
      static bool IsSectionColumnLabel(string s)
      {
         if (LiraHtmlParser.IsEmptyCell(s)) return false;
         if (s.Contains('(') || s.Contains("ЗАГРУЖ", StringComparison.OrdinalIgnoreCase)
             || s.Contains("Основное", StringComparison.OrdinalIgnoreCase)
             || s.Contains("Особое", StringComparison.OrdinalIgnoreCase))
            return false;
         // element-end: digits, dash, digit or letter
         return System.Text.RegularExpressions.Regex.IsMatch(s, @"^\d+\s*-\s*[\dA-Za-z]+$");
      }

      static bool RegexLikeSection(string s)
      {
         s = LiraHtmlParser.CleanText(s);
         return IsSectionColumnLabel(s);
      }

      static string[] ExtractColumnLabels(IReadOnlyList<string> row)
      {
         var labels = new List<string>();
         for (int i = 1; i < row.Count; i++)
         {
            var lab = LiraHtmlParser.NormalizeSectionLabel(row[i]);
            if (!string.IsNullOrEmpty(lab))
               labels.Add(lab);
         }
         return labels.ToArray();
      }

      static bool IsNodeRow(string c0, IReadOnlyList<string> row)
      {
         if (!LiraHtmlParser.IsEmptyCell(c0)) return false;
         return row.Skip(1).Take(3).All(c => !LiraHtmlParser.IsEmptyCell(c) && int.TryParse(c.Trim(), out _));
      }

      static bool IsBlockHeaderRow(IReadOnlyList<string> row, LiraImportMode mode, out string blockName)
      {
         blockName = "";
         if (row.Count < 2) return false;
         var c0 = LiraHtmlParser.CleanText(row[0]);
         if (!LiraHtmlParser.IsEmptyCell(c0) && !c0.EndsWith('_')) return false;

         string text = row.Count > 1 ? LiraHtmlParser.CleanText(row[1]) : "";
         if (string.IsNullOrWhiteSpace(text))
         {
            foreach (var c in row.Skip(1))
            {
               text = LiraHtmlParser.CleanText(c);
               if (!string.IsNullOrWhiteSpace(text) && text.Length > 3) break;
            }
         }

         if (mode == LiraImportMode.LoadCases)
         {
            if (text.Contains("ЗАГРУЖЕНИЕ", StringComparison.OrdinalIgnoreCase)
                || text.Contains("СОБСТВ", StringComparison.OrdinalIgnoreCase)
                || text.Contains("НАГРУЗ", StringComparison.OrdinalIgnoreCase)
                || text.Contains("СНЕГ", StringComparison.OrdinalIgnoreCase)
                || text.Contains("ДАВЛЕН", StringComparison.OrdinalIgnoreCase)
                || text.Contains("ПЕРЕГОР", StringComparison.OrdinalIgnoreCase)
                || RegexLikeLoadBlock(text))
            {
               blockName = text;
               return true;
            }
         }
         else if (mode == LiraImportMode.Rsn)
         {
            if (text.Contains('(') && text.Contains('-') || text.Contains("Основное", StringComparison.OrdinalIgnoreCase) || text.Contains("Особое", StringComparison.OrdinalIgnoreCase))
            {
               blockName = text;
               return true;
            }
         }
         return false;
      }

      static bool RegexLikeLoadBlock(string text)
         => char.IsDigit(text.FirstOrDefault()) && text.Contains('-');
   }
}
