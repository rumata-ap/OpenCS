using CScore.Fem;
using Microsoft.Data.Sqlite;

namespace OpenCS.Utilites
{
   /// <summary>Фрагмент этажа (подпроект B): поля схемы-фрагмента, рассечения, опорная сетка, нагрузки-поля, копии
   /// источников армирования родителя.</summary>
   public partial class DatabaseService
   {
      const string FemFragmentDdl = """
         CREATE TABLE IF NOT EXISTS fem_fragment_cuts (
             id INTEGER PRIMARY KEY AUTOINCREMENT,
             schema_id INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
             member_tag TEXT NOT NULL,
             kind TEXT NOT NULL,
             side TEXT NOT NULL,
             z REAL NOT NULL,
             node_tag TEXT,
             segments_json TEXT NOT NULL DEFAULT '[]'
         );
         CREATE INDEX IF NOT EXISTS idx_fem_fragment_cuts_schema ON fem_fragment_cuts(schema_id);
         CREATE TABLE IF NOT EXISTS fem_fragment_ref_elements (
             id INTEGER PRIMARY KEY AUTOINCREMENT,
             schema_id INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
             tag TEXT NOT NULL,
             elem_type TEXT NOT NULL,
             points_blob BLOB NOT NULL,
             stiffness_num INTEGER,
             foundation_c1 REAL,
             source_member_tag TEXT
         );
         CREATE INDEX IF NOT EXISTS idx_fem_fragment_ref_elements_schema ON fem_fragment_ref_elements(schema_id);
         CREATE TABLE IF NOT EXISTS fem_fragment_field_loads (
             id INTEGER PRIMARY KEY AUTOINCREMENT,
             schema_id INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
             load_case_id INTEGER NOT NULL,
             load_kind TEXT NOT NULL,
             axis TEXT NOT NULL,
             coordinate_system TEXT NOT NULL DEFAULT 'global',
             entries_blob BLOB NOT NULL
         );
         CREATE INDEX IF NOT EXISTS idx_fem_fragment_field_loads_schema ON fem_fragment_field_loads(schema_id);
         CREATE TABLE IF NOT EXISTS fem_fragment_rebar_sources (
             schema_id INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
             kind TEXT NOT NULL,
             file_name TEXT NOT NULL DEFAULT '',
             data BLOB NOT NULL,
             PRIMARY KEY (schema_id, kind)
         );
         CREATE INDEX IF NOT EXISTS idx_fem_mesh_nodes_schema_tag ON fem_mesh_nodes(schema_id, node_tag);
         CREATE INDEX IF NOT EXISTS idx_fem_mesh_nodes_schema_z ON fem_mesh_nodes(schema_id, z);
         """;

      /// <summary>Миграция v92: поля схемы-фрагмента этажа, таблицы рассечений, опорной сетки, нагрузок-полей и
      /// источников армирования; индексы узлов сетки по тегу и отметке (снимок полосы родителя).</summary>
      void MigrateV92()
      {
         if (!ColumnExists("fem_schemas", "parent_schema_id"))
            MigExec("ALTER TABLE fem_schemas ADD COLUMN parent_schema_id INTEGER");
         if (!ColumnExists("fem_schemas", "fragment_z1"))
            MigExec("ALTER TABLE fem_schemas ADD COLUMN fragment_z1 REAL");
         if (!ColumnExists("fem_schemas", "fragment_z2"))
            MigExec("ALTER TABLE fem_schemas ADD COLUMN fragment_z2 REAL");
         if (!ColumnExists("fem_schemas", "parent_fingerprint"))
            MigExec("ALTER TABLE fem_schemas ADD COLUMN parent_fingerprint TEXT");
         if (!ColumnExists("fem_schemas", "fragment_build_report_json"))
            MigExec("ALTER TABLE fem_schemas ADD COLUMN fragment_build_report_json TEXT");
         MigExec(FemFragmentDdl);
      }

      /// <summary>Параметры @parent, @z1, @z2, @fp, @report — поля фрагмента строки <c>fem_schemas</c>.</summary>
      static void AddFemSchemaFragmentParameters(SqliteCommand cmd, FemSchema schema)
      {
         cmd.Parameters.AddWithValue("@parent", (object?)schema.ParentSchemaId ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@z1", (object?)schema.FragmentZ1 ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@z2", (object?)schema.FragmentZ2 ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@fp", (object?)schema.ParentFingerprint ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@report", (object?)schema.FragmentBuildReportJson ?? DBNull.Value);
      }
   }
}
