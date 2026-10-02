using CScore;
using CScore.Fem;

namespace OpenCS.Utilites
{
   /// <summary>
   /// Строки результатов проверок по КЭ. На РСУ SCAD их миллионы: в <c>calc_results.data_json</c> они
   /// занимали сотни мегабайт и читались целиком при каждом открытии результата, поэтому хранятся отдельной
   /// таблицей. Повторяющиеся строки (тег набора, тип расчёта, формула, описание, сечение, армирование)
   /// заменены номерами из словаря результата <c>fem_check_row_strings</c>.
   /// </summary>
   public partial class DatabaseService
   {
      /// <summary>Сортировка «сначала худшие»: не прошедшие без коэффициента, затем по убыванию Кисп,
      /// непроверенные — в конце (как в таблице строк результата).</summary>
      const string WorstFirstOrder = """
         CASE WHEN flags = 0 AND utilization IS NULL THEN 0 WHEN (flags & 2) <> 0 THEN 2 ELSE 1 END,
         utilization DESC, idx
         """;

      /// <summary>Флаг строки: прошла проверку.</summary>
      const int RowPassed = 1;
      /// <summary>Флаг строки: не проверена.</summary>
      const int RowNotChecked = 2;

      /// <summary>Таблицы строк результатов проверок по КЭ; строки удаляются вместе с calc_results триггером.</summary>
      void EnsureFemCheckRowTables() => MigExec("""
         CREATE TABLE IF NOT EXISTS fem_check_rows (
             result_id     INTEGER NOT NULL,
             idx           INTEGER NOT NULL,
             elem_num      INTEGER,
             section_num   INTEGER,
             utilization   REAL,
             flags         INTEGER NOT NULL DEFAULT 0,
             label         TEXT NOT NULL DEFAULT '',
             set_tag       INTEGER NOT NULL DEFAULT 0,
             calc_type     INTEGER NOT NULL DEFAULT 0,
             formula       INTEGER NOT NULL DEFAULT 0,
             description   INTEGER NOT NULL DEFAULT 0,
             section_label INTEGER NOT NULL DEFAULT 0,
             rebar_key     INTEGER NOT NULL DEFAULT 0,
             rebar_source  INTEGER NOT NULL DEFAULT 0,
             PRIMARY KEY (result_id, idx)
         ) WITHOUT ROWID;
         CREATE INDEX IF NOT EXISTS idx_fem_check_rows_elem ON fem_check_rows(result_id, elem_num);
         CREATE TABLE IF NOT EXISTS fem_check_row_strings (
             result_id INTEGER NOT NULL,
             id        INTEGER NOT NULL,
             text      TEXT NOT NULL,
             PRIMARY KEY (result_id, id)
         ) WITHOUT ROWID;
         CREATE TRIGGER IF NOT EXISTS trg_calc_results_fem_check_rows AFTER DELETE ON calc_results
         BEGIN
             DELETE FROM fem_check_rows        WHERE result_id = OLD.id;
             DELETE FROM fem_check_row_strings WHERE result_id = OLD.id;
         END;
         """);

      /// <summary>Записывает строки результата (внутри текущей транзакции).</summary>
      void InsertFemCheckRows(int resultId, IReadOnlyList<FemCheckRow> rows)
      {
         var strings = new Dictionary<string, int>(StringComparer.Ordinal) { [""] = 0 };
         int Str(string? s)
         {
            s ??= "";
            if (!strings.TryGetValue(s, out int id)) strings[s] = id = strings.Count;
            return id;
         }

         using (var insert = _connection.CreateCommand())
         {
            insert.CommandText = """
               INSERT INTO fem_check_rows (result_id, idx, elem_num, section_num, utilization, flags, label,
                  set_tag, calc_type, formula, description, section_label, rebar_key, rebar_source)
               VALUES (@rid, @idx, @elem, @sec, @util, @flags, @label, @tag, @ct, @f, @d, @sl, @rk, @rs)
            """;
            var p = new[] { "@rid", "@idx", "@elem", "@sec", "@util", "@flags", "@label",
                            "@tag", "@ct", "@f", "@d", "@sl", "@rk", "@rs" }
               .Select(n => insert.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter { ParameterName = n }))
               .ToArray();
            insert.Prepare();
            p[0].Value = resultId;
            for (int i = 0; i < rows.Count; i++)
            {
               var r = rows[i];
               p[1].Value  = i;
               p[2].Value  = (object?)r.ElemNum ?? DBNull.Value;
               p[3].Value  = (object?)r.SectionNum ?? DBNull.Value;
               p[4].Value  = double.IsFinite(r.Utilization) ? Math.Round(r.Utilization, 6) : DBNull.Value;
               p[5].Value  = (r.Passed ? RowPassed : 0) | (r.NotChecked ? RowNotChecked : 0);
               p[6].Value  = r.Label ?? "";
               p[7].Value  = Str(r.ForceSetTag);
               p[8].Value  = Str(r.CalcType);
               p[9].Value  = Str(r.WorstFormula);
               p[10].Value = Str(r.WorstDescription);
               p[11].Value = Str(r.SectionLabel);
               p[12].Value = Str(r.RebarKey);
               p[13].Value = Str(r.RebarSource);
               insert.ExecuteNonQuery();
            }
         }

