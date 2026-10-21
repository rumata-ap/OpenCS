using CScore.Fem.Fields;

namespace OpenCS.Utilites
{
   /// <summary>Поля из таблиц точек (<see cref="FemField"/>), кэш их значений и ручные правки величин КЭ и узлов.</summary>
   public partial class DatabaseService
   {
      const string FemFieldsDdl = """
         CREATE TABLE IF NOT EXISTS fem_fields (
             id INTEGER PRIMARY KEY AUTOINCREMENT,
             schema_id INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
             name TEXT NOT NULL DEFAULT '',
             quantity TEXT NOT NULL,
             definition_json TEXT NOT NULL,
             points_blob BLOB NOT NULL,
             point_count INTEGER NOT NULL DEFAULT 0,
             has_z INTEGER NOT NULL DEFAULT 0,
             source_file TEXT,
             enabled INTEGER NOT NULL DEFAULT 1,
             sort_order INTEGER NOT NULL DEFAULT 0,
             mesh_signature TEXT
         );
         CREATE INDEX IF NOT EXISTS idx_fem_fields_schema ON fem_fields(schema_id);
         CREATE TABLE IF NOT EXISTS fem_field_values (
             field_id INTEGER NOT NULL REFERENCES fem_fields(id) ON DELETE CASCADE,
             target_kind TEXT NOT NULL,
             target_tag TEXT NOT NULL,
             component TEXT NOT NULL,
             value REAL,
             how TEXT NOT NULL,
             distance REAL NOT NULL DEFAULT 0
         );
         CREATE INDEX IF NOT EXISTS idx_fem_field_values_field ON fem_field_values(field_id);
         CREATE TABLE IF NOT EXISTS fem_param_overrides (
             id INTEGER PRIMARY KEY AUTOINCREMENT,
             schema_id INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
             target_kind TEXT NOT NULL,
             target_tag TEXT NOT NULL,
             quantity TEXT NOT NULL,
             component TEXT NOT NULL,
             value REAL NOT NULL
         );
         CREATE INDEX IF NOT EXISTS idx_fem_param_overrides_schema ON fem_param_overrides(schema_id);
         """;

      /// <summary>Миграция v88: поля из таблиц точек, кэш их значений, ручные правки величин.</summary>
      void MigrateV88() => MigExec(FemFieldsDdl);

      /// <summary>Есть ли у схемы включённые поля или ручные правки — без чтения точек и значений.</summary>
      public bool HasFemFieldLayers(int schemaId)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT EXISTS(SELECT 1 FROM fem_fields WHERE schema_id=@sid AND enabled=1)
                OR EXISTS(SELECT 1 FROM fem_param_overrides WHERE schema_id=@sid)
            """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         return Convert.ToInt64(cmd.ExecuteScalar()) != 0;
      }

      public List<FemField> GetFemFields(int schemaId)
      {
         var result = new List<FemField>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT id, name, quantity, definition_json, points_blob, has_z, source_file, enabled, sort_order, mesh_signature
            FROM fem_fields WHERE schema_id=@sid ORDER BY sort_order, id
            """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var r = cmd.ExecuteReader();
         while (r.Read())
         {
            var def = FemFieldDefinition.FromJson(r.GetString(3));
            result.Add(new FemField
            {
               Id = r.GetInt32(0), SchemaId = schemaId, Name = r.GetString(1), Quantity = r.GetString(2), Definition = def,
               Points = FemFieldPointCodec.Decode((byte[])r[4], def.Columns.Count), HasZ = r.GetInt32(5) != 0,
               SourceFile = r.IsDBNull(6) ? null : r.GetString(6), Enabled = r.GetInt32(7) != 0, SortOrder = r.GetInt32(8),
               MeshSignature = r.IsDBNull(9) ? null : r.GetString(9),
            });
         }
         return result;
      }

