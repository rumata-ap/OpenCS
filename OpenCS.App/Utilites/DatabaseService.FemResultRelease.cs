using System.Globalization;

namespace OpenCS.Utilites
{
   /// <summary>
   /// Удаление результатов постановок МКЭ, на которые больше никто не ссылается (сброс, новый расчёт, удаление
   /// постановки). Внешние ключи на соединении выключены, поэтому прежний <c>calc_results</c> (с полями шагов
   /// <c>fem_result_steps</c> по триггеру) без явного удаления оставался в базе навсегда. Результат, на который
   /// ссылается извлечение субмодели (<c>submodel_extractions.parent_result_id</c>), остаётся: субмодель читает
   /// по нему данные родителя; он удаляется вместе со схемой субмодели.
   /// </summary>
   public partial class DatabaseService
   {
      /// <summary>
      /// Условие «результат постановки МКЭ, ни на что не привязанный»: не задача (task_id = 0), не проверка, вид
      /// <c>fem_*</c>, на него не ссылаются постановки, проверки и извлечения субмоделей.
      /// </summary>
      const string ReleasedFemResultCondition = """
         task_id = 0 AND fem_check_id IS NULL AND task_kind LIKE 'fem%'
         AND id NOT IN (SELECT result_id FROM fem_analyses WHERE result_id IS NOT NULL)
         AND id NOT IN (SELECT result_id FROM fem_checks WHERE result_id IS NOT NULL)
         AND id NOT IN (SELECT parent_result_id FROM submodel_extractions)
         """;

      /// <summary>
      /// Удаляет из <paramref name="ids"/> результаты постановок, на которые больше никто не ссылается (внутри текущей
      /// транзакции); возвращает удалённые Id — их после commit убрать из кэша (<see cref="ForgetCalcResults"/>).
      /// </summary>
      List<int> DeleteReleasedFemResults(IEnumerable<int> ids)
      {
         var list = ids.Distinct().ToList();
         var removed = new List<int>();
         if (list.Count == 0) return removed;
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = $"""
            DELETE FROM calc_results
            WHERE id IN ({string.Join(",", list.Select(i => i.ToString(CultureInfo.InvariantCulture)))})
              AND {ReleasedFemResultCondition}
            RETURNING id
            """;
         using var r = cmd.ExecuteReader();
         while (r.Read()) removed.Add(r.GetInt32(0));
         return removed;
      }

      /// <summary>Убирает удалённые результаты из кэша процесса (после commit).</summary>
      void ForgetCalcResults(IReadOnlyCollection<int> ids)
      {
         if (ids.Count == 0) return;
         var set = ids.ToHashSet();
         foreach (var r in CalcResults.Where(r => set.Contains(r.Id)).ToList()) CalcResults.Remove(r);
      }

      /// <summary>Заголовок строки <c>calc_results</c> без данных — для списка результатов консоли.</summary>
      public sealed record CalcResultHeader(int Id, int TaskId, string TaskKind, string TaskTag, string Created, string Status,
         int? FemCheckId);

      /// <summary>Заголовки всех результатов проекта (задачи, постановки и проверки МКЭ) по возрастанию Id.</summary>
      public List<CalcResultHeader> GetCalcResultHeaders()
      {
         var rows = new List<CalcResultHeader>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT id, task_id, task_kind, task_tag, created, status, fem_check_id FROM calc_results ORDER BY id";
         using var r = cmd.ExecuteReader();
         while (r.Read())
            rows.Add(new(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5),
               r.IsDBNull(6) ? null : r.GetInt32(6)));
         return rows;
      }

      /// <summary>Id результатов родителя, удержанных извлечениями субмоделей → имя схемы-субмодели.</summary>
      public Dictionary<int, string> SubmodelPinnedResults()
      {
         var map = new Dictionary<int, string>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT e.parent_result_id, s.tag FROM submodel_extractions e JOIN fem_schemas s ON s.id = e.submodel_schema_id
            """;
         using var r = cmd.ExecuteReader();
         while (r.Read()) map.TryAdd(r.GetInt32(0), r.IsDBNull(1) ? "" : r.GetString(1));
         return map;
      }

      /// <summary>Имена схем-субмоделей, извлечённых из результата постановки (для вопроса её удаления).</summary>
      public List<string> SubmodelTagsOfParentAnalysis(int analysisId)
      {
         var tags = new List<string>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT s.tag FROM submodel_extractions e JOIN fem_schemas s ON s.id = e.submodel_schema_id
            WHERE e.parent_analysis_id = @id ORDER BY s.tag
            """;
         cmd.Parameters.AddWithValue("@id", analysisId);
         using var r = cmd.ExecuteReader();
         while (r.Read()) tags.Add(r.IsDBNull(0) ? "" : r.GetString(0));
         return tags;
      }

      /// <summary>Id результата постановки в базе (до записи изменений); null — нет строки или результата.</summary>
      int? StoredFemAnalysisResultId(int analysisId)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT result_id FROM fem_analyses WHERE id=@id";
         cmd.Parameters.AddWithValue("@id", analysisId);
         return cmd.ExecuteScalar() is long v ? (int)v : null;
      }

      /// <summary>
      /// Миграция v103: однократно удаляет накопленные осиротевшие результаты постановок (сброс и повторный расчёт
      /// раньше оставляли прежний результат). Сообщение — число удалённых строк и их объём.
      /// </summary>
      void MigrateV103()
      {
         if (!TableExists("calc_results") || !TableExists("fem_analyses") || !TableExists("fem_checks")) return;
         if (!TableExists("submodel_extractions")) EnsureSubmodelExtractionTables();
         long bytes;
         using (var size = _connection.CreateCommand())
         {
            size.CommandText = $"""
               SELECT COALESCE(SUM(LENGTH(data_json)), 0)
                    + COALESCE((SELECT SUM(LENGTH(data)) FROM fem_result_steps
                                WHERE result_id IN (SELECT id FROM calc_results WHERE {ReleasedFemResultCondition})), 0)
               FROM calc_results WHERE {ReleasedFemResultCondition}
               """;
            if (!TableExists("fem_result_steps"))
               size.CommandText = $"SELECT COALESCE(SUM(LENGTH(data_json)), 0) FROM calc_results WHERE {ReleasedFemResultCondition}";
            bytes = Convert.ToInt64(size.ExecuteScalar(), CultureInfo.InvariantCulture);
         }
         int removed;
         using (var del = _connection.CreateCommand())
         {
            del.CommandText = $"DELETE FROM calc_results WHERE {ReleasedFemResultCondition}";
            removed = del.ExecuteNonQuery();
         }
         if (removed > 0)
            _migrationNotices.Add(string.Format(CultureInfo.CurrentCulture, Loc.S("FemOrphanResultsRemovedNotice"),
               removed, (bytes / 1048576.0).ToString("0.#", CultureInfo.CurrentCulture)));
      }
   }
}
