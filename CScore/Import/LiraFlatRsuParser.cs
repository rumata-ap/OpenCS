namespace CScore.Import
{
   /// <summary>Разбор плоской таблицы РСУ (ЭЛМ/НС/УЗЛ/KRT/…).</summary>
   internal static class LiraFlatRsuParser
   {
      public static (List<(string Label, LiraElementKind Kind, Dictionary<string, double> Forces)> Rows, string? Error)
         Parse(IReadOnlyList<IReadOnlyList<IReadOnlyList<string>>> tableRowsList)
      {
         var output = new List<(string, LiraElementKind, Dictionary<string, double>)>();
         LiraElementKind? fileKind = null;

         foreach (var rows in tableRowsList)
         {
            if (!TryGetHeader(rows, out var header))
               continue;

            var headerForceKeys = header.Skip(7).Select(h => LiraForceKeys.Normalize(h)).ToList();
            if (headerForceKeys.Count > 0 && headerForceKeys.All(k =>
                   LiraForceKeys.IsIgnored(k) || string.IsNullOrEmpty(k)))
               continue;

            var kind = LiraForceKeys.DetectKind(headerForceKeys);
            if (kind == null)
               return ([], "Файл содержит смешанные стержневые и оболочечные таблицы. Экспортируйте их отдельно.");
            if (kind == LiraElementKind.Unknown) continue;

            if (fileKind == null) fileKind = kind;
            else if (fileKind != kind)
               return ([], "Файл содержит смешанные стержневые и оболочечные таблицы. Экспортируйте их отдельно.");

            int iElm = IndexOf(header, "ЭЛМ");
            int iNs  = IndexOf(header, "НС");
            int iUzl = IndexOf(header, "УЗЛ");
            var forceCols = header.Select((h, i) => (Key: LiraForceKeys.Normalize(h), i))
               .Where(t => t.i >= 7 && LiraForceKeys.IsForceRow(t.Key))
               .ToList();

            string lastElm = "";
            bool afterHeader = false;
            foreach (var row in rows)
            {
               if (!afterHeader)
               {
                  if (row.Count > 0 && LiraHtmlParser.CleanText(row[0]).Equals("ЭЛМ", StringComparison.OrdinalIgnoreCase))
                     afterHeader = true;
                  continue;
               }
               if (row.Count < header.Count) continue;
               if (!string.IsNullOrWhiteSpace(row[iElm]))
                  lastElm = LiraHtmlParser.CleanText(row[iElm]);

               if (string.IsNullOrWhiteSpace(lastElm)) continue;

               string label = BuildLabel(lastElm, row, iNs, iUzl);
               if (string.IsNullOrEmpty(label)) continue;

               var forces = new Dictionary<string, double>(StringComparer.Ordinal);
               foreach (var (key, idx) in forceCols)
               {
                  if (idx < row.Count)
                     forces[key] = LiraHtmlParser.ParseDouble(row[idx]);
               }
               if (forces.Count > 0)
                  output.Add((label, kind.Value, forces));
            }
         }

         if (output.Count == 0)
            return ([], "Не найдена таблица РСУ в ожидаемом формате.");
         return (output, null);
      }

      static bool TryGetHeader(IReadOnlyList<IReadOnlyList<string>> rows, out IReadOnlyList<string> header)
      {
         header = Array.Empty<string>();
         foreach (var row in rows)
         {
            if (row.Count > 0 && LiraHtmlParser.CleanText(row[0]).Equals("ЭЛМ", StringComparison.OrdinalIgnoreCase))
            {
               header = row;
               return true;
            }
         }
         return false;
      }

      static int IndexOf(IReadOnlyList<string> header, string name)
      {
         for (int i = 0; i < header.Count; i++)
            if (LiraHtmlParser.CleanText(header[i]).Equals(name, StringComparison.OrdinalIgnoreCase))
               return i;
         return 0;
      }

      static string BuildLabel(string elm, IReadOnlyList<string> row, int iNs, int iUzl)
      {
         string uzl = iUzl < row.Count ? LiraHtmlParser.CleanText(row[iUzl]) : "";
         if (!LiraHtmlParser.IsEmptyCell(uzl))
            return LiraHtmlParser.NormalizeSectionLabel($"{elm}-{uzl}");

         string ns = iNs < row.Count ? LiraHtmlParser.CleanText(row[iNs]) : "";
         if (!LiraHtmlParser.IsEmptyCell(ns))
            return LiraHtmlParser.NormalizeSectionLabel($"{elm}-{ns}");
         return LiraHtmlParser.NormalizeSectionLabel(elm);
      }
   }
}
