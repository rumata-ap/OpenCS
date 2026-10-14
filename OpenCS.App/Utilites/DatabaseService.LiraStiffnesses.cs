using CScore.Import;

namespace OpenCS.Utilites
{
   /// <summary>Жёсткости ЛИРЫ импортированной схемы: размеры сечений стержней и толщины пластин.</summary>
   public partial class DatabaseService
   {
      /// <summary>Жёсткости схемы-источника (таблица «Жёсткости» ЛИРЫ): параметры хранятся строкой источника,
      /// форма сечения разбирается при использовании.</summary>
      void EnsureFemSchemaStiffnessTable() => MigExec("""
         CREATE TABLE IF NOT EXISTS fem_schema_stiffnesses (
             schema_id      INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
             num            INTEGER NOT NULL,
             kind_code      INTEGER NOT NULL DEFAULT 0,
             name           TEXT NOT NULL DEFAULT '',
             params         TEXT NOT NULL DEFAULT '',
             section_unit_m REAL NOT NULL DEFAULT 0.01,
             PRIMARY KEY (schema_id, num)
         );
         """);

      /// <summary>
      /// Миграция v69: жёсткости схемы-источника и номер жёсткости у КЭ; оси усилий стержней в наборах,
      /// импортированных из ЛИРЫ и SCAD. Прежний импорт писал момент в плоскости X1Z1 стержня (My источника)
      /// в <c>My</c>, а в плоскости X1Y1 — в <c>Mx</c>, т. е. под сечение, лежащее на боку; теперь ось y сечения
      /// OpenCS — вдоль Z1, и в существующих наборах Mx↔My и Vx↔Vy меняются местами. Затрагиваются наборы
      /// файлового импорта (source_type lira/scad) и строки наборов API ЛИРЫ (source_type fea с номером КЭ
      /// внешней схемы); наборы усилий OpenSees и ручные не меняются.
      /// </summary>
      void MigrateV69()
      {
         EnsureFemSchemaStiffnessTable();
         if (!ColumnExists("fem_elements", "stiffness_num"))
            MigExec("ALTER TABLE fem_elements ADD COLUMN stiffness_num INTEGER");

         MigExec("""
            UPDATE force_items
               SET mx = my, my = mx, vx = vy, vy = vx
             WHERE set_id IN (SELECT id FROM force_sets WHERE source_type IN ('lira', 'scad'))
                OR (source_elem_num IS NOT NULL
                    AND set_id IN (SELECT id FROM force_sets WHERE source_type = 'fea'))
            """);
      }

      /// <summary>Заменяет сохранённые жёсткости схемы.</summary>
      public void SaveFemSchemaStiffnesses(int schemaId, IReadOnlyList<LiraStiffnessRecord> stiffnesses)
      {
         using var tx = _connection.BeginTransaction();
         try
         {
            InsertFemSchemaStiffnesses(schemaId, stiffnesses);
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }

      void InsertFemSchemaStiffnesses(int schemaId, IReadOnlyList<LiraStiffnessRecord> stiffnesses)
      {
         using (var delete = _connection.CreateCommand())
         {
            delete.CommandText = "DELETE FROM fem_schema_stiffnesses WHERE schema_id=@sid";
            delete.Parameters.AddWithValue("@sid", schemaId);
            delete.ExecuteNonQuery();
         }
         using var insert = _connection.CreateCommand();
         insert.CommandText = """
            INSERT OR REPLACE INTO fem_schema_stiffnesses (schema_id, num, kind_code, name, params, section_unit_m)
            VALUES (@sid, @num, @kind, @name, @params, @unit)
         """;
         foreach (var s in stiffnesses)
         {
            insert.Parameters.Clear();
            insert.Parameters.AddWithValue("@sid", schemaId);
            insert.Parameters.AddWithValue("@num", s.Id);
            insert.Parameters.AddWithValue("@kind", s.KindCode);
            insert.Parameters.AddWithValue("@name", s.Name ?? "");
            insert.Parameters.AddWithValue("@params", s.Params ?? "");
            insert.Parameters.AddWithValue("@unit", s.SectionUnitM);
            insert.ExecuteNonQuery();
         }
      }

      /// <summary>Жёсткости схемы по номеру; пусто — схема их не хранит.</summary>
      public Dictionary<int, LiraStiffnessRecord> GetFemSchemaStiffnesses(int schemaId)
      {
         var result = new Dictionary<int, LiraStiffnessRecord>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT num, kind_code, name, params, section_unit_m
            FROM fem_schema_stiffnesses WHERE schema_id=@sid ORDER BY num
         """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var r = cmd.ExecuteReader();
         while (r.Read())
            result[r.GetInt32(0)] = new LiraStiffnessRecord(
               r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetDouble(4));
         return result;
      }

      /// <summary>
      /// Заменить жёсткости схемы и номера жёсткостей у её импортированных КЭ (таблицы «Жёсткости» и
      /// «Элементы - жёсткости» ЛИРЫ): КЭ из словаря получают свой номер, остальные — «номер неизвестен».
      /// </summary>
      /// <param name="stiffnessByElemTag">Номер жёсткости по тегу КЭ.</param>
      /// <returns>Число КЭ схемы, получивших номер жёсткости.</returns>
      public int ReplaceFemSchemaStiffnesses(
         int schemaId, IReadOnlyList<LiraStiffnessRecord> stiffnesses, IReadOnlyDictionary<string, int> stiffnessByElemTag)
      {
         using var tx = _connection.BeginTransaction();
         try
         {
            InsertFemSchemaStiffnesses(schemaId, stiffnesses);
            using (var clear = _connection.CreateCommand())
            {
               clear.CommandText = "UPDATE fem_elements SET stiffness_num=NULL WHERE schema_id=@sid AND origin=@origin";
               clear.Parameters.AddWithValue("@sid", schemaId);
               clear.Parameters.AddWithValue("@origin", CScore.Fem.FemMember.MeshSourceImported);
               clear.ExecuteNonQuery();
            }
            int updated = 0;
            using (var cmd = _connection.CreateCommand())
            {
               cmd.CommandText = """
                  UPDATE fem_elements SET stiffness_num=@num
                  WHERE schema_id=@sid AND origin=@origin AND elem_tag=@tag
               """;
               cmd.Parameters.AddWithValue("@sid", schemaId);
               cmd.Parameters.AddWithValue("@origin", CScore.Fem.FemMember.MeshSourceImported);
               var num = cmd.Parameters.Add("@num", Microsoft.Data.Sqlite.SqliteType.Integer);
               var tag = cmd.Parameters.Add("@tag", Microsoft.Data.Sqlite.SqliteType.Text);
               foreach (var (elemTag, value) in stiffnessByElemTag)
               {
                  num.Value = value;
                  tag.Value = elemTag;
                  updated += cmd.ExecuteNonQuery();
               }
            }
            tx.Commit();
            return updated;
         }
         catch { tx.Rollback(); throw; }
      }
   }
}