      /// <summary>
      /// Вставка (Id = 0: новое поле встаёт последним) или обновление поля. Обновление с
      /// <see cref="FemField.MeshSignature"/> = null сбрасывает кэш значений — его пересчитает <c>FemFieldService</c>.
      /// </summary>
      public void SaveFemField(FemField field)
      {
         using var tx = _connection.BeginTransaction();
         try
         {
            SaveFemFieldCore(field);
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }

      /// <summary>Тело <see cref="SaveFemField"/> внутри внешней транзакции.</summary>
      void SaveFemFieldCore(FemField field)
      {
         using var cmd = _connection.CreateCommand();
         if (field.Id == 0)
         {
            using (var max = _connection.CreateCommand())
            {
               max.CommandText = "SELECT COALESCE(MAX(sort_order), -1) + 1 FROM fem_fields WHERE schema_id=@sid";
               max.Parameters.AddWithValue("@sid", field.SchemaId);
               field.SortOrder = Convert.ToInt32(max.ExecuteScalar());
            }
            cmd.CommandText = """
               INSERT INTO fem_fields (schema_id, name, quantity, definition_json, points_blob, point_count, has_z, source_file,
                                       enabled, sort_order, mesh_signature)
               VALUES (@sid, @name, @q, @def, @pts, @n, @z, @src, @en, @ord, @sig);
               SELECT last_insert_rowid();
               """;
         }
         else
            cmd.CommandText = """
               UPDATE fem_fields SET name=@name, quantity=@q, definition_json=@def, points_blob=@pts, point_count=@n,
                      has_z=@z, source_file=@src, enabled=@en, sort_order=@ord, mesh_signature=@sig WHERE id=@id;
               SELECT @id;
               """;
         cmd.Parameters.AddWithValue("@id", field.Id);
         cmd.Parameters.AddWithValue("@sid", field.SchemaId);
         cmd.Parameters.AddWithValue("@name", field.Name);
         cmd.Parameters.AddWithValue("@q", field.Quantity);
         cmd.Parameters.AddWithValue("@def", field.Definition.ToJson());
         cmd.Parameters.AddWithValue("@pts", FemFieldPointCodec.Encode(field.Points, field.Definition.Columns.Count));
         cmd.Parameters.AddWithValue("@n", field.Points.Count);
         cmd.Parameters.AddWithValue("@z", field.HasZ ? 1 : 0);
         cmd.Parameters.AddWithValue("@src", (object?)field.SourceFile ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@en", field.Enabled ? 1 : 0);
         cmd.Parameters.AddWithValue("@ord", field.SortOrder);
         cmd.Parameters.AddWithValue("@sig", (object?)field.MeshSignature ?? DBNull.Value);
         field.Id = Convert.ToInt32(cmd.ExecuteScalar());
         if (field.MeshSignature == null)
         {
            using var clear = _connection.CreateCommand();
            clear.CommandText = "DELETE FROM fem_field_values WHERE field_id=@id";
            clear.Parameters.AddWithValue("@id", field.Id);
            clear.ExecuteNonQuery();
         }
      }

      public void DeleteFemField(int fieldId)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "DELETE FROM fem_field_values WHERE field_id=@id; DELETE FROM fem_fields WHERE id=@id;";
         cmd.Parameters.AddWithValue("@id", fieldId);
         cmd.ExecuteNonQuery();
      }

      public void SetFemFieldEnabled(int fieldId, bool enabled)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "UPDATE fem_fields SET enabled=@en WHERE id=@id";
         cmd.Parameters.AddWithValue("@id", fieldId);
         cmd.Parameters.AddWithValue("@en", enabled ? 1 : 0);
         cmd.ExecuteNonQuery();
      }

