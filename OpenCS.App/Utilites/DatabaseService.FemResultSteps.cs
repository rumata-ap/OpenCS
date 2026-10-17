using Microsoft.Data.Sqlite;

namespace OpenCS.Utilites
{
   /// <summary>
   /// Полные поля шагов нелинейного расчёта схемы (CSfea): сжатая запись на шаг, номер шага — сквозной номер в сводке
   /// результата. Удаляются вместе с <c>calc_results</c> триггером.
   /// </summary>
   public partial class DatabaseService
   {
      /// <summary>Миграция v81: таблица полей шагов результата.</summary>
      void EnsureFemResultStepTable() => MigExec("""
         CREATE TABLE IF NOT EXISTS fem_result_steps (
             result_id INTEGER NOT NULL,
             step_n    INTEGER NOT NULL,
             data      BLOB NOT NULL,
             PRIMARY KEY (result_id, step_n)
         ) WITHOUT ROWID;
         CREATE TRIGGER IF NOT EXISTS trg_calc_results_fem_result_steps AFTER DELETE ON calc_results
         BEGIN
             DELETE FROM fem_result_steps WHERE result_id = OLD.id;
         END;
         """);

      /// <summary>Записывает поля шагов результата (прежние записи результата заменяются).</summary>
      public void SaveFemResultSteps(int resultId, IEnumerable<(int StepN, byte[] Data)> steps)
      {
         using var tx = _connection.BeginTransaction();
         using (var del = _connection.CreateCommand())
         {
            del.CommandText = "DELETE FROM fem_result_steps WHERE result_id = @rid";
            del.Parameters.AddWithValue("@rid", resultId);
            del.ExecuteNonQuery();
         }
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = "INSERT INTO fem_result_steps (result_id, step_n, data) VALUES (@rid, @n, @data)";
            var rid = cmd.Parameters.Add("@rid", SqliteType.Integer);
            var n = cmd.Parameters.Add("@n", SqliteType.Integer);
            var data = cmd.Parameters.Add("@data", SqliteType.Blob);
            rid.Value = resultId;
            foreach (var (stepN, bytes) in steps)
            {
               n.Value = stepN;
               data.Value = bytes;
               cmd.ExecuteNonQuery();
            }
         }
         tx.Commit();
      }

      /// <summary>Записывает или заменяет одну запись результата (пакетный расчёт пишет вариант сразу после расчёта).</summary>
      public void SaveFemResultStep(int resultId, int stepN, byte[] data)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "INSERT OR REPLACE INTO fem_result_steps (result_id, step_n, data) VALUES (@rid, @n, @data)";
         cmd.Parameters.AddWithValue("@rid", resultId);
         cmd.Parameters.AddWithValue("@n", stepN);
         cmd.Parameters.Add("@data", SqliteType.Blob).Value = data;
         cmd.ExecuteNonQuery();
      }

      /// <summary>Удаляет записи результата с номерами <paramref name="stepNs"/>.</summary>
      public void DeleteFemResultSteps(int resultId, IEnumerable<int> stepNs)
      {
         using var tx = _connection.BeginTransaction();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "DELETE FROM fem_result_steps WHERE result_id = @rid AND step_n = @n";
         cmd.Parameters.AddWithValue("@rid", resultId);
         var n = cmd.Parameters.Add("@n", SqliteType.Integer);
         foreach (int stepN in stepNs)
         {
            n.Value = stepN;
            cmd.ExecuteNonQuery();
         }
         tx.Commit();
      }

      /// <summary>Номера шагов результата с записанными полями, по возрастанию.</summary>
      public List<int> GetFemResultStepNumbers(int resultId)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT step_n FROM fem_result_steps WHERE result_id = @rid ORDER BY step_n";
         cmd.Parameters.AddWithValue("@rid", resultId);
         var list = new List<int>();
         using var r = cmd.ExecuteReader();
         while (r.Read()) list.Add(r.GetInt32(0));
         return list;
      }

      /// <summary>Сжатые поля шага; null — не записаны.</summary>
      public byte[]? GetFemResultStep(int resultId, int stepN)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT data FROM fem_result_steps WHERE result_id = @rid AND step_n = @n";
         cmd.Parameters.AddWithValue("@rid", resultId);
         cmd.Parameters.AddWithValue("@n", stepN);
         return cmd.ExecuteScalar() as byte[];
      }
   }
}
