using System.IO;

namespace CScore.Import
{
   /// <summary>Импорт усилий из HTML-отчётов LIRA SAPR.</summary>
   public static class LiraImporter
   {
      public static LiraImportResult ImportFile(string path, LiraImportMode mode, LiraImportOptions? options = null)
      {
         options ??= LiraImportOptions.Default;
         var result = new LiraImportResult();

         try
         {
            var html = LiraHtmlParser.ReadHtml(path);
            return ImportHtml(html, mode, options, Path.GetFileNameWithoutExtension(path));
         }
         catch (Exception ex)
         {
            result.Error = ex.Message;
            return result;
         }
      }

      public static LiraImportResult ImportHtml(string html, LiraImportMode mode, LiraImportOptions options, string? sourceName = null)
      {
         var result = new LiraImportResult();
         var preLines = LiraHtmlParser.ExtractPreBlocks(html)
            .SelectMany(p => p.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();
         var units = LiraUnitScales.FromPreLines(preLines, options.TonToKnFactor);
         var scheme = LiraHtmlParser.ExtractSchemeName(LiraHtmlParser.ExtractPreBlocks(html)) ?? sourceName ?? "LIRA";

         var tables = LiraHtmlParser.ExtractTables(html);
         var tableRows = tables.Select(t => t.Rows).ToList();

         if (mode == LiraImportMode.Rsu)
            return ImportRsu(result, tableRows, units, options, scheme);

         var (blocks, err) = LiraMatrixParser.Parse(tableRows, mode);
         if (err != null) { result.Error = err; return result; }

         foreach (var (name, kind, columns) in blocks)
         {
            var fs = new ForceSet
            {
               Kind = kind == LiraElementKind.Shell ? "shell" : "bar",
               Tag  = name,
            };
            int num = 1;
            foreach (var (label, forceDict) in columns.OrderBy(c => c.Key, StringComparer.Ordinal))
            {
               if (kind == LiraElementKind.Shell)
               {
                  var item = LiraForceMapper.MapShell(forceDict, units, options);
                  item.Label = label;
                  item.Num   = num++;
                  fs.ShellItems.Add(item);
               }
               else
               {
                  var item = LiraForceMapper.MapBar(forceDict, units, options);
                  item.Label = label;
                  item.Num   = num++;
                  fs.Items.Add(item);
               }
            }
            if (fs.Items.Count > 0 || fs.ShellItems.Count > 0)
               result.ForceSets.Add(fs);
         }

         if (result.ForceSets.Count == 0)
            result.Error = "Не удалось извлечь ни одного набора усилий.";
         return result;
      }

      static LiraImportResult ImportRsu(LiraImportResult result,
         IReadOnlyList<IReadOnlyList<IReadOnlyList<string>>> tableRows,
         LiraUnitScales units, LiraImportOptions options, string scheme)
      {
         var (rows, err) = LiraFlatRsuParser.Parse(tableRows);
         if (err != null) { result.Error = err; return result; }

         var kind = rows[0].Kind;
         var fs = new ForceSet
         {
            Kind = kind == LiraElementKind.Shell ? "shell" : "bar",
            Tag  = $"РСУ: {scheme}",
         };
         int num = 1;
         foreach (var (label, _, forceDict) in rows)
         {
            if (kind == LiraElementKind.Shell)
            {
               var item = LiraForceMapper.MapShell(forceDict, units, options);
               item.Label = label;
               item.Num   = num++;
               fs.ShellItems.Add(item);
            }
            else
            {
               var item = LiraForceMapper.MapBar(forceDict, units, options);
               item.Label = label;
               item.Num   = num++;
               fs.Items.Add(item);
            }
         }
         result.ForceSets.Add(fs);
         return result;
      }
   }
}