      /// <summary>Порядок полей схемы: sort_order = позиция в списке.</summary>
      public void SetFemFieldOrder(IReadOnlyList<int> fieldIds)
      {
         using var tx = _connection.BeginTransaction();
         try
         {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE fem_fields SET sort_order=@ord WHERE id=@id";
            var id = cmd.Parameters.Add("@id", Microsoft.Data.Sqlite.SqliteType.Integer);
            var ord = cmd.Parameters.Add("@ord", Microsoft.Data.Sqlite.SqliteType.Integer);
            for (int i = 0; i < fieldIds.Count; i++) { id.Value = fieldIds[i]; ord.Value = i; cmd.ExecuteNonQuery(); }
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }

      public List<FemFieldValue> GetFemFieldValues(int fieldId)
      {
         var result = new List<FemFieldValue>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT target_kind, target_tag, component, value, how, distance FROM fem_field_values WHERE field_id=@id ORDER BY rowid";
         cmd.Parameters.AddWithValue("@id", fieldId);
         using var r = cmd.ExecuteReader();
         while (r.Read())
            result.Add(new FemFieldValue(fieldId, Enum.Parse<FemFieldTargetKind>(r.GetString(0)), r.GetString(1), r.GetString(2),
               r.IsDBNull(3) ? null : r.GetDouble(3), Enum.Parse<FieldHow>(r.GetString(4)), r.GetDouble(5)));
         return result;
      }

      /// <summary>Полная замена кэша значений поля и его подписи целей одной транзакцией.</summary>
      public void ReplaceFemFieldValues(int fieldId, IReadOnlyList<FemFieldValue> values, string signature)
      {
         using var tx = _connection.BeginTransaction();
         try
         {
            using (var del = _connection.CreateCommand())
            {
               del.CommandText = "DELETE FROM fem_field_values WHERE field_id=@id; UPDATE fem_fields SET mesh_signature=@sig WHERE id=@id;";
               del.Parameters.AddWithValue("@id", fieldId);
               del.Parameters.AddWithValue("@sig", signature);
               del.ExecuteNonQuery();
            }
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
               INSERT INTO fem_field_values (field_id, target_kind, target_tag, component, value, how, distance)
               VALUES (@id, @kind, @tag, @comp, @v, @how, @d)
               """;
            cmd.Parameters.AddWithValue("@id", fieldId);
            var kind = cmd.Parameters.Add("@kind", Microsoft.Data.Sqlite.SqliteType.Text);
            var tag = cmd.Parameters.Add("@tag", Microsoft.Data.Sqlite.SqliteType.Text);
            var comp = cmd.Parameters.Add("@comp", Microsoft.Data.Sqlite.SqliteType.Text);
            var v = cmd.Parameters.Add("@v", Microsoft.Data.Sqlite.SqliteType.Real);
            var how = cmd.Parameters.Add("@how", Microsoft.Data.Sqlite.SqliteType.Text);
            var d = cmd.Parameters.Add("@d", Microsoft.Data.Sqlite.SqliteType.Real);
            foreach (var x in values)
            {
               kind.Value = x.TargetKind.ToString(); tag.Value = x.TargetTag; comp.Value = x.Component;
               v.Value = (object?)x.Value ?? DBNull.Value; how.Value = x.How.ToString();
               // Нет значения — расстояние бесконечно; в REAL SQLite бесконечность не хранится.
               d.Value = double.IsFinite(x.Distance) ? x.Distance : 1e300;
               cmd.ExecuteNonQuery();
            }
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }

      public List<FemParamOverride> GetFemParamOverrides(int schemaId)
      {
         var result = new List<FemParamOverride>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT target_kind, target_tag, quantity, component, value FROM fem_param_overrides WHERE schema_id=@sid ORDER BY id";
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var r = cmd.ExecuteReader();
         while (r.Read())
            result.Add(new FemParamOverride(Enum.Parse<FemFieldTargetKind>(r.GetString(0)), r.GetString(1), r.GetString(2),
               r.GetString(3), r.GetDouble(4)));
         return result;
      }

      public void ReplaceFemParamOverrides(int schemaId, IReadOnlyList<FemParamOverride> overrides)
      {
         using var tx = _connection.BeginTransaction();
         try
         {
            ReplaceFemParamOverridesCore(schemaId, overrides);
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }

      /// <summary>Тело <see cref="ReplaceFemParamOverrides"/> внутри внешней транзакции.</summary>
      void ReplaceFemParamOverridesCore(int schemaId, IReadOnlyList<FemParamOverride> overrides)
      {
         using (var del = _connection.CreateCommand())
         {
            del.CommandText = "DELETE FROM fem_param_overrides WHERE schema_id=@sid";
            del.Parameters.AddWithValue("@sid", schemaId);
            del.ExecuteNonQuery();
         }
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            INSERT INTO fem_param_overrides (schema_id, target_kind, target_tag, quantity, component, value)
            VALUES (@sid, @kind, @tag, @q, @comp, @v)
            """;
         foreach (var o in overrides)
         {
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@sid", schemaId);
            cmd.Parameters.AddWithValue("@kind", o.TargetKind.ToString());
            cmd.Parameters.AddWithValue("@tag", o.TargetTag);
            cmd.Parameters.AddWithValue("@q", o.Quantity);
            cmd.Parameters.AddWithValue("@comp", o.Component);
            cmd.Parameters.AddWithValue("@v", o.Value);
            cmd.ExecuteNonQuery();
         }
      }

      /// <summary>
      /// Копия полей и правок в другую схему внутри транзакции копии схемы; кэш значений не копируется — пересчитается.
      /// Группы целей переназначаются на копии групп (<paramref name="newGroupIdByOld"/>). Id группы, которой в исходной
      /// схеме нет, остаётся как есть: отбросить его нельзя — опустевшие цели означали бы «вся схема».
      /// </summary>
      void CopyFemFieldsCore(IReadOnlyList<FemField> fields, IReadOnlyList<FemParamOverride> overrides, int toSchemaId,
         IReadOnlyDictionary<int, int> newGroupIdByOld)
      {
         // Новое поле встаёт последним — порядок исходной схемы сохраняется.
         foreach (var f in fields)
         {
            f.Id = 0;
            f.SchemaId = toSchemaId;
            f.MeshSignature = null;
            f.Definition.Targets.GroupIds = f.Definition.Targets.GroupIds
               .Select(g => newGroupIdByOld.TryGetValue(g, out int mapped) ? mapped : g).ToList();
            SaveFemFieldCore(f);
         }
         ReplaceFemParamOverridesCore(toSchemaId, overrides);
      }
   }
}
