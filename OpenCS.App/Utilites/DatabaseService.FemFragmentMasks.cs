using CScore.Fem;
using CScore.Fem.Fragment.Masks;
using Microsoft.Data.Sqlite;

namespace OpenCS.Utilites
{
   /// <summary>Фрагмент этажа, подпроекты C+D (БД v94): набор масок из результата родителя, ГУ разрезов, пресет схемы.
   /// Удаление схемы и перезапись разрезов чистят их явно (PRAGMA foreign_keys=OFF).</summary>
   public partial class DatabaseService
   {
      const string FemFragmentMaskDdl = """
         CREATE TABLE IF NOT EXISTS fem_fragment_masks (
             id INTEGER PRIMARY KEY AUTOINCREMENT,
             schema_id INTEGER NOT NULL UNIQUE REFERENCES fem_schemas(id) ON DELETE CASCADE,
             source_kind TEXT NOT NULL,
             source_ref TEXT NOT NULL DEFAULT '',
             source_label TEXT NOT NULL DEFAULT '',
             source_fingerprint TEXT NOT NULL DEFAULT '',
             created_at TEXT NOT NULL DEFAULT '',
             report_json TEXT NOT NULL DEFAULT '[]'
         );
         CREATE TABLE IF NOT EXISTS fem_fragment_mask_items (
             id INTEGER PRIMARY KEY AUTOINCREMENT,
             mask_id INTEGER NOT NULL REFERENCES fem_fragment_masks(id) ON DELETE CASCADE,
             cut_id INTEGER NOT NULL REFERENCES fem_fragment_cuts(id) ON DELETE CASCADE,
             key_kind TEXT NOT NULL,
             load_case_id INTEGER,
             stage_expression_json TEXT,
             no_displacements INTEGER NOT NULL DEFAULT 0,
             no_forces INTEGER NOT NULL DEFAULT 0,
             data BLOB NOT NULL
         );
         CREATE INDEX IF NOT EXISTS idx_fem_fragment_mask_items_mask ON fem_fragment_mask_items(mask_id);
         CREATE TABLE IF NOT EXISTS fem_fragment_cut_bc (
             cut_id INTEGER PRIMARY KEY REFERENCES fem_fragment_cuts(id) ON DELETE CASCADE,
             modes BLOB NOT NULL
         );
         """;

      /// <summary>Миграция v94: маски фрагмента, ГУ разрезов, пресет ГУ схемы.</summary>
      void MigrateV94()
      {
         MigrateV93();
         MigExec(FemFragmentMaskDdl);
         if (!ColumnExists("fem_schemas", "fragment_bc_preset"))
            MigExec("ALTER TABLE fem_schemas ADD COLUMN fragment_bc_preset TEXT");
      }

      /// <summary>
      /// Миграция v95: у загружения фрагмента — Id загружения родителя, копией которого оно создано (карта масок; у
      /// фрагментов до v95 — null, карта восстанавливается по порядку и тегам).
      /// </summary>
      void MigrateV95()
      {
         MigrateV94();
         if (!ColumnExists("fem_load_cases", "parent_load_case_id"))
            MigExec("ALTER TABLE fem_load_cases ADD COLUMN parent_load_case_id INTEGER");
      }