         using var insertStr = _connection.CreateCommand();
         insertStr.CommandText = "INSERT INTO fem_check_row_strings (result_id, id, text) VALUES (@rid, @id, @text)";
         insertStr.Parameters.AddWithValue("@rid", resultId);
         var pi = insertStr.Parameters.Add("@id", Microsoft.Data.Sqlite.SqliteType.Integer);
         var pt = insertStr.Parameters.Add("@text", Microsoft.Data.Sqlite.SqliteType.Text);
         foreach (var (text, id) in strings)
         {
            pi.Value = id;
            pt.Value = text;
            insertStr.ExecuteNonQuery();
         }
      }

      /// <summary>Словарь строк результата: номер → текст.</summary>
      string[] LoadFemCheckRowStrings(int resultId)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT id, text FROM fem_check_row_strings WHERE result_id=@rid";
         cmd.Parameters.AddWithValue("@rid", resultId);
         var map = new Dictionary<int, string>();
         using (var r = cmd.ExecuteReader())
            while (r.Read()) map[r.GetInt32(0)] = r.GetString(1);
         var result = new string[map.Count == 0 ? 1 : map.Keys.Max() + 1];
         Array.Fill(result, "");
         foreach (var (id, text) in map) result[id] = text;
         return result;
      }

      /// <summary>Число строк результата, сохранённых отдельно от <c>data_json</c>.</summary>
      public int CountFemCheckRows(int resultId, int? elemNum = null, bool onlyFailed = false)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT COUNT(*) FROM fem_check_rows WHERE result_id=@rid" + RowFilter(elemNum, onlyFailed);
         cmd.Parameters.AddWithValue("@rid", resultId);
         if (elemNum != null) cmd.Parameters.AddWithValue("@elem", elemNum.Value);
         return Convert.ToInt32(cmd.ExecuteScalar());
      }

      /// <summary>
      /// Строки результата в порядке «сначала худшие»; <paramref name="elemNum"/> — только строки КЭ,
      /// <paramref name="onlyFailed"/> — без прошедших, <paramref name="limit"/> — не больше стольких строк.
      /// </summary>
      public List<FemCheckRow> GetFemCheckRows(int resultId, int? elemNum = null, bool onlyFailed = false, int limit = int.MaxValue)
      {
         var strings = LoadFemCheckRowStrings(resultId);
         string S(int id) => id >= 0 && id < strings.Length ? strings[id] : "";

         using var cmd = _connection.CreateCommand();
         cmd.CommandText = $"""
            SELECT elem_num, section_num, utilization, flags, label, set_tag, calc_type, formula, description,
                   section_label, rebar_key, rebar_source
            FROM fem_check_rows WHERE result_id=@rid{RowFilter(elemNum, onlyFailed)}
            ORDER BY {WorstFirstOrder}
            LIMIT @limit
            """;
         cmd.Parameters.AddWithValue("@rid", resultId);
         cmd.Parameters.AddWithValue("@limit", limit);
         if (elemNum != null) cmd.Parameters.AddWithValue("@elem", elemNum.Value);

         var rows = new List<FemCheckRow>();
         using var r = cmd.ExecuteReader();
         while (r.Read())
         {
            int flags = r.GetInt32(3);
            rows.Add(new FemCheckRow
            {
               ElemNum          = r.IsDBNull(0) ? null : r.GetInt32(0),
               SectionNum       = r.IsDBNull(1) ? null : r.GetInt32(1),
               Utilization      = r.IsDBNull(2) ? double.NaN : r.GetDouble(2),
               Passed           = (flags & RowPassed) != 0,
               NotChecked       = (flags & RowNotChecked) != 0,
               Label            = r.GetString(4),
               ForceSetTag      = S(r.GetInt32(5)),
               CalcType         = S(r.GetInt32(6)),
               WorstFormula     = S(r.GetInt32(7)),
               WorstDescription = S(r.GetInt32(8)),
               SectionLabel     = S(r.GetInt32(9)),
               RebarKey         = S(r.GetInt32(10)),
               RebarSource      = S(r.GetInt32(11)),
            });
         }
         return rows;
      }

      static string RowFilter(int? elemNum, bool onlyFailed) =>
         (elemNum != null ? " AND elem_num=@elem" : "") + (onlyFailed ? $" AND (flags & {RowPassed}) = 0" : "");

      /// <summary>
      /// Строки с номером КЭ последнего результата проверки — для эпюр и мозаик Кисп. Читает и отдельную
      /// таблицу, и раздел <c>rows</c> в <c>data_json</c> результатов, сохранённых до её появления.
      /// </summary>
      public IReadOnlyList<FemCheckRowResult> GetFemCheckRowResults(int femCheckId)
      {
         var result = GetCalcResultByFemCheck(femCheckId);
         if (result == null) return [];
         if (!FemCheckElementResults.RowsStored(result.DataJson))
            return FemCheckElementResults.ParseRows(result.DataJson);

         var strings = LoadFemCheckRowStrings(result.Id);
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT elem_num, section_num, utilization, flags, rebar_source
            FROM fem_check_rows WHERE result_id=@rid AND elem_num IS NOT NULL ORDER BY idx
            """;
         cmd.Parameters.AddWithValue("@rid", result.Id);
         var rows = new List<FemCheckRowResult>();
         using var r = cmd.ExecuteReader();
         while (r.Read())
         {
            int flags = r.GetInt32(3);
            int source = r.GetInt32(4);
            rows.Add(new FemCheckRowResult(r.GetInt32(0), r.IsDBNull(1) ? null : r.GetInt32(1),
               source >= 0 && source < strings.Length ? strings[source] : "",
               r.IsDBNull(2) ? null : r.GetDouble(2), (flags & RowPassed) != 0, (flags & RowNotChecked) != 0));
         }
         return rows;
      }
   }
}
