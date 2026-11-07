using CScore.Fem.Imperfections;
using Microsoft.Data.Sqlite;

namespace OpenCS.Utilites
{
   /// <summary>
   /// Элементы несовершенства загружений «Несовершенство» (БД v99, <c>fem_imperfections</c>). Удаление загружения или
   /// схемы и пересохранение схемы (новые Id загружений) обрабатывают их явно: ON DELETE CASCADE при
   /// PRAGMA foreign_keys=OFF не выполняется.
   /// </summary>
   public partial class DatabaseService
   {
      const string FemImperfectionDdl = """
         CREATE TABLE IF NOT EXISTS fem_imperfections (
             id INTEGER PRIMARY KEY AUTOINCREMENT,
             schema_id INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
             load_case_id INTEGER NOT NULL REFERENCES fem_load_cases(id) ON DELETE CASCADE,
             kind TEXT NOT NULL,
             tag TEXT NOT NULL DEFAULT '',
             params_json TEXT NOT NULL DEFAULT '{}',
             comment TEXT
         );
         CREATE INDEX IF NOT EXISTS idx_fem_imperfections_schema ON fem_imperfections(schema_id, load_case_id);
         """;

      /// <summary>Миграция v99: элементы несовершенства загружений «Несовершенство».</summary>
      void MigrateV99() => MigExec(FemImperfectionDdl);

      /// <summary>Элементы несовершенства схемы по порядку Id.</summary>
      public List<FemImperfection> GetFemImperfections(int schemaId)
      {
         var result = new List<FemImperfection>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT id, load_case_id, kind, tag, params_json, comment
            FROM fem_imperfections WHERE schema_id=@sid ORDER BY id
            """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var r = cmd.ExecuteReader();
         while (r.Read())
            result.Add(new FemImperfection
            {
               Id = r.GetInt32(0), SchemaId = schemaId, LoadCaseId = r.GetInt32(1), Kind = r.GetString(2),
               Tag = r.GetString(3), ParamsJson = r.GetString(4), Comment = r.IsDBNull(5) ? null : r.GetString(5),
            });
         return result;
      }

      /// <summary>Добавляет (Id = 0) или обновляет элемент несовершенства.</summary>
      public void SaveFemImperfection(FemImperfection item)
      {
         ArgumentNullException.ThrowIfNull(item);
         using var cmd = _connection.CreateCommand();
         if (item.Id == 0)
         {
            cmd.CommandText = """
               INSERT INTO fem_imperfections (schema_id, load_case_id, kind, tag, params_json, comment)
               VALUES (@sid, @lc, @kind, @tag, @params, @comment);
               SELECT last_insert_rowid();
               """;
            AddFemImperfectionParameters(cmd, item, item.SchemaId, item.LoadCaseId);
            item.Id = (int)(long)cmd.ExecuteScalar()!;
         }
         else
         {
            cmd.CommandText = """
               UPDATE fem_imperfections SET load_case_id=@lc, kind=@kind, tag=@tag, params_json=@params, comment=@comment
               WHERE id=@id AND schema_id=@sid
               """;
            AddFemImperfectionParameters(cmd, item, item.SchemaId, item.LoadCaseId);
            cmd.Parameters.AddWithValue("@id", item.Id);
            cmd.ExecuteNonQuery();
         }
      }

      public void DeleteFemImperfection(FemImperfection item)
      {
         if (item.Id == 0) return;
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "DELETE FROM fem_imperfections WHERE id=@id";
         cmd.Parameters.AddWithValue("@id", item.Id);
         cmd.ExecuteNonQuery();
      }

      static void AddFemImperfectionParameters(SqliteCommand cmd, FemImperfection item, int schemaId, int loadCaseId)
      {
         cmd.Parameters.AddWithValue("@sid", schemaId);
         cmd.Parameters.AddWithValue("@lc", loadCaseId);
         cmd.Parameters.AddWithValue("@kind", item.Kind);
         cmd.Parameters.AddWithValue("@tag", item.Tag ?? "");
         cmd.Parameters.AddWithValue("@params", string.IsNullOrWhiteSpace(item.ParamsJson) ? "{}" : item.ParamsJson);
         cmd.Parameters.AddWithValue("@comment", (object?)item.Comment ?? DBNull.Value);
      }

      /// <summary>Копии элементов несовершенства в схему <paramref name="schemaId"/> с новыми Id загружений; элементы
      /// загружений вне карты не копируются (в текущей транзакции).</summary>
      void CopyFemImperfectionsCore(IEnumerable<FemImperfection> items, int schemaId, IReadOnlyDictionary<int, int> newLoadCaseIdByOld)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            INSERT INTO fem_imperfections (schema_id, load_case_id, kind, tag, params_json, comment)
            VALUES (@sid, @lc, @kind, @tag, @params, @comment)
            """;
         foreach (var item in items)
         {
            if (!newLoadCaseIdByOld.TryGetValue(item.LoadCaseId, out int lc)) continue;
            cmd.Parameters.Clear();
            AddFemImperfectionParameters(cmd, item, schemaId, lc);
            cmd.ExecuteNonQuery();
         }
      }

      /// <summary>
      /// После пересохранения загружений схемы (новые Id): переносит элементы несовершенства на новые Id и удаляет
      /// элементы загружений, которых больше нет (в текущей транзакции). Новые Id AUTOINCREMENT больше всех прежних —
      /// цепочек переноса нет.
      /// </summary>
      void RemapFemImperfectionsCore(int schemaId, IReadOnlyDictionary<int, int> newLoadCaseIdByOld)
      {
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = "UPDATE fem_imperfections SET load_case_id=@new WHERE schema_id=@sid AND load_case_id=@old";
            cmd.Parameters.AddWithValue("@sid", schemaId);
            var oldParam = cmd.Parameters.Add("@old", SqliteType.Integer);
            var newParam = cmd.Parameters.Add("@new", SqliteType.Integer);
            foreach (var (oldId, newId) in newLoadCaseIdByOld)
            {
               if (oldId == newId) continue;
               oldParam.Value = oldId;
               newParam.Value = newId;
               cmd.ExecuteNonQuery();
            }
         }
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = """
               DELETE FROM fem_imperfections WHERE schema_id=@sid
                 AND load_case_id NOT IN (SELECT id FROM fem_load_cases WHERE schema_id=@sid)
               """;
            cmd.Parameters.AddWithValue("@sid", schemaId);
            cmd.ExecuteNonQuery();
         }
      }
   }
}