      /// <summary>
      /// Полное сохранение схемы <paramref name="parentId"/> переписало Id её загружений (<paramref name="newIdByOld"/>):
      /// ссылки загружений её фрагментов идут за ними, ссылки на удалённые загружения сбрасываются (внутри транзакции
      /// сохранения). Объекты в памяти обновляются так же.
      /// </summary>
      void RemapFragmentParentLoadCases(int parentId, IReadOnlyDictionary<int, int> newIdByOld)
      {
         const string Children = "schema_id IN (SELECT id FROM fem_schemas WHERE parent_schema_id=@pid)";
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = $"UPDATE fem_load_cases SET parent_load_case_id=@new WHERE parent_load_case_id=@old AND {Children}";
            cmd.Parameters.AddWithValue("@pid", parentId);
            var oldId = cmd.Parameters.Add("@old", SqliteType.Integer);
            var newId = cmd.Parameters.Add("@new", SqliteType.Integer);
            // Новые Id (AUTOINCREMENT) больше любых прежних, поэтому замены не наложатся одна на другую.
            foreach (var (o, n) in newIdByOld.Where(kv => kv.Key != kv.Value))
            {
               oldId.Value = o;
               newId.Value = n;
               cmd.ExecuteNonQuery();
            }
         }
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = $"""
               UPDATE fem_load_cases SET parent_load_case_id=NULL WHERE parent_load_case_id IS NOT NULL AND {Children}
                 AND parent_load_case_id NOT IN (SELECT id FROM fem_load_cases WHERE schema_id=@pid)
               """;
            cmd.Parameters.AddWithValue("@pid", parentId);
            cmd.ExecuteNonQuery();
         }
         var alive = newIdByOld.Values.ToHashSet();
         foreach (var lc in FemSchemas.Where(s => s.ParentSchemaId == parentId).SelectMany(s => s.LoadCases))
            if (lc.ParentLoadCaseId is int id)
               lc.ParentLoadCaseId = newIdByOld.TryGetValue(id, out int mapped) ? mapped : alive.Contains(id) ? id : null;
      }

      const string LoadCaseKeyKind = "load_case", StageKeyKind = "stage";

      /// <summary>
      /// Заменяет набор масок фрагмента целиком (одной транзакцией). Разрез маски не из этой схемы —
      /// <see cref="InvalidOperationException"/>, прежний набор остаётся.
      /// </summary>
      public void ReplaceFragmentMasks(int schemaId, FragmentMaskSet set)
      {
         ArgumentNullException.ThrowIfNull(set);
         var cuts = GetFragmentCutsWithIds(schemaId).Select(c => c.Id).ToHashSet();
         if (set.Items.FirstOrDefault(i => !cuts.Contains(i.CutId)) is { } foreign)
            throw new InvalidOperationException($"Маска ссылается на разрез {foreign.CutId}, которого нет у схемы {schemaId}.");
         using var tx = _connection.BeginTransaction();
         try
         {
            DeleteFragmentMasksCore(schemaId);
            InsertFragmentMasksCore(schemaId, set);
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }

      /// <summary>Набор масок фрагмента; null — маски не снимались. <paramref name="items"/> = false — только заголовок
      /// (источник, отпечаток, отчёт) без чтения блоков сэмплов.</summary>
      public FragmentMaskSet? GetFragmentMasks(int schemaId, bool items = true)
      {
         int maskId;
         FragmentMaskSet set;
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = """
               SELECT id, source_kind, source_ref, source_label, source_fingerprint, created_at, report_json
               FROM fem_fragment_masks WHERE schema_id=@sid
               """;
            cmd.Parameters.AddWithValue("@sid", schemaId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            maskId = r.GetInt32(0);
            set = new FragmentMaskSet
            {
               SourceKind = r.GetString(1), SourceRef = r.GetString(2), SourceLabel = r.GetString(3),
               SourceFingerprint = r.GetString(4), CreatedAt = r.GetString(5), ReportJson = r.GetString(6),
            };
         }
         if (!items) return set;
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = """
               SELECT cut_id, key_kind, load_case_id, stage_expression_json, data
               FROM fem_fragment_mask_items WHERE mask_id=@mid ORDER BY id
               """;
            cmd.Parameters.AddWithValue("@mid", maskId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
               var key = r.GetString(1) == StageKeyKind
                  ? FragmentMaskKey.OfStage(r.IsDBNull(3) ? "{}" : r.GetString(3))
                  : FragmentMaskKey.OfLoadCase(r.IsDBNull(2) ? 0 : r.GetInt32(2));
               set.Items.Add(new FragmentMaskItem(r.GetInt32(0), key, FragmentMaskCodec.Decode((byte[])r[4])));
            }
         }
         return set;
      }

      /// <summary>Удаляет набор масок фрагмента.</summary>
      public void DeleteFragmentMasks(int schemaId)
      {
         using var tx = _connection.BeginTransaction();
         try
         {
            DeleteFragmentMasksCore(schemaId);
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }

      /// <summary>
      /// Заменяет ГУ разрезов фрагмента: Id разреза → 6 байт режимов DOF (255 — «по пресету»); разрезы без записи — по
      /// пресету.
      /// </summary>
      public void ReplaceFragmentCutBc(int schemaId, IReadOnlyDictionary<int, byte[]> modes)
      {
         ArgumentNullException.ThrowIfNull(modes);
         var cuts = GetFragmentCutsWithIds(schemaId).Select(c => c.Id).ToHashSet();
         if (modes.FirstOrDefault(kv => !cuts.Contains(kv.Key) || kv.Value.Length != 6) is { Value: not null } bad)
            throw new InvalidOperationException($"ГУ разреза {bad.Key}: разреза нет у схемы {schemaId} или не 6 режимов DOF.");
         using var tx = _connection.BeginTransaction();
         try
         {
            using (var del = _connection.CreateCommand())
            {
               del.CommandText = "DELETE FROM fem_fragment_cut_bc WHERE cut_id IN (SELECT id FROM fem_fragment_cuts WHERE schema_id=@sid)";
               del.Parameters.AddWithValue("@sid", schemaId);
               del.ExecuteNonQuery();
            }
            InsertFragmentCutBcCore(modes);
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }

      /// <summary>ГУ разрезов фрагмента: Id разреза → 6 байт режимов DOF.</summary>
      public Dictionary<int, byte[]> GetFragmentCutBc(int schemaId)
      {
         var result = new Dictionary<int, byte[]>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT b.cut_id, b.modes FROM fem_fragment_cut_bc b JOIN fem_fragment_cuts c ON c.id = b.cut_id
            WHERE c.schema_id=@sid ORDER BY b.cut_id
            """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var r = cmd.ExecuteReader();
         while (r.Read()) result[r.GetInt32(0)] = (byte[])r[1];
         return result;
      }

      /// <summary>Записывает пресет ГУ разрезов схемы (null — по умолчанию) и обновляет объект.</summary>
      public void SetFragmentBcPreset(FemSchema schema, string? preset)
      {
         ArgumentNullException.ThrowIfNull(schema);
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "UPDATE fem_schemas SET fragment_bc_preset=@p WHERE id=@id";
         cmd.Parameters.AddWithValue("@p", (object?)preset ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@id", schema.Id);
         cmd.ExecuteNonQuery();
         schema.FragmentBcPreset = preset;
      }

      void DeleteFragmentMasksCore(int schemaId)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            DELETE FROM fem_fragment_mask_items WHERE mask_id IN (SELECT id FROM fem_fragment_masks WHERE schema_id=@sid);
            DELETE FROM fem_fragment_masks WHERE schema_id=@sid;
            """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         cmd.ExecuteNonQuery();
      }

      void InsertFragmentMasksCore(int schemaId, FragmentMaskSet set)
      {
         int maskId;
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = """
               INSERT INTO fem_fragment_masks (schema_id, source_kind, source_ref, source_label, source_fingerprint, created_at, report_json)
               VALUES (@sid, @kind, @ref, @label, @fp, @created, @report);
               SELECT last_insert_rowid();
               """;
            cmd.Parameters.AddWithValue("@sid", schemaId);
            cmd.Parameters.AddWithValue("@kind", set.SourceKind);
            cmd.Parameters.AddWithValue("@ref", set.SourceRef);
            cmd.Parameters.AddWithValue("@label", set.SourceLabel);
            cmd.Parameters.AddWithValue("@fp", set.SourceFingerprint);
            cmd.Parameters.AddWithValue("@created", set.CreatedAt);
            cmd.Parameters.AddWithValue("@report", set.ReportJson);
            maskId = (int)(long)cmd.ExecuteScalar()!;
         }
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = """
               INSERT INTO fem_fragment_mask_items
                  (mask_id, cut_id, key_kind, load_case_id, stage_expression_json, no_displacements, no_forces, data)
               VALUES (@mid, @cut, @kind, @lc, @stage, @nou, @nof, @data)
               """;
            cmd.Parameters.AddWithValue("@mid", maskId);
            var cut = cmd.Parameters.Add("@cut", SqliteType.Integer);
            var kind = cmd.Parameters.Add("@kind", SqliteType.Text);
            var lc = cmd.Parameters.Add("@lc", SqliteType.Integer);
            var stage = cmd.Parameters.Add("@stage", SqliteType.Text);
            var nou = cmd.Parameters.Add("@nou", SqliteType.Integer);
            var nof = cmd.Parameters.Add("@nof", SqliteType.Integer);
            var data = cmd.Parameters.Add("@data", SqliteType.Blob);
            foreach (var item in set.Items)
            {
               bool isStage = item.Key.Kind == FragmentMaskKeyKind.Stage;
               cut.Value = item.CutId;
               kind.Value = isStage ? StageKeyKind : LoadCaseKeyKind;
               lc.Value = isStage ? DBNull.Value : item.Key.LoadCaseId;
               stage.Value = isStage ? (object?)item.Key.StageExpressionJson ?? DBNull.Value : DBNull.Value;
               nou.Value = item.NoDisplacements ? 1 : 0;
               nof.Value = item.NoForces ? 1 : 0;
               data.Value = FragmentMaskCodec.Encode(item.Samples);
               cmd.ExecuteNonQuery();
            }
         }
      }

      void InsertFragmentCutBcCore(IReadOnlyDictionary<int, byte[]> modes)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "INSERT INTO fem_fragment_cut_bc (cut_id, modes) VALUES (@cut, @modes)";
         var cut = cmd.Parameters.Add("@cut", SqliteType.Integer);
         var m = cmd.Parameters.Add("@modes", SqliteType.Blob);
         foreach (var (id, v) in modes)
         {
            cut.Value = id;
            m.Value = v;
            cmd.ExecuteNonQuery();
         }
      }

      /// <summary>Копия масок и ГУ разрезов в схему-копию (внутри транзакции <see cref="DuplicateFemSchema"/>): Id разрезов —
      /// через <paramref name="cutMap"/>, Id загружений ключей — через <paramref name="mapLoadCase"/>.</summary>
      void CopyFragmentMasksCore(int fromSchemaId, int toSchemaId, IReadOnlyDictionary<int, int> cutMap, Func<int, int> mapLoadCase)
      {
         if (GetFragmentMasks(fromSchemaId) is { } set)
            InsertFragmentMasksCore(toSchemaId, new FragmentMaskSet
            {
               SourceKind = set.SourceKind, SourceRef = set.SourceRef, SourceLabel = set.SourceLabel,
               SourceFingerprint = set.SourceFingerprint, CreatedAt = set.CreatedAt, ReportJson = set.ReportJson,
               Items = set.Items.Where(i => cutMap.ContainsKey(i.CutId))
                  .Select(i => i with { CutId = cutMap[i.CutId], Key = i.Key.MapLoadCases(mapLoadCase) }).ToList(),
            });
         var bc = GetFragmentCutBc(fromSchemaId).Where(kv => cutMap.ContainsKey(kv.Key))
            .ToDictionary(kv => cutMap[kv.Key], kv => kv.Value);
         InsertFragmentCutBcCore(bc);
      }
   }
}
