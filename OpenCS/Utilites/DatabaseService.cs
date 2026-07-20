using CScore;
using CScore.Fire;
using CScore.Fire.Entities;
using CSmath;

using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Text.Json.Serialization;

using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace OpenCS.Utilites
{
   /// <summary>
   /// Сервис работы с базой данных SQLite. Заменяет EF Core ApplicationContext.
   /// Управляет подключением к БД, создаёт таблицы и выполняет CRUD-операции
   /// через параметризованный SQL. Вложенные коллекции сериализуются в JSON-колонки.
   /// </summary>
   public class DatabaseService : IDisposable
   {
      private SqliteConnection _connection;
      private string _dataSource;
      private SaveCategory _pendingSave = SaveCategory.None;
      private static readonly JsonSerializerOptions _jsonSettings = new()
      {
         DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
         WriteIndented = false
      };

        const int CurrentSchemaVersion = 35;

      // Миграции v1-v22 удалены — проект всегда стартует от EnsureCreated (v25).
      // Оставлены только v23-v25 как C#-методы ниже.
      static readonly string[] Migrations = [  // пустой — fallback не используется
      ];

      public string DataSource => _dataSource;

      public ObservableCollection<Material> Materials { get; } = [];
      public ObservableCollection<MaterialChars> MaterialChars { get; } = [];
      public ObservableCollection<Contour> Contours { get; } = [];
      public ObservableCollection<StressPoint> Points { get; } = [];
      public ObservableCollection<CircleP> Circles { get; } = [];
      public ObservableCollection<Fiber> Fibers { get; } = [];
      public ObservableCollection<Diagramm> Diagrams { get; } = [];
      public ObservableCollection<CrossSection> CrossSections { get; } = [];
      public ObservableCollection<MaterialArea> MaterialAreas { get; } = [];
      public ObservableCollection<ForceSet> ForceSets { get; } = [];
      public ObservableCollection<PlateSection> PlateSections { get; } = [];
      public ObservableCollection<FireSectionDef> FireSections { get; } = [];
      public ObservableCollection<CalcTask> CalcTasks { get; } = [];
      public ObservableCollection<CalcResult> CalcResults  { get; } = [];
      public ObservableCollection<CScore.Fem.FemSchema> FemSchemas { get; } = [];
      public ObservableCollection<CScore.Fem.FemCheck>  FemChecks  { get; } = [];

      public DatabaseService(string dataSource)
      {
         _dataSource = dataSource;
         _connection = OpenOrRecreate(dataSource);
         SetDeleteJournalMode();
         EnsureCreated();
         Migrate();
      }

      // Открывает файл БД; если он повреждён — удаляет и создаёт заново.
      static SqliteConnection OpenOrRecreate(string dataSource)
      {
         var conn = new SqliteConnection($"Data Source={dataSource}");
         try
         {
            conn.Open();
            // Быстрая проверка целостности
            using var chk = conn.CreateCommand();
            chk.CommandText = "PRAGMA schema_version";
            chk.ExecuteScalar();
            return conn;
         }
         catch (SqliteException)
         {
            conn.Dispose();
            try { File.Delete(dataSource); } catch { }
            var fresh = new SqliteConnection($"Data Source={dataSource}");
            fresh.Open();
            return fresh;
         }
      }

      void SetDeleteJournalMode()
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "PRAGMA journal_mode=DELETE";
         cmd.ExecuteNonQuery();
         cmd.CommandText = "PRAGMA foreign_keys=OFF";
         cmd.ExecuteNonQuery();
      }

      private void EnsureCreated()
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS materials (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                type INTEGER NOT NULL DEFAULT 0,
                tag TEXT NOT NULL DEFAULT '',
                description TEXT NOT NULL DEFAULT '',
                e REAL NOT NULL DEFAULT 0,
                chars_json TEXT NOT NULL DEFAULT '[]',
                aggregate_type TEXT NOT NULL DEFAULT 'silicate',
                base_type INTEGER NOT NULL DEFAULT 0,
                custom_diagram_ids TEXT NOT NULL DEFAULT '{}'
            );
            CREATE TABLE IF NOT EXISTS contours (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                tag TEXT NOT NULL DEFAULT '',
                wkt TEXT NOT NULL DEFAULT '',
                type INTEGER NOT NULL DEFAULT 0,
                geometry_set TEXT NULL,
                points_json TEXT NOT NULL DEFAULT '[]',
                regions_json TEXT NOT NULL DEFAULT '[]'
            );
            CREATE TABLE IF NOT EXISTS circles (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                tag TEXT NOT NULL DEFAULT '',
                x REAL NOT NULL DEFAULT 0,
                y REAL NOT NULL DEFAULT 0,
                diameter REAL NOT NULL DEFAULT 0,
                radius REAL NOT NULL DEFAULT 0,
                area REAL NOT NULL DEFAULT 0,
                type INTEGER NOT NULL DEFAULT 0,
                num INTEGER NOT NULL DEFAULT 0,
                geometry_set TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS rc_fiber_regions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                tag TEXT NOT NULL DEFAULT '',
                data_json TEXT NOT NULL DEFAULT '{}'
            );
            CREATE TABLE IF NOT EXISTS diagrams (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                tag TEXT NOT NULL DEFAULT '',
                type INTEGER NOT NULL DEFAULT 0,
                material_type INTEGER NOT NULL DEFAULT 0,
                calc_type INTEGER NOT NULL DEFAULT 0,
                material_id INTEGER NOT NULL DEFAULT 0,
                spline_data_json TEXT NOT NULL DEFAULT '{}'
            );
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value_json TEXT NOT NULL DEFAULT '{}'
            );
            CREATE TABLE IF NOT EXISTS cross_sections (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                num         INTEGER NOT NULL DEFAULT 0,
                tag         TEXT NOT NULL DEFAULT '',
                description TEXT,
                type        TEXT NOT NULL DEFAULT 'simple'
            );
            CREATE TABLE IF NOT EXISTS cross_section_stages (
                section_id        INTEGER NOT NULL REFERENCES cross_sections(id) ON DELETE CASCADE,
                stage1_section_id INTEGER NOT NULL REFERENCES cross_sections(id)
            );
            CREATE TABLE IF NOT EXISTS cross_section_stage_kurvature (
                section_id INTEGER PRIMARY KEY REFERENCES cross_sections(id) ON DELETE CASCADE,
                e0         REAL NOT NULL DEFAULT 0,
                ky         REAL NOT NULL DEFAULT 0,
                kz         REAL NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS cross_section_areas (
                section_id  INTEGER NOT NULL REFERENCES cross_sections(id) ON DELETE CASCADE,
                area_id     INTEGER NOT NULL REFERENCES material_areas(id),
                sort_order  INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (section_id, area_id)
            );
            CREATE TABLE IF NOT EXISTS force_sets (
                id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                num                INTEGER NOT NULL DEFAULT 0,
                tag                TEXT NOT NULL DEFAULT '',
                description        TEXT,
                kind               TEXT NOT NULL DEFAULT 'bar',
                source_type        TEXT,
                source_schema_id   INTEGER,
                source_element_tag TEXT,
                source_member_id   INTEGER,
                source_element_id  INTEGER
            );
            CREATE TABLE IF NOT EXISTS force_items (
                id      INTEGER PRIMARY KEY AUTOINCREMENT,
                set_id  INTEGER NOT NULL REFERENCES force_sets(id) ON DELETE CASCADE,
                num     INTEGER NOT NULL DEFAULT 0,
                label   TEXT NOT NULL DEFAULT '',
                n       REAL NOT NULL DEFAULT 0,
                mx      REAL NOT NULL DEFAULT 0,
                my      REAL NOT NULL DEFAULT 0,
                vx      REAL NOT NULL DEFAULT 0,
                vy      REAL NOT NULL DEFAULT 0,
                t       REAL NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS force_shell_items (
                id      INTEGER PRIMARY KEY AUTOINCREMENT,
                set_id  INTEGER NOT NULL REFERENCES force_sets(id) ON DELETE CASCADE,
                num     INTEGER NOT NULL DEFAULT 0,
                label   TEXT NOT NULL DEFAULT '',
                nx      REAL NOT NULL DEFAULT 0,
                ny      REAL NOT NULL DEFAULT 0,
                nxy     REAL NOT NULL DEFAULT 0,
                mx      REAL NOT NULL DEFAULT 0,
                my      REAL NOT NULL DEFAULT 0,
                mxy     REAL NOT NULL DEFAULT 0,
                qx      REAL NOT NULL DEFAULT 0,
                qy      REAL NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS plate_sections (
                id                   INTEGER PRIMARY KEY AUTOINCREMENT,
                num                  INTEGER NOT NULL DEFAULT 0,
                tag                  TEXT NOT NULL DEFAULT '',
                description          TEXT,
                h                    REAL NOT NULL DEFAULT 0.2,
                n_layers             INTEGER NOT NULL DEFAULT 10,
                concrete_material_id INTEGER NOT NULL DEFAULT 0,
                rebar_material_id    INTEGER NOT NULL DEFAULT 0,
                tension_concrete     INTEGER NOT NULL DEFAULT 0,
                softening_model      TEXT NOT NULL DEFAULT '',
                softening_eps_c2     REAL NOT NULL DEFAULT 0.002,
                plate_model           TEXT NOT NULL DEFAULT 'layered',
                concrete_diagram_type TEXT NOT NULL DEFAULT 'L3',
                rebar_layers_json     TEXT NOT NULL DEFAULT '[]'
            );
            CREATE TABLE IF NOT EXISTS material_areas (
                id               INTEGER PRIMARY KEY AUTOINCREMENT,
                section_id       INTEGER,
                num              INTEGER NOT NULL DEFAULT 0,
                tag              TEXT NOT NULL DEFAULT '',
                description      TEXT,
                material_id      INTEGER REFERENCES materials(id),
                host_area_id     INTEGER REFERENCES material_areas(id),
                diagramm_type    TEXT NOT NULL DEFAULT 'L2',
                nx               INTEGER NOT NULL DEFAULT 21,
                ny               INTEGER NOT NULL DEFAULT 21,
                wkt              TEXT,
                category         TEXT NOT NULL DEFAULT 'region',
                pool_contour_id  INTEGER REFERENCES contours(id),
                mesh_method      TEXT NOT NULL DEFAULT 'grid',
                mesh_max_area    REAL NOT NULL DEFAULT 0.01,
                mesh_min_angle   REAL NOT NULL DEFAULT 30.0,
                mesh_max_edge_len REAL NOT NULL DEFAULT 0.0,
                mesh_smooth_iter  INTEGER NOT NULL DEFAULT 5,
                sig_sp            REAL    NOT NULL DEFAULT 0.0,
                gamma_sp          REAL    NOT NULL DEFAULT 1.0
            );
            CREATE TABLE IF NOT EXISTS point_fibers (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                area_id     INTEGER NOT NULL REFERENCES material_areas(id) ON DELETE CASCADE,
                x           REAL NOT NULL DEFAULT 0,
                y           REAL NOT NULL DEFAULT 0,
                area        REAL NOT NULL DEFAULT 0,
                diameter    REAL NOT NULL DEFAULT 0,
                eps_p       REAL NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS mesh_fibers (
                id      INTEGER PRIMARY KEY AUTOINCREMENT,
                area_id INTEGER NOT NULL REFERENCES material_areas(id) ON DELETE CASCADE,
                type    TEXT NOT NULL DEFAULT 'poly',
                x       REAL NOT NULL DEFAULT 0,
                y       REAL NOT NULL DEFAULT 0,
                area    REAL NOT NULL DEFAULT 0,
                wkt     TEXT,
                eps_p   REAL NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS calc_tasks (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                num             INTEGER NOT NULL DEFAULT 0,
                tag             TEXT NOT NULL DEFAULT '',
                kind            TEXT NOT NULL DEFAULT 'strain_state',
                section_id      INTEGER NOT NULL DEFAULT 0,
                force_set_id    INTEGER NOT NULL DEFAULT 0,
                force_item_id   INTEGER NOT NULL DEFAULT 0,
                calc_type       TEXT NOT NULL DEFAULT 'C',
                params_json     TEXT NOT NULL DEFAULT '{}'
            );
            CREATE TABLE IF NOT EXISTS calc_results (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                task_id      INTEGER NOT NULL DEFAULT 0,
                task_kind    TEXT NOT NULL DEFAULT '',
                task_tag     TEXT NOT NULL DEFAULT '',
                created      TEXT NOT NULL DEFAULT '',
                status       TEXT NOT NULL DEFAULT 'ok',
                data_json    TEXT NOT NULL DEFAULT '{}',
                fem_check_id INTEGER
            );
            CREATE TABLE IF NOT EXISTS fire_sections (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                num INTEGER NOT NULL DEFAULT 0,
                tag TEXT NOT NULL DEFAULT '',
                section_id INTEGER NOT NULL DEFAULT 0,
                fire_duration_min REAL NOT NULL DEFAULT 60,
                fire_curve TEXT NOT NULL DEFAULT 'iso834',
                mesh_step_m REAL NOT NULL DEFAULT 0.01,
                time_step_s REAL NOT NULL DEFAULT 5,
                theta REAL NOT NULL DEFAULT 1,
                picard_tol_celsius REAL NOT NULL DEFAULT 0.5,
                picard_max_iter INTEGER NOT NULL DEFAULT 20,
                snapshot_step_min REAL NOT NULL DEFAULT 5,
                bc_preset TEXT NOT NULL DEFAULT 'manual',
                hole_bc_preset TEXT NOT NULL DEFAULT 'ambient',
                algorithm TEXT NOT NULL DEFAULT 'ruppert',
                smooth_iter_tri INTEGER NOT NULL DEFAULT 5,
                aggregate_type TEXT NOT NULL DEFAULT '',
                mesh_element_type TEXT NOT NULL DEFAULT 'linear'
            );
            CREATE TABLE IF NOT EXISTS fire_section_edges (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                fire_section_id INTEGER NOT NULL REFERENCES fire_sections(id) ON DELETE CASCADE,
                edge_index INTEGER NOT NULL,
                contour_type TEXT NOT NULL DEFAULT 'outer',
                hole_index INTEGER,
                bc_type TEXT NOT NULL DEFAULT 'adiabatic',
                alpha_conv REAL NOT NULL DEFAULT 0,
                emissivity REAL NOT NULL DEFAULT 0,
                t_ambient REAL NOT NULL DEFAULT 20
            );
            CREATE TABLE IF NOT EXISTS fire_thermal_results (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                fire_section_id INTEGER NOT NULL REFERENCES fire_sections(id) ON DELETE CASCADE,
                created TEXT NOT NULL DEFAULT '',
                blob BLOB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS fem_schemas (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                tag         TEXT NOT NULL DEFAULT '',
                source_type TEXT NOT NULL DEFAULT 'internal',
                created     TEXT NOT NULL DEFAULT ''
            );
            CREATE TABLE IF NOT EXISTS fem_nodes (
                id        INTEGER PRIMARY KEY AUTOINCREMENT,
                schema_id INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
                node_tag  TEXT NOT NULL DEFAULT '',
                x REAL NOT NULL DEFAULT 0,
                y REAL NOT NULL DEFAULT 0,
                z REAL NOT NULL DEFAULT 0,
                dof_mask  INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS fem_members (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                schema_id     INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
                elem_tag      TEXT NOT NULL DEFAULT '',
                elem_type     TEXT NOT NULL DEFAULT 'beam',
                node_ids_json TEXT NOT NULL DEFAULT '[]',
                section_tag   TEXT,
                material_tag  TEXT,
                thickness_m   REAL,
                cross_section_id   INTEGER REFERENCES cross_sections(id),
                gj_strategy        TEXT NOT NULL DEFAULT 'manual',
                gj_manual_value    REAL,
                gj_torsion_task_id INTEGER REFERENCES calc_tasks(id),
                target_mesh_length_m REAL,
                plate_section_id   INTEGER REFERENCES plate_sections(id),
                force_set_id       INTEGER REFERENCES force_sets(id),
                design_params_json TEXT
            );
            CREATE TABLE IF NOT EXISTS fem_mesh_nodes (
                id               INTEGER PRIMARY KEY AUTOINCREMENT,
                schema_id        INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
                node_tag         TEXT NOT NULL DEFAULT '',
                x                REAL NOT NULL DEFAULT 0,
                y                REAL NOT NULL DEFAULT 0,
                z                REAL NOT NULL DEFAULT 0,
                source_node_tag  TEXT,
                source_member_tag TEXT
            );
            CREATE TABLE IF NOT EXISTS fem_elements (
                id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                schema_id           INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
                elem_tag            TEXT NOT NULL DEFAULT '',
                elem_type           TEXT NOT NULL DEFAULT 'beam',
                node_ids_json       TEXT NOT NULL DEFAULT '[]',
                source_member_tag   TEXT,
                cross_section_id    INTEGER REFERENCES cross_sections(id),
                gj_strategy         TEXT NOT NULL DEFAULT 'manual',
                gj_manual_value     REAL,
                gj_torsion_task_id  INTEGER REFERENCES calc_tasks(id),
                section_tag         TEXT,
                material_tag        TEXT,
                thickness_m         REAL
            );
            CREATE TABLE IF NOT EXISTS fem_member_groups (
                id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                schema_id          INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
                tag                TEXT NOT NULL DEFAULT '',
                member_type        TEXT,
                member_tags_json   TEXT NOT NULL DEFAULT '[]',
                plate_section_id   INTEGER REFERENCES plate_sections(id),
                force_set_id       INTEGER REFERENCES force_sets(id),
                design_params_json TEXT
            );
            CREATE TABLE IF NOT EXISTS fem_load_cases (
                id        INTEGER PRIMARY KEY AUTOINCREMENT,
                schema_id INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
                tag       TEXT NOT NULL DEFAULT '',
                load_type TEXT,
                sp20_type TEXT NOT NULL DEFAULT 'short_term',
                sp20_group TEXT,
                gamma_f_unfav REAL,
                gamma_f_fav REAL,
                psi1 REAL,
                psi2 REAL
            );
            CREATE TABLE IF NOT EXISTS fem_node_loads (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                schema_id INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
                load_case_id INTEGER NOT NULL REFERENCES fem_load_cases(id) ON DELETE CASCADE,
                node_id INTEGER NOT NULL REFERENCES fem_nodes(id) ON DELETE CASCADE,
                fx REAL NOT NULL DEFAULT 0,
                fy REAL NOT NULL DEFAULT 0,
                fz REAL NOT NULL DEFAULT 0,
                mx REAL NOT NULL DEFAULT 0,
                my REAL NOT NULL DEFAULT 0,
                mz REAL NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS fem_load_definitions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                schema_id INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
                tag TEXT NOT NULL DEFAULT '',
                description TEXT,
                expression_json TEXT NOT NULL DEFAULT '{}',
                source_kind TEXT NOT NULL DEFAULT 'manual',
                combination_type TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_fem_load_definitions_schema_tag
                ON fem_load_definitions(schema_id, tag);
            CREATE TABLE IF NOT EXISTS fem_analyses (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                schema_id INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
                tag TEXT NOT NULL DEFAULT '',
                kind TEXT NOT NULL DEFAULT 'linear',
                load_expression_json TEXT NOT NULL DEFAULT '{}',
                params_json TEXT NOT NULL DEFAULT '{}',
                status TEXT NOT NULL DEFAULT 'created',
                result_id INTEGER REFERENCES calc_results(id),
                created TEXT NOT NULL DEFAULT ''
            );
            CREATE TABLE IF NOT EXISTS fem_checks (
                id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                schema_id           INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
                member_id           INTEGER NOT NULL REFERENCES fem_member_groups(id),
                norm_code           TEXT NOT NULL DEFAULT 'steel_check',
                params_json         TEXT,
                result_id           INTEGER REFERENCES calc_results(id),
                tag                 TEXT NOT NULL DEFAULT '',
                force_set_ids_json  TEXT NOT NULL DEFAULT '[]',
                calc_type_override  TEXT,
                element_id          INTEGER
            );";
         cmd.ExecuteNonQuery();

         // Для новых БД сразу выставляем текущую версию, чтобы Migrate() не гнал старые миграции
         // по таблицам, которые EnsureCreated уже создал в финальном виде.
         var initVer = _connection.CreateCommand();
         initVer.CommandText =
            "INSERT OR IGNORE INTO settings (key, value_json) VALUES ('schema_version', $ver)";
         initVer.Parameters.AddWithValue("$ver", CurrentSchemaVersion.ToString());
         initVer.ExecuteNonQuery();
      }

      /// <summary>
      /// Применяет миграции схемы БД, отсутствующие в текущем файле.
      /// Версия схемы хранится в таблице settings (ключ 'schema_version').
      /// Новые базы данных создаются с версией CurrentSchemaVersion.
      /// Старые базы данных последовательно догоняются миграциями.
      /// </summary>
      void Migrate()
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT value_json FROM settings WHERE key = 'schema_version'";
         var row = cmd.ExecuteScalar() as string;

         int version = 0;
         if (row != null)
         {
            if (int.TryParse(row, out var v)) version = v;
            else if (int.TryParse(row.Trim('"'), out v)) version = v;
         }

         if (version >= CurrentSchemaVersion) return;

         using var tx = _connection.BeginTransaction();
         try
         {
            for (int i = version; i < CurrentSchemaVersion; i++)
            {
               if (i == 22) { MigrateV23(); continue; }
               if (i == 23) { MigrateV24(); continue; }
               if (i == 24) { MigrateV25(); continue; }
               if (i == 25) { MigrateV26(); continue; }
               if (i == 26) { MigrateV27(); continue; }
               if (i == 27) { MigrateV28(); continue; }
               if (i == 28) { MigrateV29(); continue; }
               if (i == 29) { MigrateV30(); continue; }
               if (i == 30) { MigrateV31(); continue; }
               if (i == 31) { MigrateV32(); continue; }
               if (i == 32) { MigrateV33(); continue; }
               if (i == 33) { MigrateV34(); continue; }
               if (i == 34) { MigrateV35(); continue; }
            }

            var updCmd = _connection.CreateCommand();
            updCmd.CommandText = "INSERT OR REPLACE INTO settings (key, value_json) VALUES ('schema_version', $ver)";
            updCmd.Parameters.AddWithValue("$ver", CurrentSchemaVersion.ToString());
            updCmd.ExecuteNonQuery();

            tx.Commit();
         }
         catch
         {
            tx.Rollback();
            throw;
         }
      }

      // ------------------------------------------------------------------
      // Вспомогательные методы миграции
      // ------------------------------------------------------------------

      bool ColumnExists(string table, string column)
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}'";
         return (long)cmd.ExecuteScalar()! > 0;
      }

      void MigExec(string sql)
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = sql;
         cmd.ExecuteNonQuery();
      }

      /// <summary>Миграция v23: FEM-таблицы, source-колонки force_sets, fem_check_id в calc_results.</summary>
      void MigrateV23()
      {
         MigExec("""
            CREATE TABLE IF NOT EXISTS fem_schemas (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                tag         TEXT NOT NULL DEFAULT '',
                source_type TEXT NOT NULL DEFAULT 'internal',
                created     TEXT NOT NULL DEFAULT ''
            );
            CREATE TABLE IF NOT EXISTS fem_nodes (
                id        INTEGER PRIMARY KEY AUTOINCREMENT,
                schema_id INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
                node_tag  TEXT NOT NULL DEFAULT '',
                x REAL NOT NULL DEFAULT 0,
                y REAL NOT NULL DEFAULT 0,
                z REAL NOT NULL DEFAULT 0,
                dof_mask  INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS fem_elements (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                schema_id     INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
                elem_tag      TEXT NOT NULL DEFAULT '',
                elem_type     TEXT NOT NULL DEFAULT 'beam',
                node_ids_json TEXT NOT NULL DEFAULT '[]',
                section_tag   TEXT,
                material_tag  TEXT,
                thickness_m   REAL
            );
            CREATE TABLE IF NOT EXISTS fem_members (
                id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                schema_id          INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
                tag                TEXT NOT NULL DEFAULT '',
                member_type        TEXT,
                elem_ids_json      TEXT NOT NULL DEFAULT '[]',
                cross_section_id   INTEGER REFERENCES cross_sections(id),
                plate_section_id   INTEGER REFERENCES plate_sections(id),
                force_set_id       INTEGER REFERENCES force_sets(id),
                design_params_json TEXT
            );
            CREATE TABLE IF NOT EXISTS fem_load_cases (
                id        INTEGER PRIMARY KEY AUTOINCREMENT,
                schema_id INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
                tag       TEXT NOT NULL DEFAULT '',
                load_type TEXT
            );
            CREATE TABLE IF NOT EXISTS fem_checks (
                id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                schema_id           INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
                member_id           INTEGER NOT NULL REFERENCES fem_members(id),
                norm_code           TEXT NOT NULL DEFAULT 'steel_check',
                params_json         TEXT,
                result_id           INTEGER REFERENCES calc_results(id),
                tag                 TEXT NOT NULL DEFAULT '',
                force_set_ids_json  TEXT NOT NULL DEFAULT '[]',
                calc_type_override  TEXT
            );
            """);
         if (!ColumnExists("force_sets", "source_type"))
            MigExec("ALTER TABLE force_sets ADD COLUMN source_type        TEXT");
         if (!ColumnExists("force_sets", "source_schema_id"))
            MigExec("ALTER TABLE force_sets ADD COLUMN source_schema_id   INTEGER");
         if (!ColumnExists("force_sets", "source_element_tag"))
            MigExec("ALTER TABLE force_sets ADD COLUMN source_element_tag TEXT");
         if (!ColumnExists("fem_elements", "thickness_m"))
            MigExec("ALTER TABLE fem_elements ADD COLUMN thickness_m REAL");
            MigExec("ALTER TABLE calc_results ADD COLUMN fem_check_id INTEGER");
      }

      /// <summary>Миграция v25: source_member_id в force_sets.</summary>
      void MigrateV25()
      {
         if (!ColumnExists("force_sets", "source_member_id"))
            MigExec("ALTER TABLE force_sets ADD COLUMN source_member_id INTEGER");
      }

      /// <summary>Миграция v27: исправить kind='shell' у наборов усилий стержней (ошибка импорта).</summary>
      void MigrateV27()
      {
         MigExec("""
            UPDATE force_sets
            SET kind = 'bar'
            WHERE kind = 'shell'
              AND EXISTS     (SELECT 1 FROM force_items       WHERE set_id = force_sets.id)
              AND NOT EXISTS (SELECT 1 FROM force_shell_items WHERE set_id = force_sets.id)
         """);
      }

      /// <summary>Миграция v28: канонические узловые нагрузки, постановки и метаданные СП20.</summary>
      void MigrateV28()
      {
         if (!ColumnExists("fem_load_cases", "sp20_type"))
            MigExec("ALTER TABLE fem_load_cases ADD COLUMN sp20_type TEXT NOT NULL DEFAULT 'short_term'");
         if (!ColumnExists("fem_load_cases", "sp20_group"))
            MigExec("ALTER TABLE fem_load_cases ADD COLUMN sp20_group TEXT");
         if (!ColumnExists("fem_load_cases", "gamma_f_unfav"))
            MigExec("ALTER TABLE fem_load_cases ADD COLUMN gamma_f_unfav REAL");
         if (!ColumnExists("fem_load_cases", "gamma_f_fav"))
            MigExec("ALTER TABLE fem_load_cases ADD COLUMN gamma_f_fav REAL");
         if (!ColumnExists("fem_load_cases", "psi1"))
            MigExec("ALTER TABLE fem_load_cases ADD COLUMN psi1 REAL");
         if (!ColumnExists("fem_load_cases", "psi2"))
            MigExec("ALTER TABLE fem_load_cases ADD COLUMN psi2 REAL");

         MigExec("""
            CREATE TABLE IF NOT EXISTS fem_node_loads (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                schema_id INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
                load_case_id INTEGER NOT NULL REFERENCES fem_load_cases(id) ON DELETE CASCADE,
                node_id INTEGER NOT NULL REFERENCES fem_nodes(id) ON DELETE CASCADE,
                fx REAL NOT NULL DEFAULT 0,
                fy REAL NOT NULL DEFAULT 0,
                fz REAL NOT NULL DEFAULT 0,
                mx REAL NOT NULL DEFAULT 0,
                my REAL NOT NULL DEFAULT 0,
                mz REAL NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS fem_analyses (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                schema_id INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
                tag TEXT NOT NULL DEFAULT '',
                kind TEXT NOT NULL DEFAULT 'linear',
                load_expression_json TEXT NOT NULL DEFAULT '{}',
                params_json TEXT NOT NULL DEFAULT '{}',
                status TEXT NOT NULL DEFAULT 'created',
                result_id INTEGER REFERENCES calc_results(id),
                created TEXT NOT NULL DEFAULT ''
            )
         """);
      }

      /// <summary>Миграция v29: GJ-стратегия и ссылка на задачу кручения на FemMember.</summary>
      void MigrateV29()
      {
         if (!ColumnExists("fem_members", "gj_strategy"))
            MigExec("ALTER TABLE fem_members ADD COLUMN gj_strategy TEXT NOT NULL DEFAULT 'manual'");
         if (!ColumnExists("fem_members", "gj_manual_value"))
            MigExec("ALTER TABLE fem_members ADD COLUMN gj_manual_value REAL");
         if (!ColumnExists("fem_members", "gj_torsion_task_id"))
            MigExec("ALTER TABLE fem_members ADD COLUMN gj_torsion_task_id INTEGER");
      }

      /// <summary>Проверяет существование таблицы (для идемпотентных переименований в миграциях).</summary>
      bool TableExists(string table)
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@n";
         cmd.Parameters.AddWithValue("@n", table);
         return (long)cmd.ExecuteScalar()! > 0;
      }

      /// <summary>Миграция v30: конструктивный элемент — теперь FemMember (было FemElement, таблица
      /// fem_elements→fem_members), группа — FemMemberGroup (было FemMember, таблица fem_members→
      /// fem_member_groups). Сечение/GJ переезжают с группы на каждый её элемент — раньше значение
      /// относилось сразу ко всем элементам группы, теперь это собственное поле элемента.
      ///
      /// EnsureCreated() уже выполнился к моменту вызова Migrate() и мог успеть создать пустые
      /// fem_members/fem_member_groups НОВОЙ формы через CREATE TABLE IF NOT EXISTS (для баз без
      /// старого fem_members вообще — например, версии до v23). Поэтому признаком «есть что
      /// переносить» служит не факт существования таблиц (они теперь существуют всегда), а наличие
      /// СТАРОЙ колонки elem_ids_json на fem_members — она есть только у старой формы группы.</summary>
      void MigrateV30()
      {
         bool hasLegacyGroupShape = ColumnExists("fem_members", "elem_ids_json");

         if (hasLegacyGroupShape)
         {
            // EnsureCreated мог уже создать пустую fem_member_groups новой формы — она не нужна,
            // переносим на её место старый fem_members со всеми данными.
            MigExec("DROP TABLE IF EXISTS fem_member_groups");
            MigExec("ALTER TABLE fem_members RENAME TO fem_member_groups");
            MigExec("ALTER TABLE fem_member_groups RENAME COLUMN elem_ids_json TO member_tags_json");

            if (TableExists("fem_elements") && ColumnExists("fem_elements", "elem_type"))
            {
               // Аналогично: EnsureCreated мог уже создать пустую fem_members новой формы.
               MigExec("DROP TABLE IF EXISTS fem_members");
               MigExec("ALTER TABLE fem_elements RENAME TO fem_members");
            }
         }

         if (!ColumnExists("fem_members", "cross_section_id"))
            MigExec("ALTER TABLE fem_members ADD COLUMN cross_section_id INTEGER REFERENCES cross_sections(id)");
         if (!ColumnExists("fem_members", "gj_strategy"))
            MigExec("ALTER TABLE fem_members ADD COLUMN gj_strategy TEXT NOT NULL DEFAULT 'manual'");
         if (!ColumnExists("fem_members", "gj_manual_value"))
            MigExec("ALTER TABLE fem_members ADD COLUMN gj_manual_value REAL");
         if (!ColumnExists("fem_members", "gj_torsion_task_id"))
            MigExec("ALTER TABLE fem_members ADD COLUMN gj_torsion_task_id INTEGER REFERENCES calc_tasks(id)");

         if (!hasLegacyGroupShape) return; // fem_member_groups новой формы — переносить нечего

         // Перенос значений сечения/GJ с группы на каждый её элемент.
         var groups = new List<(int Id, string TagsJson, long? CrossSectionId, string GjStrategy, double? GjManualValue, long? GjTorsionTaskId)>();
         using (var groupCmd = _connection.CreateCommand())
         {
            groupCmd.CommandText = "SELECT id, member_tags_json, cross_section_id, gj_strategy, gj_manual_value, gj_torsion_task_id FROM fem_member_groups";
            using var rdr = groupCmd.ExecuteReader();
            while (rdr.Read())
               groups.Add((
                  rdr.GetInt32(0),
                  rdr.GetString(1),
                  rdr.IsDBNull(2) ? null : rdr.GetInt64(2),
                  rdr.GetString(3),
                  rdr.IsDBNull(4) ? null : rdr.GetDouble(4),
                  rdr.IsDBNull(5) ? null : rdr.GetInt64(5)));
         }

         using var updCmd = _connection.CreateCommand();
         updCmd.CommandText = """
            UPDATE fem_members SET cross_section_id=@cs, gj_strategy=@gs, gj_manual_value=@gv, gj_torsion_task_id=@gt
            WHERE schema_id=(SELECT schema_id FROM fem_member_groups WHERE id=@gid) AND elem_tag=@tag
         """;
         updCmd.Parameters.Add("@cs",  Microsoft.Data.Sqlite.SqliteType.Integer);
         updCmd.Parameters.Add("@gs",  Microsoft.Data.Sqlite.SqliteType.Text);
         updCmd.Parameters.Add("@gv",  Microsoft.Data.Sqlite.SqliteType.Real);
         updCmd.Parameters.Add("@gt",  Microsoft.Data.Sqlite.SqliteType.Integer);
         updCmd.Parameters.Add("@gid", Microsoft.Data.Sqlite.SqliteType.Integer);
         updCmd.Parameters.Add("@tag", Microsoft.Data.Sqlite.SqliteType.Text);

         foreach (var g in groups)
         {
            if (g.CrossSectionId is null && g.GjStrategy == "manual" && g.GjManualValue is null && g.GjTorsionTaskId is null)
               continue; // нечего переносить

            var tags = System.Text.Json.JsonSerializer.Deserialize<int[]>(g.TagsJson) ?? [];
            foreach (var tag in tags)
            {
               updCmd.Parameters["@cs"].Value  = (object?)g.CrossSectionId  ?? DBNull.Value;
               updCmd.Parameters["@gs"].Value  = g.GjStrategy;
               updCmd.Parameters["@gv"].Value  = (object?)g.GjManualValue   ?? DBNull.Value;
               updCmd.Parameters["@gt"].Value  = (object?)g.GjTorsionTaskId ?? DBNull.Value;
               updCmd.Parameters["@gid"].Value = g.Id;
               updCmd.Parameters["@tag"].Value = tag.ToString();
               updCmd.ExecuteNonQuery();
            }
         }
      }

      /// <summary>Миграция v31: целевая длина конструктивного элемента и сохранённый FEM-слепок.</summary>
      void MigrateV31()
      {
         if (!ColumnExists("fem_members", "target_mesh_length_m"))
            MigExec("ALTER TABLE fem_members ADD COLUMN target_mesh_length_m REAL");

         if (TableExists("fem_elements") && ColumnExists("fem_elements", "elem_type"))
         {
            MigExec("""
               INSERT INTO fem_members
                  (schema_id, elem_tag, elem_type, node_ids_json, section_tag, material_tag,
                   thickness_m, cross_section_id, gj_strategy, gj_manual_value,
                   gj_torsion_task_id, target_mesh_length_m)
               SELECT legacy.schema_id, legacy.elem_tag, legacy.elem_type, legacy.node_ids_json,
                      legacy.section_tag, legacy.material_tag, legacy.thickness_m,
                      NULL, 'manual', NULL, NULL, NULL
               FROM fem_elements AS legacy
               WHERE NOT EXISTS
                  (SELECT 1 FROM fem_members AS current
                   WHERE current.schema_id = legacy.schema_id
                     AND current.elem_tag = legacy.elem_tag)
               """);

            var backupTable = "fem_elements_legacy_v30";
            for (var suffix = 2; TableExists(backupTable); suffix++)
               backupTable = $"fem_elements_legacy_v30_{suffix}";
            MigExec($"ALTER TABLE fem_elements RENAME TO {backupTable}");
         }

         MigExec("""
            CREATE TABLE IF NOT EXISTS fem_mesh_nodes (
                id                INTEGER PRIMARY KEY AUTOINCREMENT,
                schema_id         INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
                node_tag          TEXT NOT NULL DEFAULT '',
                x                 REAL NOT NULL DEFAULT 0,
                y                 REAL NOT NULL DEFAULT 0,
                z                 REAL NOT NULL DEFAULT 0,
                source_node_tag   TEXT,
                source_member_tag TEXT
            );
            CREATE TABLE IF NOT EXISTS fem_elements (
                id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                schema_id          INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
                elem_tag           TEXT NOT NULL DEFAULT '',
                node_ids_json      TEXT NOT NULL DEFAULT '[]',
                source_member_tag  TEXT,
                cross_section_id   INTEGER REFERENCES cross_sections(id),
                gj_strategy        TEXT NOT NULL DEFAULT 'manual',
                gj_manual_value    REAL,
                gj_torsion_task_id INTEGER REFERENCES calc_tasks(id)
            );
            """);
      }

      /// <summary>Миграция v32: именованные определения загружений FEM.</summary>
      void MigrateV32()
      {
         MigExec("""
            CREATE TABLE IF NOT EXISTS fem_load_definitions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                schema_id INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
                tag TEXT NOT NULL DEFAULT '',
                description TEXT,
                expression_json TEXT NOT NULL DEFAULT '{}',
                source_kind TEXT NOT NULL DEFAULT 'manual',
                combination_type TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_fem_load_definitions_schema_tag
                ON fem_load_definitions(schema_id, tag);
         """);
      }

      /// <summary>
      /// Миграция v33: восстанавливает thickness_m в конструктивных элементах.
      ///
      /// Ранние версии v30 могли оставлять базу с отметкой актуальной версии,
      /// но без этого столбца, если перестройка fem_members была прервана.
      /// </summary>
      void MigrateV33()
      {
         if (!ColumnExists("fem_members", "thickness_m"))
            MigExec("ALTER TABLE fem_members ADD COLUMN thickness_m REAL");
      }

      /// <summary>
      /// Миграция v34: FemMember получает собственные свойства цели проверки
      /// (plate_section_id, force_set_id, design_params_json) — как у FemMemberGroup, но для
      /// одиночного конструктивного элемента. fem_checks получает element_id (альтернатива
      /// member_id для проверки одного элемента без группы). force_sets получает
      /// source_element_id (аналог source_member_id, для наборов усилий одного элемента).
      /// </summary>
      void MigrateV34()
      {
         if (!ColumnExists("fem_members", "plate_section_id"))
            MigExec("ALTER TABLE fem_members ADD COLUMN plate_section_id INTEGER REFERENCES plate_sections(id)");
         if (!ColumnExists("fem_members", "force_set_id"))
            MigExec("ALTER TABLE fem_members ADD COLUMN force_set_id INTEGER REFERENCES force_sets(id)");
         if (!ColumnExists("fem_members", "design_params_json"))
            MigExec("ALTER TABLE fem_members ADD COLUMN design_params_json TEXT");
         if (!ColumnExists("fem_checks", "element_id"))
            MigExec("ALTER TABLE fem_checks ADD COLUMN element_id INTEGER");
         if (!ColumnExists("force_sets", "source_element_id"))
            MigExec("ALTER TABLE force_sets ADD COLUMN source_element_id INTEGER");
      }

      /// <summary>
      /// Миграция v35: fem_elements получает elem_type/section_tag/material_tag/thickness_m —
      /// чтобы представлять не только 2-узловые стержни (дискретизированные из конструктивной
      /// модели), но и 3/4-узловые пластинчатые КЭ, импортированные напрямую из SCAD/Lira.
      /// </summary>
      void MigrateV35()
      {
         if (!ColumnExists("fem_elements", "elem_type"))
            MigExec("ALTER TABLE fem_elements ADD COLUMN elem_type TEXT NOT NULL DEFAULT 'beam'");
         if (!ColumnExists("fem_elements", "section_tag"))
            MigExec("ALTER TABLE fem_elements ADD COLUMN section_tag TEXT");
         if (!ColumnExists("fem_elements", "material_tag"))
            MigExec("ALTER TABLE fem_elements ADD COLUMN material_tag TEXT");
         if (!ColumnExists("fem_elements", "thickness_m"))
            MigExec("ALTER TABLE fem_elements ADD COLUMN thickness_m REAL");
      }

      /// <summary>Миграция v26: tag, force_set_ids_json, calc_type_override в fem_checks.</summary>
      void MigrateV26()
      {
         if (!ColumnExists("fem_checks", "tag"))
            MigExec("ALTER TABLE fem_checks ADD COLUMN tag TEXT NOT NULL DEFAULT ''");
         if (!ColumnExists("fem_checks", "force_set_ids_json"))
            MigExec("ALTER TABLE fem_checks ADD COLUMN force_set_ids_json TEXT NOT NULL DEFAULT '[]'");
         if (!ColumnExists("fem_checks", "calc_type_override"))
            MigExec("ALTER TABLE fem_checks ADD COLUMN calc_type_override TEXT");
      }

      /// <summary>Миграция v24: plate_section_id в fem_members.</summary>
      void MigrateV24()
      {
         if (!ColumnExists("fem_members", "plate_section_id"))
            MigExec("ALTER TABLE fem_members ADD COLUMN plate_section_id INTEGER REFERENCES plate_sections(id)");
      }

      public void ChangeDatabase(string dataSource)
      {
          CheckpointAndClose();
          DeleteWalShm(_dataSource);
          _dataSource = dataSource;
          RepairWal(dataSource);
          DeleteWalShm(dataSource);
          _connection = new SqliteConnection($"Data Source={dataSource}");
          try
          {
             _connection.Open();
          }
          catch (SqliteException ex) when (ex.SqliteErrorCode == 26)
          {
             _connection.Dispose();
             throw new Exception("File is not a valid SQLite database. It may have been corrupted during a previous save operation.");
          }
          SetDeleteJournalMode();
          EnsureCreated();
          Migrate();
      }

      /// <summary>
      /// Закрывает текущее соединение, удаляет файл newPath и открывает его заново как пустую БД.
      /// Используется при создании нового проекта, в том числе когда newPath уже открыт.
      /// </summary>
      public void ReinitializeDatabase(string newPath)
      {
          CheckpointAndClose();
          DeleteWalShm(_dataSource);
          if (File.Exists(newPath)) File.Delete(newPath);
          DeleteWalShm(newPath);
          _dataSource = newPath;
          _connection = new SqliteConnection($"Data Source={newPath}");
          _connection.Open();
          SetDeleteJournalMode();
          EnsureCreated();
          Migrate();
      }
      /// </summary>
      static void RepairWal(string dbPath)
      {
         if (!File.Exists(dbPath)) return;
         try
         {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
            cmd.ExecuteNonQuery();
            conn.Close();
         }
         catch { }
         DeleteWalShm(dbPath);
      }

      private void CheckpointAndClose()
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
         cmd.ExecuteNonQuery();
         _connection.Close();
         SqliteConnection.ClearPool(_connection);
         _connection.Dispose();
      }

      static void DeleteWalShm(string dbPath)
      {
         try { if (File.Exists(dbPath + "-wal")) File.Delete(dbPath + "-wal"); } catch { }
         try { if (File.Exists(dbPath + "-shm")) File.Delete(dbPath + "-shm"); } catch { }
      }

      public void SaveAs(string newPath)
      {
         SaveAll();
         CheckpointAndClose();
         DeleteWalShm(_dataSource);
         File.Copy(_dataSource, newPath, overwrite: true);
         DeleteWalShm(newPath);
         _dataSource = newPath;
         _connection = new SqliteConnection($"Data Source={newPath}");
         _connection.Open();
         SetDeleteJournalMode();
         EnsureCreated();
         Migrate();
      }

      public void SaveAll()
      {
         if (_pendingSave.HasFlag(SaveCategory.Materials))
         {
            foreach (var m in Materials) SaveMaterial(m);
         }
         if (_pendingSave.HasFlag(SaveCategory.Contours))
         {
            foreach (var c in Contours) SaveContour(c);
         }
         if (_pendingSave.HasFlag(SaveCategory.Circles))
         {
            foreach (var c in Circles) SaveCircle(c);
         }
         if (_pendingSave.HasFlag(SaveCategory.Diagrams))
         {
            foreach (var d in Diagrams) SaveDiagram(d);
         }
         if (_pendingSave.HasFlag(SaveCategory.CrossSections))
         {
            foreach (var sec in CrossSections) SaveCrossSection(sec);
         }
         foreach (var fs in ForceSets)
         {
            if (fs.IsModified)
               SaveForceSet(fs);
         }
         if (_pendingSave.HasFlag(SaveCategory.PlateSections))
         {
            foreach (var ps in PlateSections) SavePlateSection(ps);
         }
         if (_pendingSave.HasFlag(SaveCategory.FireSections))
         {
            foreach (var fire in FireSections) SaveFireSection(fire);
         }
         if (_pendingSave.HasFlag(SaveCategory.CalcTasks))
         {
            foreach (var ct in CalcTasks) SaveCalcTask(ct);
         }
         ClearPendingSave();
      }

      /// <summary>Есть ли несохранённые изменения, требующие SaveAll.</summary>
      public bool NeedsSave =>
         _pendingSave != SaveCategory.None || ForceSets.Any(fs => fs.IsModified);

      public void MarkPending(SaveCategory category) => _pendingSave |= category;

      public void ClearPendingSave()
      {
         _pendingSave = SaveCategory.None;
         foreach (var fs in ForceSets)
            fs.IsModified = false;
      }

      internal void ClearCollections()
      {
         Materials.Clear();
         MaterialChars.Clear();
         Contours.Clear();
         Points.Clear();
         Circles.Clear();
         Fibers.Clear();
         Diagrams.Clear();
         CrossSections.Clear();
         ForceSets.Clear();
         PlateSections.Clear();
         FireSections.Clear();
         MaterialAreas.Clear();
         CalcTasks.Clear();
         CalcResults.Clear();
         FemSchemas.Clear();
         FemChecks.Clear();
      }

      #region Load

      /// <summary>
      /// Загружает все данные из базы данных в ObservableCollection-коллекции.
      /// Вызывать после создания сервиса, перед использованием данных.
      /// </summary>
      public void LoadAll()
      {
         LoadMaterials();
         LoadCircles();
         LoadContours();
         LoadDiagrams();
         LoadMaterialAreas();
         ResolveReferencesForStandaloneAreas();
         LoadCrossSections();
         ResolveReferencesForCrossSections();
         LoadForceSets();
         LoadPlateSections();
         LoadFireSections();
         LoadCalcTasks();
         LoadCalcResults();
         LoadFemSchemas();
         LoadFemChecks();
      }

      void LoadMaterials()
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT id, type, tag, description, e, chars_json, aggregate_type, base_type, custom_diagram_ids FROM materials ORDER BY id";
         using var reader = cmd.ExecuteReader();
         while (reader.Read())
         {
            var m = new Material
            {
               Id = reader.GetInt32(0),
               Type = (MatType)reader.GetInt32(1),
               Tag = reader.GetString(2),
               Description = reader.GetString(3),
               E = reader.GetDouble(4),
               AggregateType    = reader.IsDBNull(6) ? "silicate" : reader.GetString(6),
               BaseType         = reader.IsDBNull(7) ? MatType.None : (MatType)reader.GetInt32(7)
            };
            var customIdsJson = reader.IsDBNull(8) ? "{}" : reader.GetString(8);
            var customIds     = JsonSerializer.Deserialize<Dictionary<CalcType, int>>(customIdsJson, _jsonSettings);
            if (customIds != null) m.CustomDiagramIds = customIds;
            var charsJson = reader.GetString(5);
             var chars = JsonSerializer.Deserialize<List<MaterialChars>>(charsJson, _jsonSettings);
            if (chars != null && chars.Count == 4)
               m.MaterialChars = chars;
            Materials.Add(m);
            foreach (var c in m.MaterialChars)
               MaterialChars.Add(c);
         }
      }

      void LoadCircles()
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT id, tag, x, y, diameter, radius, area, type, num, geometry_set FROM circles ORDER BY id";
         using var reader = cmd.ExecuteReader();
         while (reader.Read())
         {
            var cp = new CircleP
            {
               Id = reader.GetInt32(0),
               Tag = reader.GetString(1),
               X = reader.GetDouble(2),
               Y = reader.GetDouble(3),
               Diameter = reader.GetDouble(4),
               Radius = reader.GetDouble(5),
               Area = reader.GetDouble(6),
               Type = (PointType)reader.GetInt32(7),
               Num = reader.GetInt32(8)
            };
            if (!reader.IsDBNull(9)) cp.GeometrySet = reader.GetString(9);
            Circles.Add(cp);
         }
      }

      void LoadContours()
      {
         var cmd = _connection.CreateCommand();
          cmd.CommandText = "SELECT id, tag, wkt, type, geometry_set, points_json FROM contours ORDER BY id";
          using var reader = cmd.ExecuteReader();
          while (reader.Read())
          {
             var c = new Contour
             {
                Id = reader.GetInt32(0),
                Tag = reader.IsDBNull(1) ? "" : reader.GetString(1)
             };
             c.WKT = reader.IsDBNull(2) ? "" : reader.GetString(2);
            if (!string.IsNullOrEmpty(c.WKT))
            {
               WktHelper.ParseWKTPolygon(c.WKT, out var ox, out var oy, out _, out _);
               c.X = ox; c.Y = oy;
            }
            c.Type = (ContourType)reader.GetInt32(3);
            if (!reader.IsDBNull(4)) c.GeometrySet = reader.GetString(4);
            var pointsJson = reader.GetString(5);
             var points = JsonSerializer.Deserialize<List<StressPoint>>(pointsJson, _jsonSettings);
            if (points != null)
               foreach (var p in points) { p.Contour = c; c.Points.Add(p); }
            if (c.X.Count == 0 && c.Points.Count >= 4)
               c.PointsToXYs();
            Contours.Add(c);
            foreach (var p in c.Points) Points.Add(p);
         }
      }

      void LoadCrossSections()
      {
         CrossSections.Clear();

         var sections = new Dictionary<int, CrossSection>();
          using (var cmd = _connection.CreateCommand())
          {
            cmd.CommandText = "SELECT id, num, tag, description, type FROM cross_sections ORDER BY num";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
               CrossSection cs = r.GetString(4) == "two_stage"
                  ? new TwoStageSection()
                  : new CrossSection();
               cs.Id = r.GetInt32(0);
               cs.Num = r.GetInt32(1);
               cs.Tag = r.GetString(2);
               cs.Description = r.IsDBNull(3) ? null : r.GetString(3);
               sections[cs.Id] = cs;
            }
         }

         // Связываем секции с областями из пула через junction-таблицу
         var poolAreaDict = MaterialAreas.ToDictionary(a => a.Id);
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = """
               SELECT section_id, area_id FROM cross_section_areas
               ORDER BY section_id, sort_order
            """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
               int sId = r.GetInt32(0), aId = r.GetInt32(1);
               if (sections.TryGetValue(sId, out var sec) && poolAreaDict.TryGetValue(aId, out var area))
                  sec.Areas.Add(area);
            }
         }

         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = "SELECT section_id, stage1_section_id FROM cross_section_stages";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
               int sId = r.GetInt32(0); int s1Id = r.GetInt32(1);
               if (sections.TryGetValue(sId, out var sec) && sec is TwoStageSection tss
                  && sections.TryGetValue(s1Id, out var stage1))
               {
                  tss.Stage1 = stage1;
                  tss.Stage1SectionId = s1Id;
               }
            }
         }

         // κ1 этапа 1 более не хранится в БД: вычисляется при выполнении расчётной задачи.

         foreach (var sec in sections.Values)
            CrossSections.Add(sec);
      }

      void ResolveReferencesForCrossSections()
      {
         // Материалы и диаграммы областей уже разрешены в пуле (ResolveReferencesForStandaloneAreas).
         // Вызываем ResolveAndBuildDiagramms для правильной привязки HostArea внутри сечения.
         var calc = LoadCalcSettings();
         foreach (var sec in CrossSections)
         {
            sec.ResolveAndBuildDiagramms(calc.Sp63DescEtaMin, pool: Diagrams,
               rebarDifferentialDiagram: calc.RebarDifferentialDiagram);
            if (sec is TwoStageSection tss)
               tss.Stage1.ResolveAndBuildDiagramms(calc.Sp63DescEtaMin, pool: Diagrams,
                  rebarDifferentialDiagram: calc.RebarDifferentialDiagram);
         }
      }

      void LoadMaterialAreas()
      {
         MaterialAreas.Clear();
         using var conn = new SqliteConnection($"Data Source={_dataSource}");
         conn.Open();
         using var cmd = conn.CreateCommand();
         cmd.CommandText = """
            SELECT id, num, tag, description, material_id,
                   host_area_id, diagramm_type, nx, ny, wkt, category, pool_contour_id,
                   mesh_method, mesh_max_area, mesh_min_angle, mesh_max_edge_len, mesh_smooth_iter,
                   sig_sp, gamma_sp
            FROM material_areas
            WHERE section_id IS NULL
            ORDER BY num
         """;
         using var r = cmd.ExecuteReader();
         while (r.Read())
         {
            var area = new MaterialArea
            {
               Id           = r.GetInt32(0),
               Num          = r.GetInt32(1),
               Tag          = r.GetString(2),
               Description  = r.IsDBNull(3) ? null : r.GetString(3),
               MaterialId   = r.IsDBNull(4) ? 0 : r.GetInt32(4),
               HostAreaId   = r.IsDBNull(5) ? null : r.GetInt32(5),
               DiagrammType = Enum.Parse<DiagrammType>(r.GetString(6)),
               NX           = r.GetInt32(7),
               NY           = r.GetInt32(8),
               WKT          = r.IsDBNull(9) ? null : r.GetString(9),
               Category      = Enum.TryParse<AreaCategory>(r.GetString(10), ignoreCase: true, out var cat) ? cat : AreaCategory.RebarGroup,
               PoolContourId = r.IsDBNull(11) ? null : r.GetInt32(11),
               MeshMethod    = Enum.TryParse<CScore.MeshMethod>(r.IsDBNull(12) ? "grid" : r.GetString(12), ignoreCase: true, out var mm) ? mm : CScore.MeshMethod.Grid,
               MeshMaxArea    = r.IsDBNull(13) ? 0.01 : r.GetDouble(13),
               MeshMinAngle   = r.IsDBNull(14) ? 30.0 : r.GetDouble(14),
               MeshMaxEdgeLen = r.IsDBNull(15) ? 0.0  : r.GetDouble(15),
               MeshSmoothIter = r.IsDBNull(16) ? 5    : r.GetInt32(16),
               SigSp          = r.IsDBNull(17) ? 0.0  : r.GetDouble(17),
               GammaSp        = r.IsDBNull(18) ? 1.0  : r.GetDouble(18)
            };
            if (area.WKT != null)
            {
               WktHelper.ParseWKTPolygon(area.WKT,
                  out var outerX, out var outerY, out var holeXs, out var holeYs);
               if (outerX.Count >= 5)
                  area.Contours.Add(new Contour(outerX, outerY, "hull") { Type = ContourType.Hull });
               if (holeXs != null)
                  for (int j = 0; j < holeXs.Count; j++)
                     if (holeXs[j].Count >= 5)
                        area.Contours.Add(new Contour(holeXs[j], holeYs[j], $"hole{j}") { Type = ContourType.Hole });
            }
            MaterialAreas.Add(area);
         }
         LoadPointFibersForAreas(MaterialAreas, conn);
         LoadMeshFibersForAreas(MaterialAreas, conn);
      }

      void LoadPointFibersForAreas(System.Collections.Generic.IEnumerable<MaterialArea> areas, SqliteConnection conn)
      {
         var dict = new System.Collections.Generic.Dictionary<int, MaterialArea>();
         foreach (var a in areas) dict[a.Id] = a;
         if (dict.Count == 0) return;
         using var cmd = conn.CreateCommand();
         cmd.CommandText = $"SELECT area_id, x, y, area, diameter, eps_p FROM point_fibers WHERE area_id IN ({string.Join(",", dict.Keys)})";
         using var r = cmd.ExecuteReader();
         while (r.Read())
         {
            if (!dict.TryGetValue(r.GetInt32(0), out var area)) continue;
            area.Fibers.Add(new Fiber(r.GetDouble(1), r.GetDouble(2))
            {
               Area = r.GetDouble(3), Diameter = r.GetDouble(4),
               Eps_p = r.GetDouble(5), TypeFiber = FiberType.point
            });
         }
      }

      void LoadMeshFibersForAreas(System.Collections.Generic.IEnumerable<MaterialArea> areas, SqliteConnection conn)
      {
         var dict = new System.Collections.Generic.Dictionary<int, MaterialArea>();
         foreach (var a in areas) dict[a.Id] = a;
         if (dict.Count == 0) return;
         using var cmd = conn.CreateCommand();
         cmd.CommandText = $"SELECT area_id, type, x, y, area, wkt, eps_p FROM mesh_fibers WHERE area_id IN ({string.Join(",", dict.Keys)})";
         using var r = cmd.ExecuteReader();
         while (r.Read())
         {
            if (!dict.TryGetValue(r.GetInt32(0), out var area)) continue;
            var fiber = new Fiber(r.GetDouble(2), r.GetDouble(3))
            {
               TypeFiber = Enum.TryParse<FiberType>(r.GetString(1), out var ft) ? ft : FiberType.poly,
               Area      = r.GetDouble(4),
               WKT       = r.IsDBNull(5) ? null : r.GetString(5),
               Eps_p     = r.GetDouble(6)
            };
            area.Fibers.Add(fiber);
         }
      }

      void ResolveReferencesForStandaloneAreas()
      {
         var calc = LoadCalcSettings();
         foreach (var area in MaterialAreas)
         {
            area.Material = Materials.FirstOrDefault(m => m.Id == area.MaterialId);
            if (area.HostAreaId != null)
               area.HostArea = MaterialAreas.FirstOrDefault(a => a.Id == area.HostAreaId);
            if (area.PoolContourId != null)
            {
               var pc = Contours.FirstOrDefault(c => c.Id == area.PoolContourId);
               if (pc != null)
               {
                  area.PoolContour = pc;
                  area.Hull = pc;
               }
            }
            area.ResolveAndBuildDiagramms(calc.Sp63DescEtaMin, pool: Diagrams,
               rebarDifferentialDiagram: calc.RebarDifferentialDiagram);
         }
      }

      public void SaveMaterialArea(MaterialArea area)
      {
         using var conn = new SqliteConnection($"Data Source={_dataSource}");
         conn.Open();
         // В отличие от _connection, это отдельное соединение открывается заново на каждый вызов
         // и по умолчанию (в отличие от _connection) имеет PRAGMA foreign_keys=ON (значение по
         // умолчанию у e_sqlite3), из-за чего вставка MaterialArea со ссылкой на ещё не
         // сохранённые material/host_area/contour падала с "FOREIGN KEY constraint failed".
         using (var fkCmd = conn.CreateCommand())
         {
            fkCmd.CommandText = "PRAGMA foreign_keys=OFF";
            fkCmd.ExecuteNonQuery();
         }
         using var tx = conn.BeginTransaction();
         bool isNew = area.Id == 0;
         using (var cmd = conn.CreateCommand())
         {
            if (isNew)
            {
               cmd.CommandText = """
                  INSERT INTO material_areas
                     (num, tag, description, material_id, host_area_id,
                      diagramm_type, nx, ny, wkt, category, pool_contour_id,
                      mesh_method, mesh_max_area, mesh_min_angle, mesh_max_edge_len, mesh_smooth_iter,
                      sig_sp, gamma_sp)
                  VALUES (@num,@tag,@desc,@mid,@hid,@dtype,@nx,@ny,@wkt,@cat,@pcid,
                          @mmethod,@mmaxarea,@mminangle,@mmaxedge,@msmoothiter,
                          @sigsp,@gammasp);
                  SELECT last_insert_rowid();
               """;
            }
            else
            {
               cmd.CommandText = """
                  UPDATE material_areas SET
                     num=@num, tag=@tag, description=@desc, material_id=@mid,
                     host_area_id=@hid, diagramm_type=@dtype, nx=@nx, ny=@ny,
                     wkt=@wkt, category=@cat, pool_contour_id=@pcid,
                     mesh_method=@mmethod, mesh_max_area=@mmaxarea, mesh_min_angle=@mminangle,
                     mesh_max_edge_len=@mmaxedge, mesh_smooth_iter=@msmoothiter,
                     sig_sp=@sigsp, gamma_sp=@gammasp
                  WHERE id=@id;
               """;
               cmd.Parameters.AddWithValue("@id", area.Id);
            }
            cmd.Parameters.AddWithValue("@num",   area.Num);
            cmd.Parameters.AddWithValue("@tag",   area.Tag);
            cmd.Parameters.AddWithValue("@desc",  (object?)area.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@mid",   area.MaterialId == 0 ? DBNull.Value : (object)area.MaterialId);
            cmd.Parameters.AddWithValue("@hid",   (object?)area.HostAreaId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@dtype", area.DiagrammType.ToString());
            cmd.Parameters.AddWithValue("@nx",    area.NX);
            cmd.Parameters.AddWithValue("@ny",    area.NY);
            cmd.Parameters.AddWithValue("@wkt",   (object?)area.WKT ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@cat",   area.Category.ToString().ToLowerInvariant());
            cmd.Parameters.AddWithValue("@pcid",  (object?)area.PoolContourId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@mmethod",    area.MeshMethod.ToString().ToLowerInvariant());
            cmd.Parameters.AddWithValue("@mmaxarea",    area.MeshMaxArea);
            cmd.Parameters.AddWithValue("@mminangle",   area.MeshMinAngle);
            cmd.Parameters.AddWithValue("@mmaxedge",    area.MeshMaxEdgeLen);
            cmd.Parameters.AddWithValue("@msmoothiter", area.MeshSmoothIter);
            cmd.Parameters.AddWithValue("@sigsp",       area.SigSp);
            cmd.Parameters.AddWithValue("@gammasp",     area.GammaSp);
            if (isNew) area.Id = (int)(long)cmd.ExecuteScalar()!;
            else cmd.ExecuteNonQuery();
         }
         using (var cmd = conn.CreateCommand())
         {
            cmd.CommandText = "DELETE FROM point_fibers WHERE area_id = @aid";
            cmd.Parameters.AddWithValue("@aid", area.Id);
            cmd.ExecuteNonQuery();
         }
         foreach (var f in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
         {
            using var fc = conn.CreateCommand();
            fc.CommandText = "INSERT INTO point_fibers(area_id,x,y,area,diameter,eps_p) VALUES(@aid,@x,@y,@a,@d,@ep)";
            fc.Parameters.AddWithValue("@aid", area.Id);
            fc.Parameters.AddWithValue("@x",   f.X);
            fc.Parameters.AddWithValue("@y",   f.Y);
            fc.Parameters.AddWithValue("@a",   f.Area);
            fc.Parameters.AddWithValue("@d",   f.Diameter);
            fc.Parameters.AddWithValue("@ep",  f.Eps_p);
            fc.ExecuteNonQuery();
         }
         tx.Commit();
         if (isNew && !MaterialAreas.Contains(area))
            MaterialAreas.Add(area);
      }

      public void DeleteMaterialArea(MaterialArea area)
      {
         if (area.Id == 0) { MaterialAreas.Remove(area); return; }
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "DELETE FROM material_areas WHERE id = @id";
         cmd.Parameters.AddWithValue("@id", area.Id);
         cmd.ExecuteNonQuery();
         MaterialAreas.Remove(area);
      }

      /// <summary>
      /// Сохраняет сеточные волокна (poly/tri) области: обновляет параметры сетки
      /// в material_areas, удаляет старые записи mesh_fibers, добавляет новые.
      /// </summary>
      public void SaveMeshFibers(MaterialArea area)
      {
         if (area.Id == 0) return;
         using var tx = _connection.BeginTransaction();

         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = """
               UPDATE material_areas
               SET mesh_method=@mm, mesh_max_area=@ma, mesh_min_angle=@mi,
                   mesh_max_edge_len=@me, mesh_smooth_iter=@ms
               WHERE id=@id
            """;
            cmd.Parameters.AddWithValue("@id", area.Id);
            cmd.Parameters.AddWithValue("@mm", area.MeshMethod.ToString().ToLowerInvariant());
            cmd.Parameters.AddWithValue("@ma", area.MeshMaxArea);
            cmd.Parameters.AddWithValue("@mi", area.MeshMinAngle);
            cmd.Parameters.AddWithValue("@me", area.MeshMaxEdgeLen);
            cmd.Parameters.AddWithValue("@ms", area.MeshSmoothIter);
            cmd.ExecuteNonQuery();
         }

         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = "DELETE FROM mesh_fibers WHERE area_id=@aid";
            cmd.Parameters.AddWithValue("@aid", area.Id);
            cmd.ExecuteNonQuery();
         }

         foreach (var f in area.Fibers.Where(f => f.TypeFiber is FiberType.poly or FiberType.tri))
         {
            using var fc = _connection.CreateCommand();
            fc.CommandText = "INSERT INTO mesh_fibers(area_id,type,x,y,area,wkt,eps_p) VALUES(@aid,@t,@x,@y,@a,@wkt,@ep)";
            fc.Parameters.AddWithValue("@aid", area.Id);
            fc.Parameters.AddWithValue("@t",   f.TypeFiber.ToString());
            fc.Parameters.AddWithValue("@x",   f.X);
            fc.Parameters.AddWithValue("@y",   f.Y);
            fc.Parameters.AddWithValue("@a",   f.Area);
            fc.Parameters.AddWithValue("@wkt", (object?)f.WKT ?? DBNull.Value);
            fc.Parameters.AddWithValue("@ep",  f.Eps_p);
            fc.ExecuteNonQuery();
         }

         tx.Commit();
      }

      void LoadDiagrams()
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT id, tag, type, material_type, calc_type, spline_data_json FROM diagrams ORDER BY id";
         using var reader = cmd.ExecuteReader();
         while (reader.Read())
         {
            var id = reader.GetInt32(0);
            var tag = reader.GetString(1);
            var type = (DiagrammType)reader.GetInt32(2);
            var matType = (MatType)reader.GetInt32(3);
            var calcType = (CalcType)reader.GetInt32(4);
            var splineJson = reader.GetString(5);
             var sd = JsonSerializer.Deserialize<SplineDataJson>(splineJson, _jsonSettings);
            var d = new Diagramm
            {
               Id = id,
               Tag = tag,
               Type = type,
               MaterialType = matType,
               CalcType = calcType,
             Ic = RebuildSpline(sd?.Compression)!,
                It = RebuildSpline(sd?.Tension)!,
               CharacteristicStrains = sd?.CharacteristicStrains ?? new List<double>()
            };
            Diagrams.Add(d);
         }
      }

      static ISpline? RebuildSpline(SplineBranchJson? branch)
      {
         if (branch?.X == null || branch.Y == null) return null;
         return branch.SplineType == "HSpline" && branch.DY != null
            ? new HSpline(branch.X, branch.Y, branch.DY)
            : new LSpline(branch.X, branch.Y) as ISpline;
      }

      #endregion

      #region Save

      public void SaveDiagram(Diagramm d)
      {
         var sd = new SplineDataJson
         {
            Compression = ExtractSpline(d.Ic),
            Tension = ExtractSpline(d.It),
            CharacteristicStrains = d.CharacteristicStrains
         };
          var splineJson = JsonSerializer.Serialize(sd, _jsonSettings);
         var cmd = _connection.CreateCommand();
          if (d.Id == 0)
          {
             cmd.CommandText = @"INSERT INTO diagrams (tag, type, material_type, calc_type, spline_data_json)
                                 VALUES ($tag, $type, $mt, $ct, $spl);
                                 SELECT last_insert_rowid();";
          }
          else
          {
             cmd.CommandText = @"UPDATE diagrams SET tag=$tag, type=$type, material_type=$mt,
                                 calc_type=$ct, spline_data_json=$spl WHERE id=$id";
             cmd.Parameters.AddWithValue("$id", d.Id);
          }
          cmd.Parameters.AddWithValue("$tag", d.Tag ?? "");
          cmd.Parameters.AddWithValue("$type", (int)d.Type);
          cmd.Parameters.AddWithValue("$mt", (int)d.MaterialType);
          cmd.Parameters.AddWithValue("$ct", (int)d.CalcType);
          cmd.Parameters.AddWithValue("$spl", splineJson);
         if (d.Id == 0)
            d.Id = Convert.ToInt32(cmd.ExecuteScalar());
         else
            cmd.ExecuteNonQuery();
      }

      public void DeleteDiagram(Diagramm d)
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "DELETE FROM diagrams WHERE id=$id";
         cmd.Parameters.AddWithValue("$id", d.Id);
         cmd.ExecuteNonQuery();
      }

      static SplineBranchJson? ExtractSpline(ISpline? spline)
      {
         if (spline == null) return null;
         return new SplineBranchJson
         {
            SplineType = spline is HSpline ? "HSpline" : "LSpline",
            X = spline.X,
            Y = spline.Y,
            DY = spline.DY
         };
      }

      public void SaveMaterial(Material m)
      {
         var cmd = _connection.CreateCommand();
         if (m.Id == 0)
         {
            cmd.CommandText = @"INSERT INTO materials (type, tag, description, e, chars_json, aggregate_type, base_type, custom_diagram_ids)
                               VALUES ($type, $tag, $desc, $e, $chars, $agg, $bt, $cdi);
                               SELECT last_insert_rowid();";
         }
         else
         {
            cmd.CommandText = @"UPDATE materials SET type=$type, tag=$tag, description=$desc, e=$e, chars_json=$chars,
                               aggregate_type=$agg, base_type=$bt, custom_diagram_ids=$cdi
                               WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", m.Id);
         }
         cmd.Parameters.AddWithValue("$type", (int)m.Type);
         cmd.Parameters.AddWithValue("$tag", m.Tag ?? "");
         cmd.Parameters.AddWithValue("$desc", m.Description ?? "");
         cmd.Parameters.AddWithValue("$e", m.E);
          cmd.Parameters.AddWithValue("$chars", JsonSerializer.Serialize(m.MaterialChars, _jsonSettings));
         cmd.Parameters.AddWithValue("$agg", string.IsNullOrWhiteSpace(m.AggregateType) ? "silicate" : m.AggregateType);
         cmd.Parameters.AddWithValue("$bt",  (int)m.BaseType);
         cmd.Parameters.AddWithValue("$cdi", JsonSerializer.Serialize(m.CustomDiagramIds, _jsonSettings));
         if (m.Id == 0)
            m.Id = Convert.ToInt32(cmd.ExecuteScalar());
         else
            cmd.ExecuteNonQuery();
      }

      public void DeleteMaterial(Material m)
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "DELETE FROM materials WHERE id=$id";
         cmd.Parameters.AddWithValue("$id", m.Id);
         cmd.ExecuteNonQuery();
         Materials.Remove(m);
         foreach (var chars in m.MaterialChars.ToList())
            MaterialChars.Remove(chars);
      }

      public void SaveContour(Contour c)
      {
         var cmd = _connection.CreateCommand();
         var pointsJson = JsonSerializer.Serialize(c.Points.ToList(), _jsonSettings);

          if (c.Id == 0)
          {
             cmd.CommandText = @"INSERT INTO contours (tag, wkt, type, geometry_set, points_json, regions_json)
                                VALUES ($tag, $wkt, $type, $gset, $pjson, '[]');
                                SELECT last_insert_rowid();";
          }
          else
          {
             cmd.CommandText = @"UPDATE contours SET tag=$tag, wkt=$wkt, type=$type, geometry_set=$gset,
                                points_json=$pjson WHERE id=$id";
             cmd.Parameters.AddWithValue("$id", c.Id);
          }
          cmd.Parameters.AddWithValue("$tag", c.Tag ?? "");
          cmd.Parameters.AddWithValue("$wkt", c.WKT ?? "");
          cmd.Parameters.AddWithValue("$type", (int)c.Type);
          cmd.Parameters.AddWithValue("$gset", (object?)c.GeometrySet ?? DBNull.Value);
          cmd.Parameters.AddWithValue("$pjson", pointsJson);
         if (c.Id == 0)
            c.Id = Convert.ToInt32(cmd.ExecuteScalar());
         else
            cmd.ExecuteNonQuery();
      }

      public void DeleteContour(Contour c)
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "DELETE FROM contours WHERE id=$id";
         cmd.Parameters.AddWithValue("$id", c.Id);
         cmd.ExecuteNonQuery();
      }

      public void SaveCircle(CircleP c)
      {
         var cmd = _connection.CreateCommand();
         if (c.Id == 0)
         {
            cmd.CommandText = @"INSERT INTO circles (tag, x, y, diameter, radius, area, type, num, geometry_set)
                               VALUES ($tag, $x, $y, $dia, $rad, $area, $type, $num, $gset);
                               SELECT last_insert_rowid();";
         }
         else
         {
            cmd.CommandText = @"UPDATE circles SET tag=$tag, x=$x, y=$y, diameter=$dia, radius=$rad,
                               area=$area, type=$type, num=$num, geometry_set=$gset WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", c.Id);
         }
         cmd.Parameters.AddWithValue("$tag", c.Tag ?? "");
         cmd.Parameters.AddWithValue("$x", c.X);
         cmd.Parameters.AddWithValue("$y", c.Y);
         cmd.Parameters.AddWithValue("$dia", c.Diameter);
         cmd.Parameters.AddWithValue("$rad", c.Radius);
         cmd.Parameters.AddWithValue("$area", c.Area);
         cmd.Parameters.AddWithValue("$type", (int)c.Type);
         cmd.Parameters.AddWithValue("$num", c.Num);
         cmd.Parameters.AddWithValue("$gset", c.GeometrySet is null ? (object)DBNull.Value : c.GeometrySet);
         if (c.Id == 0)
            c.Id = Convert.ToInt32(cmd.ExecuteScalar());
         else
            cmd.ExecuteNonQuery();
      }

      public void DeleteCircle(CircleP c)
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "DELETE FROM circles WHERE id=$id";
         cmd.Parameters.AddWithValue("$id", c.Id);
         cmd.ExecuteNonQuery();
      }

      public void SaveCrossSection(CrossSection section)
      {
         using var tx = _connection.BeginTransaction();
         try
         {
            SaveCrossSectionCore(section);
            tx.Commit();
         }
         catch
         {
            tx.Rollback();
            throw;
         }
      }

      void SaveCrossSectionCore(CrossSection section)
      {
         using var cmd = _connection.CreateCommand();
         bool isNew = section.Id == 0;
         if (isNew)
         {
            cmd.CommandText = """
               INSERT INTO cross_sections (num, tag, description, type)
               VALUES (@num, @tag, @desc, @type);
               SELECT last_insert_rowid();
            """;
         }
         else
         {
            cmd.CommandText = """
               UPDATE cross_sections SET num=@num, tag=@tag, description=@desc, type=@type
               WHERE id=@id;
            """;
            cmd.Parameters.AddWithValue("@id", section.Id);
         }
         cmd.Parameters.AddWithValue("@num", section.Num);
         cmd.Parameters.AddWithValue("@tag", section.Tag);
         cmd.Parameters.AddWithValue("@desc", (object?)section.Description ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@type", section is TwoStageSection ? "two_stage" : "simple");
         if (isNew) section.Id = (int)(long)cmd.ExecuteScalar()!;
         else cmd.ExecuteNonQuery();

         // Сохраняем список ссылок на области через junction-таблицу
         SaveSectionAreaJunction(section);

         if (section is TwoStageSection tss)
         {
            SaveCrossSectionCore(tss.Stage1);

            using var stageCmd = _connection.CreateCommand();
            stageCmd.CommandText = """
               DELETE FROM cross_section_stages WHERE section_id = @sid;
               INSERT INTO cross_section_stages (section_id, stage1_section_id)
               VALUES (@sid, @s1id);
            """;
            stageCmd.Parameters.AddWithValue("@sid", tss.Id);
            stageCmd.Parameters.AddWithValue("@s1id", tss.Stage1.Id);
            // κ1 этапа 1 не сохраняем — она вычисляется при расчёте.
            stageCmd.ExecuteNonQuery();
         }
      }

      void SaveSectionAreaJunction(CrossSection section)
      {
         using var delCmd = _connection.CreateCommand();
         delCmd.CommandText = "DELETE FROM cross_section_areas WHERE section_id = @sid";
         delCmd.Parameters.AddWithValue("@sid", section.Id);
         delCmd.ExecuteNonQuery();

         for (int i = 0; i < section.Areas.Count; i++)
         {
            var area = section.Areas[i];
            if (area.Id == 0) continue; // область не сохранена в пуле — пропускаем
            using var ins = _connection.CreateCommand();
            ins.CommandText = """
               INSERT OR IGNORE INTO cross_section_areas (section_id, area_id, sort_order)
               VALUES (@sid, @aid, @ord);
            """;
            ins.Parameters.AddWithValue("@sid", section.Id);
            ins.Parameters.AddWithValue("@aid", area.Id);
            ins.Parameters.AddWithValue("@ord", i);
            ins.ExecuteNonQuery();
         }
      }

      public void DeleteCrossSection(CrossSection section)
      {
         if (section.Id == 0) return;
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "DELETE FROM cross_sections WHERE id = @id";
         cmd.Parameters.AddWithValue("@id", section.Id);
         cmd.ExecuteNonQuery();
         CrossSections.Remove(section);
      }

      #endregion

      #region ForceSets

      void LoadForceSets()
      {
         ForceSets.Clear();
         var sets = new Dictionary<int, ForceSet>();
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = "SELECT id, num, tag, description, kind, source_type, source_schema_id, source_element_tag, source_member_id, source_element_id FROM force_sets ORDER BY num";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
               var fs = new ForceSet
               {
                  Id               = r.GetInt32(0),
                  Num              = r.GetInt32(1),
                  Tag              = r.GetString(2),
                  Description      = r.IsDBNull(3) ? null : r.GetString(3),
                  Kind             = r.IsDBNull(4) ? "bar" : r.GetString(4),
                  SourceType       = r.IsDBNull(5) ? null : r.GetString(5),
                  SourceSchemaId   = r.IsDBNull(6) ? null : r.GetInt32(6),
                  SourceElementTag = r.IsDBNull(7) ? null : r.GetString(7),
                  SourceMemberId   = r.IsDBNull(8) ? null : r.GetInt32(8),
                  SourceElementId  = r.IsDBNull(9) ? null : r.GetInt32(9)
               };
               sets[fs.Id] = fs;
            }
          }
          using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = "SELECT id, set_id, num, label, n, mx, my, vx, vy, t FROM force_items ORDER BY set_id, num";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
               int setId = r.GetInt32(1);
               if (!sets.TryGetValue(setId, out var fs)) continue;
               fs.Items.Add(new LoadItem
               {
                  Id    = r.GetInt32(0),
                  Num   = r.GetInt32(2),
                  Label = r.GetString(3),
                  N     = r.GetDouble(4),
                  Mx    = r.GetDouble(5),
                  My    = r.GetDouble(6),
                  Vx    = r.GetDouble(7),
                  Vy    = r.GetDouble(8),
                  T     = r.GetDouble(9)
               });
            }
         }
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = "SELECT id, set_id, num, label, nx, ny, nxy, mx, my, mxy, qx, qy FROM force_shell_items ORDER BY set_id, num";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
               int setId = r.GetInt32(1);
               if (!sets.TryGetValue(setId, out var fs)) continue;
               fs.ShellItems.Add(new ShellLoadItem
               {
                  Id    = r.GetInt32(0),
                  Num   = r.GetInt32(2),
                  Label = r.GetString(3),
                  Nx    = r.GetDouble(4),
                  Ny    = r.GetDouble(5),
                  Nxy   = r.GetDouble(6),
                  Mx    = r.GetDouble(7),
                  My    = r.GetDouble(8),
                  Mxy   = r.GetDouble(9),
                  Qx    = r.GetDouble(10),
                  Qy    = r.GetDouble(11),
               });
            }
         }
         foreach (var fs in sets.Values)
         {
            fs.IsModified = false;
            ForceSets.Add(fs);
         }
      }

      public void SaveForceSet(ForceSet fs)
      {
         using var tx = _connection.BeginTransaction();
         try
         {
            SaveForceSetCore(fs);
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }

      void SaveForceSetCore(ForceSet fs)
      {
         using var cmd = _connection.CreateCommand();
         bool isNew = fs.Id == 0;
         if (isNew)
         {
            cmd.CommandText = """
               INSERT INTO force_sets (num, tag, description, kind, source_type, source_schema_id, source_element_tag, source_member_id, source_element_id)
               VALUES (@num, @tag, @desc, @kind, @stype, @ssid, @setag, @smid, @seid);
               SELECT last_insert_rowid();
            """;
         }
         else
         {
            cmd.CommandText = """
               UPDATE force_sets SET num=@num, tag=@tag, description=@desc, kind=@kind,
               source_type=@stype, source_schema_id=@ssid, source_element_tag=@setag, source_member_id=@smid,
               source_element_id=@seid WHERE id=@id
            """;
            cmd.Parameters.AddWithValue("@id", fs.Id);
         }
         cmd.Parameters.AddWithValue("@num",   fs.Num);
         cmd.Parameters.AddWithValue("@tag",   fs.Tag);
         cmd.Parameters.AddWithValue("@desc",  (object?)fs.Description      ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@kind",  fs.Kind);
         cmd.Parameters.AddWithValue("@stype", (object?)fs.SourceType       ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@ssid",  (object?)fs.SourceSchemaId   ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@setag", (object?)fs.SourceElementTag ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@smid",  (object?)fs.SourceMemberId   ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@seid",  (object?)fs.SourceElementId  ?? DBNull.Value);
         if (isNew) fs.Id = (int)(long)cmd.ExecuteScalar()!;
         else cmd.ExecuteNonQuery();

         using var delCmd = _connection.CreateCommand();
         delCmd.CommandText = "DELETE FROM force_items WHERE set_id = @sid";
         delCmd.Parameters.AddWithValue("@sid", fs.Id);
         delCmd.ExecuteNonQuery();

         for (int i = 0; i < fs.Items.Count; i++)
         {
            var item = fs.Items[i];
            item.Num = i + 1;
            using var ins = _connection.CreateCommand();
            // Сохраняем существующий id, чтобы CalcTask.ForceItemId оставался валидным
            if (item.Id != 0)
            {
               ins.CommandText = """
                  INSERT INTO force_items (id, set_id, num, label, n, mx, my, vx, vy, t)
                  VALUES (@id, @sid, @num, @lbl, @n, @mx, @my, @vx, @vy, @t);
                  SELECT last_insert_rowid();
               """;
               ins.Parameters.AddWithValue("@id", item.Id);
            }
            else
            {
               ins.CommandText = """
                  INSERT INTO force_items (set_id, num, label, n, mx, my, vx, vy, t)
                  VALUES (@sid, @num, @lbl, @n, @mx, @my, @vx, @vy, @t);
                  SELECT last_insert_rowid();
               """;
            }
            ins.Parameters.AddWithValue("@sid", fs.Id);
            ins.Parameters.AddWithValue("@num", item.Num);
            ins.Parameters.AddWithValue("@lbl", item.Label);
            ins.Parameters.AddWithValue("@n",   item.N);
            ins.Parameters.AddWithValue("@mx",  item.Mx);
            ins.Parameters.AddWithValue("@my",  item.My);
            ins.Parameters.AddWithValue("@vx",  item.Vx);
            ins.Parameters.AddWithValue("@vy",  item.Vy);
            ins.Parameters.AddWithValue("@t",   item.T);
            item.Id = (int)(long)ins.ExecuteScalar()!;
         }

         using var delShell = _connection.CreateCommand();
         delShell.CommandText = "DELETE FROM force_shell_items WHERE set_id = @sid";
         delShell.Parameters.AddWithValue("@sid", fs.Id);
         delShell.ExecuteNonQuery();

         for (int i = 0; i < fs.ShellItems.Count; i++)
         {
            var item = fs.ShellItems[i];
            item.Num = i + 1;
            using var ins = _connection.CreateCommand();
            if (item.Id != 0)
            {
               ins.CommandText = """
                  INSERT INTO force_shell_items (id, set_id, num, label, nx, ny, nxy, mx, my, mxy, qx, qy)
                  VALUES (@id, @sid, @num, @lbl, @nx, @ny, @nxy, @mx, @my, @mxy, @qx, @qy);
                  SELECT last_insert_rowid();
               """;
               ins.Parameters.AddWithValue("@id", item.Id);
            }
            else
            {
               ins.CommandText = """
                  INSERT INTO force_shell_items (set_id, num, label, nx, ny, nxy, mx, my, mxy, qx, qy)
                  VALUES (@sid, @num, @lbl, @nx, @ny, @nxy, @mx, @my, @mxy, @qx, @qy);
                  SELECT last_insert_rowid();
               """;
            }
            ins.Parameters.AddWithValue("@sid", fs.Id);
            ins.Parameters.AddWithValue("@num", item.Num);
            ins.Parameters.AddWithValue("@lbl", item.Label);
            ins.Parameters.AddWithValue("@nx",  item.Nx);
            ins.Parameters.AddWithValue("@ny",  item.Ny);
            ins.Parameters.AddWithValue("@nxy", item.Nxy);
            ins.Parameters.AddWithValue("@mx",  item.Mx);
            ins.Parameters.AddWithValue("@my",  item.My);
            ins.Parameters.AddWithValue("@mxy", item.Mxy);
            ins.Parameters.AddWithValue("@qx",  item.Qx);
            ins.Parameters.AddWithValue("@qy",  item.Qy);
            item.Id = (int)(long)ins.ExecuteScalar()!;
         }
         fs.IsModified = false;
      }

      public void DeleteForceSet(ForceSet fs)
      {
         if (fs.Id == 0) return;
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "DELETE FROM force_sets WHERE id = @id";
         cmd.Parameters.AddWithValue("@id", fs.Id);
         cmd.ExecuteNonQuery();
         ForceSets.Remove(fs);
      }

      #endregion

      #region Maintenance

      public void Vacuum()
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "VACUUM";
         cmd.ExecuteNonQuery();
      }

      public long GetDbSizeBytes()
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT page_count * page_size FROM pragma_page_count(), pragma_page_size()";
         return (long)(cmd.ExecuteScalar() ?? 0L);
      }

      #endregion

      #region PlateSections

      void LoadPlateSections()
      {
         PlateSections.Clear();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT id, num, tag, description, h, n_layers,
                   concrete_material_id, rebar_material_id,
                   tension_concrete, softening_model, softening_eps_c2,
                   plate_model, concrete_diagram_type, rebar_layers_json
            FROM plate_sections ORDER BY num
         """;
         using var r = cmd.ExecuteReader();
         while (r.Read())
         {
            var ps = new PlateSection
            {
               Id                  = r.GetInt32(0),
               Num                 = r.GetInt32(1),
               Tag                 = r.GetString(2),
               H                   = r.GetDouble(4),
               NLayers             = r.GetInt32(5),
               ConcreteMaterialId  = r.GetInt32(6),
               RebarMaterialId     = r.GetInt32(7),
               TensionConcrete     = r.GetInt32(8) != 0,
               SofteningModel      = r.GetString(9),
               SofteningEpsC2      = r.GetDouble(10),
               PlateModel          = r.GetString(11),
               ConcreteDiagramType = Enum.TryParse<DiagrammType>(r.GetString(12), out var cdt)
                                     ? cdt : DiagrammType.L3,
            };
            var layersJson = r.GetString(13);
            var layers = JsonSerializer.Deserialize<List<PlateRebarLayer>>(layersJson, _jsonSettings);
            if (layers != null) ps.RebarLayers = layers;
            PlateSections.Add(ps);
         }
      }

      public void SavePlateSection(PlateSection ps)
      {
         var layersJson = JsonSerializer.Serialize(ps.RebarLayers, _jsonSettings);
         using var cmd = _connection.CreateCommand();
         bool isNew = ps.Id == 0;
         if (isNew)
         {
            cmd.CommandText = """
               INSERT INTO plate_sections
                  (num, tag, description, h, n_layers,
                   concrete_material_id, rebar_material_id,
                   tension_concrete, softening_model, softening_eps_c2,
                   plate_model, concrete_diagram_type, rebar_layers_json)
               VALUES (@num,@tag,@desc,@h,@nl,@cmid,@rmid,@tc,@sm,@sec2,@pm,@cdtype,@rlj);
               SELECT last_insert_rowid();
            """;
         }
         else
         {
            cmd.CommandText = """
               UPDATE plate_sections SET
                  num=@num, tag=@tag, description=@desc, h=@h, n_layers=@nl,
                  concrete_material_id=@cmid, rebar_material_id=@rmid,
                  tension_concrete=@tc, softening_model=@sm, softening_eps_c2=@sec2,
                  plate_model=@pm, concrete_diagram_type=@cdtype, rebar_layers_json=@rlj
               WHERE id=@id;
            """;
            cmd.Parameters.AddWithValue("@id", ps.Id);
         }
         cmd.Parameters.AddWithValue("@num",  ps.Num);
         cmd.Parameters.AddWithValue("@tag",  ps.Tag);
         cmd.Parameters.AddWithValue("@desc", (object?)null ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@h",    ps.H);
         cmd.Parameters.AddWithValue("@nl",   ps.NLayers);
         cmd.Parameters.AddWithValue("@cmid", ps.ConcreteMaterialId);
         cmd.Parameters.AddWithValue("@rmid", ps.RebarMaterialId);
         cmd.Parameters.AddWithValue("@tc",   ps.TensionConcrete ? 1 : 0);
         cmd.Parameters.AddWithValue("@sm",   ps.SofteningModel);
         cmd.Parameters.AddWithValue("@sec2", ps.SofteningEpsC2);
         cmd.Parameters.AddWithValue("@pm",
            string.IsNullOrEmpty(ps.PlateModel) ? "layered" : ps.PlateModel);
         cmd.Parameters.AddWithValue("@cdtype", ps.ConcreteDiagramType.ToString());
         cmd.Parameters.AddWithValue("@rlj",  layersJson);
         if (isNew) ps.Id = (int)(long)cmd.ExecuteScalar()!;
         else cmd.ExecuteNonQuery();

         if (isNew && !PlateSections.Contains(ps))
            PlateSections.Add(ps);
      }

      public void DeletePlateSection(PlateSection ps)
      {
         if (ps.Id == 0) { PlateSections.Remove(ps); return; }
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "DELETE FROM plate_sections WHERE id = @id";
         cmd.Parameters.AddWithValue("@id", ps.Id);
         cmd.ExecuteNonQuery();
         PlateSections.Remove(ps);
      }

      #endregion

      #region FireSections

      void LoadFireSections()
      {
         FireSections.Clear();
         var dict = new Dictionary<int, FireSectionDef>();
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = """
               SELECT id, num, tag, section_id, fire_duration_min, fire_curve,
                      mesh_step_m, time_step_s, theta, picard_tol_celsius, picard_max_iter,
                      snapshot_step_min, bc_preset, hole_bc_preset, algorithm, smooth_iter_tri,
                      aggregate_type, mesh_element_type
               FROM fire_sections
               ORDER BY num, id
            """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
               var fs = new FireSectionDef
               {
                  Id = r.GetInt32(0),
                  Num = r.GetInt32(1),
                  Tag = r.GetString(2),
                  SectionId = r.GetInt32(3),
                  FireDurationMin = r.GetDouble(4),
                  FireCurve = r.GetString(5),
                  MeshStepM = r.GetDouble(6),
                  TimeStepS = r.GetDouble(7),
                  Theta = r.GetDouble(8),
                  PicardTolCelsius = r.GetDouble(9),
                  PicardMaxIter = r.GetInt32(10),
                  SnapshotStepMin = r.GetDouble(11),
                  BcPreset = r.GetString(12),
                  HoleBcPreset = r.GetString(13),
                  Algorithm = r.GetString(14),
                  SmoothIterTri = r.GetInt32(15),
                  AggregateType = r.IsDBNull(16) ? "" : r.GetString(16),
                  MeshElementType = r.IsDBNull(17) ? "linear" : r.GetString(17)
               };
               dict[fs.Id] = fs;
            }
         }

         if (dict.Count > 0)
         {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
               SELECT fire_section_id, edge_index, contour_type, hole_index, bc_type, alpha_conv, emissivity, t_ambient
               FROM fire_section_edges
               ORDER BY fire_section_id, contour_type, hole_index, edge_index, id
            """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
               int sectionId = r.GetInt32(0);
               if (!dict.TryGetValue(sectionId, out var fs)) continue;
               fs.Edges.Add(new FireBoundaryEdgeDef
               {
                  EdgeIndex = r.GetInt32(1),
                  ContourType = r.GetString(2),
                  HoleIndex = r.IsDBNull(3) ? null : r.GetInt32(3),
                  BcType = r.GetString(4),
                  AlphaConv = r.GetDouble(5),
                  Emissivity = r.GetDouble(6),
                  TAmbientCelsius = r.GetDouble(7)
               });
            }
         }

         foreach (var fs in dict.Values.OrderBy(x => x.Num).ThenBy(x => x.Id))
            FireSections.Add(fs);
      }

      /// <summary>
      /// Сохраняет огневое сечение в БД (INSERT/UPDATE) и полностью пересохраняет его граничные рёбра.
      /// </summary>
      public void SaveFireSection(FireSectionDef fs)
      {
         using var tx = _connection.BeginTransaction();
         try
         {
            bool isNew = fs.Id == 0;
            using (var cmd = _connection.CreateCommand())
            {
               if (isNew)
               {
                  cmd.CommandText = """
                     INSERT INTO fire_sections
                        (num, tag, section_id, fire_duration_min, fire_curve, mesh_step_m, time_step_s,
                         theta, picard_tol_celsius, picard_max_iter, snapshot_step_min, bc_preset,
                         hole_bc_preset, algorithm, smooth_iter_tri, aggregate_type, mesh_element_type)
                     VALUES
                        (@num, @tag, @sid, @dur, @curve, @mesh, @dt, @theta, @ptol, @piter, @snap, @bcp, @hbcp, @algo, @smooth, @agg, @meshEl);
                     SELECT last_insert_rowid();
                  """;
               }
               else
               {
                  cmd.CommandText = """
                     UPDATE fire_sections SET
                        num=@num, tag=@tag, section_id=@sid, fire_duration_min=@dur, fire_curve=@curve,
                        mesh_step_m=@mesh, time_step_s=@dt, theta=@theta, picard_tol_celsius=@ptol,
                        picard_max_iter=@piter, snapshot_step_min=@snap, bc_preset=@bcp,
                        hole_bc_preset=@hbcp, algorithm=@algo, smooth_iter_tri=@smooth,
                        aggregate_type=@agg, mesh_element_type=@meshEl
                     WHERE id=@id;
                  """;
                  cmd.Parameters.AddWithValue("@id", fs.Id);
               }

               cmd.Parameters.AddWithValue("@num", fs.Num);
               cmd.Parameters.AddWithValue("@tag", fs.Tag);
               cmd.Parameters.AddWithValue("@sid", fs.SectionId);
               cmd.Parameters.AddWithValue("@dur", fs.FireDurationMin);
               cmd.Parameters.AddWithValue("@curve", fs.FireCurve);
               cmd.Parameters.AddWithValue("@mesh", fs.MeshStepM);
               cmd.Parameters.AddWithValue("@dt", fs.TimeStepS);
               cmd.Parameters.AddWithValue("@theta", fs.Theta);
               cmd.Parameters.AddWithValue("@ptol", fs.PicardTolCelsius);
               cmd.Parameters.AddWithValue("@piter", fs.PicardMaxIter);
               cmd.Parameters.AddWithValue("@snap", fs.SnapshotStepMin);
               cmd.Parameters.AddWithValue("@bcp", fs.BcPreset);
               cmd.Parameters.AddWithValue("@hbcp", fs.HoleBcPreset);
               cmd.Parameters.AddWithValue("@algo", fs.Algorithm);
               cmd.Parameters.AddWithValue("@smooth", fs.SmoothIterTri);
               cmd.Parameters.AddWithValue("@agg", fs.AggregateType ?? "");
               cmd.Parameters.AddWithValue("@meshEl", string.IsNullOrWhiteSpace(fs.MeshElementType) ? "linear" : fs.MeshElementType);

               if (isNew) fs.Id = (int)(long)cmd.ExecuteScalar()!;
               else cmd.ExecuteNonQuery();
            }

            using (var del = _connection.CreateCommand())
            {
               del.CommandText = "DELETE FROM fire_section_edges WHERE fire_section_id=@sid";
               del.Parameters.AddWithValue("@sid", fs.Id);
               del.ExecuteNonQuery();
            }

            foreach (var edge in fs.Edges)
            {
               using var ins = _connection.CreateCommand();
               ins.CommandText = """
                  INSERT INTO fire_section_edges
                     (fire_section_id, edge_index, contour_type, hole_index, bc_type, alpha_conv, emissivity, t_ambient)
                  VALUES
                     (@sid, @edge, @ctype, @hid, @bc, @alpha, @eps, @ta);
               """;
               ins.Parameters.AddWithValue("@sid", fs.Id);
               ins.Parameters.AddWithValue("@edge", edge.EdgeIndex);
               ins.Parameters.AddWithValue("@ctype", edge.ContourType);
               ins.Parameters.AddWithValue("@hid", (object?)edge.HoleIndex ?? DBNull.Value);
               ins.Parameters.AddWithValue("@bc", edge.BcType);
               ins.Parameters.AddWithValue("@alpha", edge.AlphaConv);
               ins.Parameters.AddWithValue("@eps", edge.Emissivity);
               ins.Parameters.AddWithValue("@ta", edge.TAmbientCelsius);
               ins.ExecuteNonQuery();
            }

            tx.Commit();
            if (!FireSections.Contains(fs)) FireSections.Add(fs);
         }
         catch
         {
            tx.Rollback();
            throw;
         }
      }

      /// <summary>
      /// Удаляет огневое сечение по идентификатору.
      /// </summary>
      public void DeleteFireSection(int id)
      {
         if (id == 0) return;
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "DELETE FROM fire_sections WHERE id=@id";
         cmd.Parameters.AddWithValue("@id", id);
         cmd.ExecuteNonQuery();
         var existing = FireSections.FirstOrDefault(x => x.Id == id);
         if (existing != null) FireSections.Remove(existing);
      }

      /// <summary>
      /// Сохраняет результат огневого теплового расчёта в таблицу BLOB и возвращает id записи.
      /// </summary>
      public int SaveFireThermalResult(int fireSectionId, FireThermalResult result)
      {
         byte[] blob = FireThermalBlobCodec.Pack(result);
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            INSERT INTO fire_thermal_results (fire_section_id, created, blob)
            VALUES (@sid, @created, @blob);
            SELECT last_insert_rowid();
         """;
         cmd.Parameters.AddWithValue("@sid", fireSectionId);
         cmd.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("O"));
         cmd.Parameters.AddWithValue("@blob", blob);
         return (int)(long)cmd.ExecuteScalar()!;
      }

      /// <summary>
      /// Загружает результат огневого теплового расчёта из таблицы BLOB по идентификатору записи.
      /// </summary>
      public FireThermalResult LoadFireThermalResult(int id)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT blob FROM fire_thermal_results WHERE id=@id";
         cmd.Parameters.AddWithValue("@id", id);
         var payload = cmd.ExecuteScalar() as byte[];
         if (payload == null)
            throw new InvalidOperationException($"Результат fire_thermal_results с id={id} не найден.");
         return FireThermalBlobCodec.Unpack(payload);
      }

      /// <summary>Последний сохранённый тепловой результат для огневого сечения.</summary>
      public FireThermalResult? LoadLatestFireThermalResult(int fireSectionId)
      {
         int? id = GetLatestFireThermalResultId(fireSectionId);
         return id.HasValue ? LoadFireThermalResult(id.Value) : null;
      }

      /// <summary>Идентификатор последнего теплового результата для огневого сечения.</summary>
      public int? GetLatestFireThermalResultId(int fireSectionId)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT id FROM fire_thermal_results
            WHERE fire_section_id=@sid
            ORDER BY id DESC
            LIMIT 1
         """;
         cmd.Parameters.AddWithValue("@sid", fireSectionId);
         var scalar = cmd.ExecuteScalar();
         if (scalar is null or DBNull)
            return null;
         return Convert.ToInt32(scalar);
      }

      #endregion

      #region CalcTask / CalcResult

      void LoadCalcTasks()
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT id, num, tag, kind, section_id, force_set_id, force_item_id, calc_type, params_json FROM calc_tasks ORDER BY num";
         using var reader = cmd.ExecuteReader();
         while (reader.Read())
         {
            var ct = new CalcTask
            {
               Id          = reader.GetInt32(0),
               Num         = reader.GetInt32(1),
               Tag         = reader.GetString(2),
               Kind        = reader.GetString(3),
               SectionId   = reader.GetInt32(4),
               ForceSetId  = reader.GetInt32(5),
               ForceItemId = reader.GetInt32(6),
               CalcType    = Enum.TryParse<CalcType>(reader.GetString(7), out var ct2) ? ct2 : CalcType.C,
               ParamsJson  = reader.IsDBNull(8) ? "{}" : reader.GetString(8)
            };
            CalcTasks.Add(ct);
         }
      }

      void LoadCalcResults()
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT id, task_id, task_kind, task_tag, created, status, data_json FROM calc_results ORDER BY id";
         using var reader = cmd.ExecuteReader();
         while (reader.Read())
            CalcResults.Add(ReadCalcResult(reader));
      }

      static CalcResult ReadCalcResult(Microsoft.Data.Sqlite.SqliteDataReader reader) => new()
      {
         Id       = reader.GetInt32(0),
         TaskId   = reader.GetInt32(1),
         TaskKind = reader.GetString(2),
         TaskTag  = reader.GetString(3),
         Created  = reader.GetString(4),
         Status   = reader.GetString(5),
         DataJson = reader.GetString(6)
      };

      /// <summary>Загружает сохранённый расчёт по идентификатору.</summary>
      public CalcResult? GetCalcResultById(int resultId)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT id, task_id, task_kind, task_tag, created, status, data_json FROM calc_results WHERE id=@id";
         cmd.Parameters.AddWithValue("@id", resultId);
         using var reader = cmd.ExecuteReader();
         return reader.Read() ? ReadCalcResult(reader) : null;
      }

      public void SaveCalcTask(CalcTask ct)
      {
         bool isNew = ct.Id == 0;
         var cmd = _connection.CreateCommand();
         if (isNew)
         {
            cmd.CommandText = @"
               INSERT INTO calc_tasks (num, tag, kind, section_id, force_set_id, force_item_id, calc_type, params_json)
               VALUES (@num, @tag, @kind, @sid, @fsid, @fiid, @ct, @params);
               SELECT last_insert_rowid();";
         }
         else
         {
            cmd.CommandText = @"
               UPDATE calc_tasks SET num=@num, tag=@tag, kind=@kind,
                  section_id=@sid, force_set_id=@fsid, force_item_id=@fiid, calc_type=@ct, params_json=@params
               WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", ct.Id);
         }
         cmd.Parameters.AddWithValue("@num",  ct.Num);
         cmd.Parameters.AddWithValue("@tag",  ct.Tag);
         cmd.Parameters.AddWithValue("@kind", ct.Kind);
         cmd.Parameters.AddWithValue("@sid",  ct.SectionId);
         cmd.Parameters.AddWithValue("@fsid", ct.ForceSetId);
         cmd.Parameters.AddWithValue("@fiid", ct.ForceItemId);
         cmd.Parameters.AddWithValue("@ct",   ct.CalcType.ToString());
         cmd.Parameters.AddWithValue("@params", ct.ParamsJson ?? "{}");
         if (isNew)
         {
            ct.Id = (int)(long)cmd.ExecuteScalar()!;
            if (!CalcTasks.Contains(ct)) CalcTasks.Add(ct);
         }
         else
            cmd.ExecuteNonQuery();
      }

      public void DeleteCalcTask(CalcTask ct)
      {
         if (ct.Id == 0) { CalcTasks.Remove(ct); return; }
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "DELETE FROM calc_tasks WHERE id=@id";
         cmd.Parameters.AddWithValue("@id", ct.Id);
         cmd.ExecuteNonQuery();
         CalcTasks.Remove(ct);
         // Удаляем связанные результаты из коллекции (каскад в БД уже сработал)
         var toRemove = CalcResults.Where(r => r.TaskId == ct.Id).ToList();
         foreach (var r in toRemove) CalcResults.Remove(r);
      }

      public void SaveCalcResult(CalcResult cr)
      {
         bool isNew = cr.Id == 0;
         var cmd = _connection.CreateCommand();
         if (isNew)
         {
            cmd.CommandText = @"
               INSERT INTO calc_results (task_id, task_kind, task_tag, created, status, data_json)
               VALUES (@tid, @kind, @ttag, @created, @status, @data);
               SELECT last_insert_rowid();";
         }
         else
         {
            cmd.CommandText = @"
               UPDATE calc_results SET status=@status, data_json=@data WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", cr.Id);
         }
         cmd.Parameters.AddWithValue("@tid",     cr.TaskId);
         cmd.Parameters.AddWithValue("@kind",    cr.TaskKind);
         cmd.Parameters.AddWithValue("@ttag",    cr.TaskTag);
         cmd.Parameters.AddWithValue("@created", cr.Created);
         cmd.Parameters.AddWithValue("@status",  cr.Status);
         cmd.Parameters.AddWithValue("@data",    cr.DataJson);
         if (isNew)
         {
            cr.Id = (int)(long)cmd.ExecuteScalar()!;
            if (!CalcResults.Contains(cr)) CalcResults.Add(cr);
         }
         else
            cmd.ExecuteNonQuery();
      }

      public void DeleteCalcResult(CalcResult cr)
      {
         if (cr.Id == 0) { CalcResults.Remove(cr); return; }
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "DELETE FROM calc_results WHERE id=@id";
         cmd.Parameters.AddWithValue("@id", cr.Id);
         cmd.ExecuteNonQuery();
         CalcResults.Remove(cr);
      }

      public void DeleteCalcResultsByTaskId(int taskId)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "DELETE FROM calc_results WHERE task_id=@tid";
         cmd.Parameters.AddWithValue("@tid", taskId);
         cmd.ExecuteNonQuery();
         var toRemove = CalcResults.Where(r => r.TaskId == taskId).ToList();
         foreach (var r in toRemove) CalcResults.Remove(r);
      }

      #endregion

      #region Convenience methods

      public void AddMaterial(Material m) { Materials.Add(m); MaterialChars.Clear(); SaveMaterial(m); foreach (var c in m.MaterialChars) MaterialChars.Add(c); }
      public void AddContour(Contour c) { Contours.Add(c); foreach (var p in c.Points) Points.Add(p); SaveContour(c); }
      public void AddCircle(CircleP c) { Circles.Add(c); SaveCircle(c); }
      public void AddRange(IEnumerable<CircleP> circles) { foreach (var c in circles) AddCircle(c); }
      public void AddRange(IEnumerable<Contour> contours) { foreach (var c in contours) AddContour(c); }

      #endregion

      #region DTO classes for JSON serialization

      class SplineBranchJson
      {
         public string SplineType { get; set; } = "LSpline";
         public double[]? X { get; set; }
         public double[]? Y { get; set; }
         public double[]? DY { get; set; }
      }

      class SplineDataJson
      {
         public SplineBranchJson? Compression { get; set; }
         public SplineBranchJson? Tension { get; set; }
         public List<double>? CharacteristicStrains { get; set; }
      }

      #endregion

      #region Settings

      public CsvExportSettings LoadCsvSettings()
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT value_json FROM settings WHERE key='csv'";
         var json = cmd.ExecuteScalar() as string;
         if (json == null)
         {
            var def = CsvExportSettings.Default;
            SaveCsvSettings(def);
            return def;
         }
         return JsonSerializer.Deserialize<CsvExportSettings>(json) ?? CsvExportSettings.Default;
      }

      public void SaveCsvSettings(CsvExportSettings s)
      {
          var json = JsonSerializer.Serialize(s);
          var cmd = _connection.CreateCommand();
         cmd.CommandText = @"INSERT OR REPLACE INTO settings (key, value_json)
                             VALUES ('csv', $json)";
         cmd.Parameters.AddWithValue("$json", json);
         cmd.ExecuteNonQuery();
      }

      public LiraImportSettings LoadLiraImportSettings()
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT value_json FROM settings WHERE key='lira_import'";
         var json = cmd.ExecuteScalar() as string;
         if (json == null)
         {
            var def = LiraImportSettings.Default;
            SaveLiraImportSettings(def);
            return def;
         }
         return JsonSerializer.Deserialize<LiraImportSettings>(json) ?? LiraImportSettings.Default;
      }

      public void SaveLiraImportSettings(LiraImportSettings s)
      {
         var json = JsonSerializer.Serialize(s);
         var cmd = _connection.CreateCommand();
         cmd.CommandText = @"INSERT OR REPLACE INTO settings (key, value_json)
                             VALUES ('lira_import', $json)";
         cmd.Parameters.AddWithValue("$json", json);
         cmd.ExecuteNonQuery();
      }

      public AcadImportSettings LoadAcadImportSettings()
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT value_json FROM settings WHERE key='acad_import'";
         var json = cmd.ExecuteScalar() as string;
         if (json == null)
         {
            var def = AcadImportSettings.Default;
            SaveAcadImportSettings(def);
            return def;
         }
         return JsonSerializer.Deserialize<AcadImportSettings>(json) ?? AcadImportSettings.Default;
      }

      public void SaveAcadImportSettings(AcadImportSettings s)
      {
         var json = JsonSerializer.Serialize(s);
         var cmd = _connection.CreateCommand();
         cmd.CommandText = @"INSERT OR REPLACE INTO settings (key, value_json)
                              VALUES ('acad_import', $json)";
         cmd.Parameters.AddWithValue("$json", json);
         cmd.ExecuteNonQuery();
      }

      public PlotSettings LoadPlotSettings()
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT value_json FROM settings WHERE key='plot'";
         var json = cmd.ExecuteScalar() as string;
         if (json == null)
         {
            var def = PlotSettings.Default;
            SavePlotSettings(def);
            return def;
         }
         var plot = JsonSerializer.Deserialize<PlotSettings>(json) ?? PlotSettings.Default;
         MigrateForceSetColorizeFromCalc(plot, json);
         return plot;
      }

      /// <summary>Перенос forceSetColorize из calc в plot (ранее хранилось в расчётных настройках).</summary>
      void MigrateForceSetColorizeFromCalc(PlotSettings plot, string plotJson)
      {
         if (plotJson.Contains("\"forceSetColorize\"", StringComparison.Ordinal))
            return;

         var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT value_json FROM settings WHERE key='calc'";
         var calcJson = cmd.ExecuteScalar() as string;
         if (calcJson == null)
            return;

         using var doc = JsonDocument.Parse(calcJson);
         if (doc.RootElement.TryGetProperty("forceSetColorize", out var prop)
             && prop.ValueKind == JsonValueKind.True)
         {
            plot.ForceSetColorize = true;
            SavePlotSettings(plot);
         }
      }

      public void SavePlotSettings(PlotSettings s)
      {
          var json = JsonSerializer.Serialize(s);
          var cmd = _connection.CreateCommand();
         cmd.CommandText = @"INSERT OR REPLACE INTO settings (key, value_json)
                             VALUES ('plot', $json)";
         cmd.Parameters.AddWithValue("$json", json);
         cmd.ExecuteNonQuery();
      }

      public CalcSettings LoadCalcSettings()
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT value_json FROM settings WHERE key='calc'";
         var json = cmd.ExecuteScalar() as string;
         if (json == null)
         {
            var def = CalcSettings.Default;
            SaveCalcSettings(def);
            return def;
         }
         return JsonSerializer.Deserialize<CalcSettings>(json) ?? CalcSettings.Default;
      }

      public void SaveCalcSettings(CalcSettings s)
      {
         var json = JsonSerializer.Serialize(s);
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "INSERT OR REPLACE INTO settings (key, value_json) VALUES ('calc', $json)";
         cmd.Parameters.AddWithValue("$json", json);
         cmd.ExecuteNonQuery();
      }

      #endregion

      #region FEM

      void LoadFemSchemas()
      {
         FemSchemas.Clear();
         var schemas = new Dictionary<int, CScore.Fem.FemSchema>();
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = "SELECT id, tag, source_type, created FROM fem_schemas ORDER BY id";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
               var s = new CScore.Fem.FemSchema
               {
                  Id         = r.GetInt32(0),
                  Tag        = r.GetString(1),
                  SourceType = r.GetString(2),
                  Created    = r.GetString(3)
               };
               schemas[s.Id] = s;
            }
         }
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = """
               SELECT id, schema_id, tag, member_type, member_tags_json,
                      plate_section_id, force_set_id, design_params_json
               FROM fem_member_groups ORDER BY schema_id, id
            """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
               int sid = r.GetInt32(1);
               if (!schemas.TryGetValue(sid, out var schema)) continue;
               schema.MemberGroups.Add(new CScore.Fem.FemMemberGroup
               {
                  Id               = r.GetInt32(0),
                  SchemaId         = sid,
                  Tag              = r.GetString(2),
                  MemberType       = r.IsDBNull(3) ? null : r.GetString(3),
                  MemberTagsJson   = r.GetString(4),
                  PlateSectionId   = r.IsDBNull(5) ? null : r.GetInt32(5),
                  ForceSetId       = r.IsDBNull(6) ? null : r.GetInt32(6),
                  DesignParamsJson = r.IsDBNull(7) ? null : r.GetString(7),
               });
            }
         }
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = """
               SELECT id, schema_id, tag, load_type, sp20_type, sp20_group,
                      gamma_f_unfav, gamma_f_fav, psi1, psi2
               FROM fem_load_cases ORDER BY schema_id, id
            """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
               int sid = r.GetInt32(1);
               if (!schemas.TryGetValue(sid, out var schema)) continue;
               schema.LoadCases.Add(new CScore.Fem.FemLoadCase
               {
                  Id          = r.GetInt32(0),
                  SchemaId    = sid,
                  Tag         = r.GetString(2),
                  LoadType    = r.IsDBNull(3) ? null : r.GetString(3),
                  Sp20Type    = r.IsDBNull(4) ? "short_term" : r.GetString(4),
                  Sp20Group   = r.IsDBNull(5) ? null : r.GetString(5),
                  GammaFUnfav = r.IsDBNull(6) ? null : r.GetDouble(6),
                  GammaFFav   = r.IsDBNull(7) ? null : r.GetDouble(7),
                  Psi1        = r.IsDBNull(8) ? null : r.GetDouble(8),
                  Psi2        = r.IsDBNull(9) ? null : r.GetDouble(9)
               });
            }
         }
          using (var cmd = _connection.CreateCommand())
          {
             cmd.CommandText = """
                SELECT id, schema_id, tag, description, expression_json, source_kind, combination_type
                FROM fem_load_definitions ORDER BY schema_id, id
             """;
             using var r = cmd.ExecuteReader();
             while (r.Read())
             {
                int sid = r.GetInt32(1);
                if (!schemas.TryGetValue(sid, out var schema)) continue;
                schema.LoadDefinitions.Add(new CScore.Fem.FemLoadDefinition
                {
                   Id = r.GetInt32(0), SchemaId = sid, Tag = r.GetString(2),
                   Description = r.IsDBNull(3) ? null : r.GetString(3),
                   ExpressionJson = r.GetString(4), SourceKind = r.GetString(5),
                   CombinationType = r.IsDBNull(6) ? null : r.GetString(6)
                });
             }
          }
          using (var cmd = _connection.CreateCommand())
          {
             cmd.CommandText = """
                SELECT id, schema_id, tag, kind, load_expression_json, params_json,
                      status, result_id, created
               FROM fem_analyses ORDER BY schema_id, id
            """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
               int sid = r.GetInt32(1);
               if (!schemas.TryGetValue(sid, out var schema)) continue;
               schema.Analyses.Add(new CScore.Fem.FemAnalysis
               {
                  Id                  = r.GetInt32(0),
                  SchemaId            = sid,
                  Tag                 = r.GetString(2),
                  Kind                = r.GetString(3),
                  LoadExpressionJson  = r.GetString(4),
                  ParamsJson          = r.GetString(5),
                  Status              = r.GetString(6),
                  ResultId            = r.IsDBNull(7) ? null : r.GetInt32(7),
                  Created             = r.GetString(8)
               });
            }
         }
         foreach (var s in schemas.Values) FemSchemas.Add(s);
      }

      void LoadFemChecks()
      {
         // clear member Checks collections first
         foreach (var s in FemSchemas)
            foreach (var m in s.MemberGroups)
               m.Checks.Clear();

         FemChecks.Clear();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT id, schema_id, member_id, norm_code, params_json, result_id, tag, force_set_ids_json, calc_type_override, element_id FROM fem_checks ORDER BY id";
         using var r = cmd.ExecuteReader();
         while (r.Read())
         {
            var check = new CScore.Fem.FemCheck
            {
               Id               = r.GetInt32(0),
               SchemaId         = r.GetInt32(1),
               MemberId         = r.GetInt32(2),
               NormCode         = r.GetString(3),
               ParamsJson       = r.IsDBNull(4) ? null : r.GetString(4),
               ResultId         = r.IsDBNull(5) ? null : r.GetInt32(5),
               Tag              = r.IsDBNull(6) ? "" : r.GetString(6),
               ForceSetIdsJson  = r.IsDBNull(7) ? "[]" : r.GetString(7),
               CalcTypeOverride = r.IsDBNull(8) ? null : r.GetString(8),
               ElementId        = r.IsDBNull(9) ? null : r.GetInt32(9),
            };
            FemChecks.Add(check);
            var schema = FemSchemas.FirstOrDefault(s => s.Id == check.SchemaId);
            if (!check.TargetsElement)
               schema?.MemberGroups.FirstOrDefault(m => m.Id == check.MemberId)?.Checks.Add(check);
         }
      }

      public void SaveFemSchema(CScore.Fem.FemSchema schema)
      {
         using var tx = _connection.BeginTransaction();
         try
         {
            using var cmd = _connection.CreateCommand();
            if (schema.Id == 0)
            {
               cmd.CommandText = """
                  INSERT INTO fem_schemas (tag, source_type, created)
                  VALUES (@tag, @src, @created);
                  SELECT last_insert_rowid();
               """;
               cmd.Parameters.AddWithValue("@tag",     schema.Tag);
               cmd.Parameters.AddWithValue("@src",     schema.SourceType);
               cmd.Parameters.AddWithValue("@created", schema.Created);
               schema.Id = (int)(long)cmd.ExecuteScalar()!;
               FemSchemas.Add(schema);
            }
            else
            {
               cmd.CommandText = "UPDATE fem_schemas SET tag=@tag, source_type=@src WHERE id=@id";
               cmd.Parameters.AddWithValue("@tag", schema.Tag);
               cmd.Parameters.AddWithValue("@src", schema.SourceType);
               cmd.Parameters.AddWithValue("@id",  schema.Id);
               cmd.ExecuteNonQuery();
            }
            foreach (var g in schema.MemberGroups)
               SaveFemMemberGroupCore(g, schema.Id);
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }

      /// <summary>
      /// Удаляет схему МКЭ и все дочерние строки по schema_id вручную — ON DELETE CASCADE в
      /// объявлениях FK не выполняется, т.к. на соединении PRAGMA foreign_keys=OFF (см.
      /// SetDeleteJournalMode). Без явного удаления узлы/элементы импортированной сетки
      /// (обычно самая объёмная часть) оставались бы «осиротевшими» строками в БД — файл
      /// не уменьшался бы даже после VACUUM.
      /// </summary>
      public void DeleteFemSchema(CScore.Fem.FemSchema schema)
      {
         if (schema.Id == 0) return;
         using var tx = _connection.BeginTransaction();
         try
         {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
               DELETE FROM fem_checks            WHERE schema_id=@id;
               DELETE FROM fem_analyses           WHERE schema_id=@id;
               DELETE FROM fem_load_definitions   WHERE schema_id=@id;
               DELETE FROM fem_node_loads         WHERE schema_id=@id;
               DELETE FROM fem_load_cases         WHERE schema_id=@id;
               DELETE FROM fem_member_groups      WHERE schema_id=@id;
               DELETE FROM fem_elements           WHERE schema_id=@id;
               DELETE FROM fem_mesh_nodes         WHERE schema_id=@id;
               DELETE FROM fem_members            WHERE schema_id=@id;
               DELETE FROM fem_nodes              WHERE schema_id=@id;
               DELETE FROM fem_schemas            WHERE id=@id;
            """;
            cmd.Parameters.AddWithValue("@id", schema.Id);
            cmd.ExecuteNonQuery();
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
         FemSchemas.Remove(schema);
      }

      public List<CScore.Fem.FemLoadCase> GetFemLoadCases(int schemaId)
      {
         var result = new List<CScore.Fem.FemLoadCase>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT id, tag, load_type, sp20_type, sp20_group,
                   gamma_f_unfav, gamma_f_fav, psi1, psi2
            FROM fem_load_cases WHERE schema_id=@sid ORDER BY id
         """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var r = cmd.ExecuteReader();
         while (r.Read())
            result.Add(new CScore.Fem.FemLoadCase
            {
               Id          = r.GetInt32(0),
               SchemaId    = schemaId,
               Tag         = r.GetString(1),
               LoadType    = r.IsDBNull(2) ? null : r.GetString(2),
               Sp20Type    = r.IsDBNull(3) ? "short_term" : r.GetString(3),
               Sp20Group   = r.IsDBNull(4) ? null : r.GetString(4),
               GammaFUnfav = r.IsDBNull(5) ? null : r.GetDouble(5),
               GammaFFav   = r.IsDBNull(6) ? null : r.GetDouble(6),
               Psi1        = r.IsDBNull(7) ? null : r.GetDouble(7),
               Psi2        = r.IsDBNull(8) ? null : r.GetDouble(8)
            });
         return result;
      }

      public void SaveFemLoadCase(CScore.Fem.FemLoadCase loadCase)
      {
         using var tx = _connection.BeginTransaction();
         try
         {
            using var cmd = _connection.CreateCommand();
            if (loadCase.Id == 0)
            {
               cmd.CommandText = """
                  INSERT INTO fem_load_cases
                     (schema_id, tag, load_type, sp20_type, sp20_group,
                      gamma_f_unfav, gamma_f_fav, psi1, psi2)
                  VALUES (@sid, @tag, @lt, @st, @sg, @gu, @gf, @p1, @p2);
                  SELECT last_insert_rowid();
               """;
               AddFemLoadCaseParameters(cmd, loadCase);
               loadCase.Id = (int)(long)cmd.ExecuteScalar()!;
            }
            else
            {
               cmd.CommandText = """
                  UPDATE fem_load_cases SET tag=@tag, load_type=@lt, sp20_type=@st,
                     sp20_group=@sg, gamma_f_unfav=@gu, gamma_f_fav=@gf,
                     psi1=@p1, psi2=@p2 WHERE id=@id AND schema_id=@sid
               """;
               AddFemLoadCaseParameters(cmd, loadCase);
               cmd.Parameters.AddWithValue("@id", loadCase.Id);
               cmd.ExecuteNonQuery();
            }

            var schema = FemSchemas.FirstOrDefault(s => s.Id == loadCase.SchemaId);
            if (schema != null && !schema.LoadCases.Contains(loadCase))
               schema.LoadCases.Add(loadCase);
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }

      public void DeleteFemLoadCase(CScore.Fem.FemLoadCase loadCase)
      {
         if (loadCase.Id == 0) return;
         using var tx = _connection.BeginTransaction();
         try
         {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM fem_node_loads WHERE load_case_id=@id; DELETE FROM fem_load_cases WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", loadCase.Id);
            cmd.ExecuteNonQuery();
            tx.Commit();
            FemSchemas.FirstOrDefault(s => s.Id == loadCase.SchemaId)?.LoadCases.Remove(loadCase);
         }
         catch { tx.Rollback(); throw; }
      }

      static void AddFemLoadCaseParameters(SqliteCommand cmd, CScore.Fem.FemLoadCase loadCase)
      {
         cmd.Parameters.AddWithValue("@sid", loadCase.SchemaId);
         cmd.Parameters.AddWithValue("@tag", loadCase.Tag);
         cmd.Parameters.AddWithValue("@lt", (object?)loadCase.LoadType ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@st", loadCase.Sp20Type);
         cmd.Parameters.AddWithValue("@sg", (object?)loadCase.Sp20Group ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@gu", (object?)loadCase.GammaFUnfav ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@gf", (object?)loadCase.GammaFFav ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@p1", (object?)loadCase.Psi1 ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@p2", (object?)loadCase.Psi2 ?? DBNull.Value);
      }

      public List<CScore.Fem.FemNodeLoad> GetFemNodeLoads(int schemaId, int? loadCaseId = null)
      {
         var result = new List<CScore.Fem.FemNodeLoad>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT id, load_case_id, node_id, fx, fy, fz, mx, my, mz
            FROM fem_node_loads
            WHERE schema_id=@sid AND (@lc IS NULL OR load_case_id=@lc)
            ORDER BY id
         """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         cmd.Parameters.AddWithValue("@lc", (object?)loadCaseId ?? DBNull.Value);
         using var r = cmd.ExecuteReader();
         while (r.Read())
            result.Add(new CScore.Fem.FemNodeLoad
            {
               Id         = r.GetInt32(0), SchemaId = schemaId,
               LoadCaseId = r.GetInt32(1), NodeId = r.GetInt32(2),
               Fx = r.GetDouble(3), Fy = r.GetDouble(4), Fz = r.GetDouble(5),
               Mx = r.GetDouble(6), My = r.GetDouble(7), Mz = r.GetDouble(8)
            });
         return result;
      }

      public List<CScore.Fem.FemLoadDefinition> GetFemLoadDefinitions(int schemaId)
      {
         var result = new List<CScore.Fem.FemLoadDefinition>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT id, tag, description, expression_json, source_kind, combination_type
            FROM fem_load_definitions WHERE schema_id=@sid ORDER BY id
         """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var r = cmd.ExecuteReader();
         while (r.Read())
            result.Add(new CScore.Fem.FemLoadDefinition
            {
               Id = r.GetInt32(0), SchemaId = schemaId, Tag = r.GetString(1),
               Description = r.IsDBNull(2) ? null : r.GetString(2), ExpressionJson = r.GetString(3),
               SourceKind = r.GetString(4), CombinationType = r.IsDBNull(5) ? null : r.GetString(5)
            });
         return result;
      }

      public void SaveFemLoadDefinition(CScore.Fem.FemLoadDefinition definition)
      {
         using var cmd = _connection.CreateCommand();
         if (definition.Id <= 0)
         {
            cmd.CommandText = """
               INSERT INTO fem_load_definitions (schema_id, tag, description, expression_json, source_kind, combination_type)
               VALUES (@sid, @tag, @desc, @expr, @source, @type); SELECT last_insert_rowid();
            """;
            AddFemLoadDefinitionParameters(cmd, definition);
            definition.Id = (int)(long)cmd.ExecuteScalar()!;
         }
         else
         {
            cmd.CommandText = """
               UPDATE fem_load_definitions SET tag=@tag, description=@desc, expression_json=@expr,
                   source_kind=@source, combination_type=@type WHERE id=@id AND schema_id=@sid
            """;
            AddFemLoadDefinitionParameters(cmd, definition);
            cmd.Parameters.AddWithValue("@id", definition.Id);
            cmd.ExecuteNonQuery();
         }
      }

      public void DeleteFemLoadDefinition(CScore.Fem.FemLoadDefinition definition)
      {
         if (definition.Id != 0)
         {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM fem_load_definitions WHERE id=@id AND schema_id=@sid";
            cmd.Parameters.AddWithValue("@id", definition.Id);
            cmd.Parameters.AddWithValue("@sid", definition.SchemaId);
            cmd.ExecuteNonQuery();
         }
         FemSchemas.FirstOrDefault(schema => schema.Id == definition.SchemaId)?.LoadDefinitions.Remove(definition);
      }

      static void AddFemLoadDefinitionParameters(SqliteCommand cmd, CScore.Fem.FemLoadDefinition definition)
      {
         cmd.Parameters.AddWithValue("@sid", definition.SchemaId);
         cmd.Parameters.AddWithValue("@tag", definition.Tag);
         cmd.Parameters.AddWithValue("@desc", (object?)definition.Description ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@expr", definition.ExpressionJson);
         cmd.Parameters.AddWithValue("@source", definition.SourceKind);
         cmd.Parameters.AddWithValue("@type", (object?)definition.CombinationType ?? DBNull.Value);
      }

      public void SaveFemNodeLoad(CScore.Fem.FemNodeLoad load)
      {
         ValidateFemNodeLoadReferences(load);
         using var tx = _connection.BeginTransaction();
         try
         {
            using var cmd = _connection.CreateCommand();
            if (load.Id == 0)
            {
               cmd.CommandText = """
                  INSERT INTO fem_node_loads
                     (schema_id, load_case_id, node_id, fx, fy, fz, mx, my, mz)
                  VALUES (@sid, @lc, @nid, @fx, @fy, @fz, @mx, @my, @mz);
                  SELECT last_insert_rowid();
               """;
               AddFemNodeLoadParameters(cmd, load);
               load.Id = (int)(long)cmd.ExecuteScalar()!;
            }
            else
            {
               cmd.CommandText = """
                  UPDATE fem_node_loads SET load_case_id=@lc, node_id=@nid,
                     fx=@fx, fy=@fy, fz=@fz, mx=@mx, my=@my, mz=@mz
                  WHERE id=@id AND schema_id=@sid
               """;
               AddFemNodeLoadParameters(cmd, load);
               cmd.Parameters.AddWithValue("@id", load.Id);
               cmd.ExecuteNonQuery();
            }
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }

      public void DeleteFemNodeLoad(CScore.Fem.FemNodeLoad load)
      {
         if (load.Id == 0) return;
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "DELETE FROM fem_node_loads WHERE id=@id";
         cmd.Parameters.AddWithValue("@id", load.Id);
         cmd.ExecuteNonQuery();
      }

      static void AddFemNodeLoadParameters(SqliteCommand cmd, CScore.Fem.FemNodeLoad load)
      {
         cmd.Parameters.AddWithValue("@sid", load.SchemaId);
         cmd.Parameters.AddWithValue("@lc", load.LoadCaseId);
         cmd.Parameters.AddWithValue("@nid", load.NodeId);
         cmd.Parameters.AddWithValue("@fx", load.Fx);
         cmd.Parameters.AddWithValue("@fy", load.Fy);
         cmd.Parameters.AddWithValue("@fz", load.Fz);
         cmd.Parameters.AddWithValue("@mx", load.Mx);
         cmd.Parameters.AddWithValue("@my", load.My);
         cmd.Parameters.AddWithValue("@mz", load.Mz);
      }

      public List<CScore.Fem.FemAnalysis> GetFemAnalyses(int schemaId)
      {
         var result = new List<CScore.Fem.FemAnalysis>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT id, tag, kind, load_expression_json, params_json, status, result_id, created
            FROM fem_analyses WHERE schema_id=@sid ORDER BY id
         """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var r = cmd.ExecuteReader();
         while (r.Read())
            result.Add(ReadFemAnalysis(r, schemaId));
         return result;
      }

      public CScore.Fem.FemAnalysis? GetFemAnalysis(int id)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT id, schema_id, tag, kind, load_expression_json, params_json, status, result_id, created
            FROM fem_analyses WHERE id=@id
         """;
         cmd.Parameters.AddWithValue("@id", id);
         using var r = cmd.ExecuteReader();
         return r.Read() ? ReadFemAnalysis(r, r.GetInt32(1), hasSchemaColumn: true) : null;
      }

      static CScore.Fem.FemAnalysis ReadFemAnalysis(SqliteDataReader r, int schemaId, bool hasSchemaColumn = false)
      {
         int offset = hasSchemaColumn ? 1 : 0;
         return new CScore.Fem.FemAnalysis
         {
            Id                 = r.GetInt32(0),
            SchemaId           = schemaId,
            Tag                = r.GetString(1 + offset),
            Kind               = r.GetString(2 + offset),
            LoadExpressionJson = r.GetString(3 + offset),
            ParamsJson         = r.GetString(4 + offset),
            Status             = r.GetString(5 + offset),
            ResultId           = r.IsDBNull(6 + offset) ? null : r.GetInt32(6 + offset),
            Created            = r.GetString(7 + offset)
         };
      }

      public void SaveFemAnalysis(CScore.Fem.FemAnalysis analysis)
      {
         using var tx = _connection.BeginTransaction();
         try
         {
            using var cmd = _connection.CreateCommand();
            if (analysis.Id == 0)
            {
               cmd.CommandText = """
                  INSERT INTO fem_analyses
                     (schema_id, tag, kind, load_expression_json, params_json, status, result_id, created)
                  VALUES (@sid, @tag, @kind, @expr, @params, @status, @rid, @created);
                  SELECT last_insert_rowid();
               """;
               AddFemAnalysisParameters(cmd, analysis);
               analysis.Id = (int)(long)cmd.ExecuteScalar()!;
            }
            else
            {
               cmd.CommandText = """
                  UPDATE fem_analyses SET tag=@tag, kind=@kind, load_expression_json=@expr,
                     params_json=@params, status=@status, result_id=@rid WHERE id=@id AND schema_id=@sid
               """;
               AddFemAnalysisParameters(cmd, analysis);
               cmd.Parameters.AddWithValue("@id", analysis.Id);
               cmd.ExecuteNonQuery();
            }
            var schema = FemSchemas.FirstOrDefault(s => s.Id == analysis.SchemaId);
            if (schema != null && !schema.Analyses.Contains(analysis))
               schema.Analyses.Add(analysis);
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }

      public void DeleteFemAnalysis(CScore.Fem.FemAnalysis analysis)
      {
         if (analysis.Id == 0) return;
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "DELETE FROM fem_analyses WHERE id=@id";
         cmd.Parameters.AddWithValue("@id", analysis.Id);
         cmd.ExecuteNonQuery();
         FemSchemas.FirstOrDefault(s => s.Id == analysis.SchemaId)?.Analyses.Remove(analysis);
      }

      static void AddFemAnalysisParameters(SqliteCommand cmd, CScore.Fem.FemAnalysis analysis)
      {
         cmd.Parameters.AddWithValue("@sid", analysis.SchemaId);
         cmd.Parameters.AddWithValue("@tag", analysis.Tag);
         cmd.Parameters.AddWithValue("@kind", analysis.Kind);
         cmd.Parameters.AddWithValue("@expr", analysis.LoadExpressionJson);
         cmd.Parameters.AddWithValue("@params", analysis.ParamsJson);
         cmd.Parameters.AddWithValue("@status", analysis.Status);
         cmd.Parameters.AddWithValue("@rid", (object?)analysis.ResultId ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@created", analysis.Created);
      }

      void ValidateFemNodeLoadReferences(CScore.Fem.FemNodeLoad load)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT
              (SELECT schema_id FROM fem_nodes WHERE id=@nid),
              (SELECT schema_id FROM fem_load_cases WHERE id=@lc)
         """;
         cmd.Parameters.AddWithValue("@nid", load.NodeId);
         cmd.Parameters.AddWithValue("@lc", load.LoadCaseId);
         using var r = cmd.ExecuteReader();
         if (!r.Read() || r.IsDBNull(0) || r.IsDBNull(1))
            throw new InvalidOperationException("Узловая нагрузка ссылается на неизвестный узел или загружение.");
         if (r.GetInt32(0) != load.SchemaId || r.GetInt32(1) != load.SchemaId)
            throw new InvalidOperationException("Узел, загружение и узловая нагрузка должны принадлежать одной FEM-схеме.");
      }

      /// <summary>Массовая вставка узлов и элементов МКЭ-схемы. Существующие записи для schemaId удаляются.</summary>
      public void SaveFemTopology(
         int schemaId,
         IReadOnlyList<CScore.Fem.FemNode>        nodes,
         IReadOnlyList<CScore.Fem.FemMember>      members,
         IReadOnlyList<CScore.Fem.FemMemberGroup> memberGroups)
      {
         var oldNodesById = GetFemNodes(schemaId).ToDictionary(node => node.Id);
         var newTags = nodes.Select(node => node.NodeTag).ToList();
         if (newTags.Count != newTags.Distinct(StringComparer.Ordinal).Count())
            throw new InvalidOperationException("Теги узлов FEM-схемы должны быть уникальными.");

         var preservedLoads = new List<(string NodeTag, CScore.Fem.FemNodeLoad Load)>();
         foreach (var load in GetFemNodeLoads(schemaId))
         {
            if (!oldNodesById.TryGetValue(load.NodeId, out var oldNode))
               throw new InvalidOperationException($"Узловая нагрузка {load.Id} ссылается на отсутствующий узел {load.NodeId}.");
            if (!newTags.Contains(oldNode.NodeTag, StringComparer.Ordinal))
               throw new InvalidOperationException($"Нельзя удалить загруженный узел с тегом '{oldNode.NodeTag}'.");
            preservedLoads.Add((oldNode.NodeTag, load));
         }

         using var tx = _connection.BeginTransaction();
         try
         {
            using var delCmd = _connection.CreateCommand();
            delCmd.CommandText = """
               DELETE FROM fem_member_groups WHERE schema_id=@sid;
               DELETE FROM fem_members       WHERE schema_id=@sid;
               DELETE FROM fem_node_loads WHERE schema_id=@sid;
               DELETE FROM fem_nodes    WHERE schema_id=@sid;
            """;
            delCmd.Parameters.AddWithValue("@sid", schemaId);
            delCmd.ExecuteNonQuery();

            using var nodeCmd = _connection.CreateCommand();
            nodeCmd.CommandText = """
               INSERT INTO fem_nodes (schema_id, node_tag, x, y, z, dof_mask)
               VALUES (@sid, @tag, @x, @y, @z, @dm)
            """;
            nodeCmd.Parameters.Add("@sid", Microsoft.Data.Sqlite.SqliteType.Integer);
            nodeCmd.Parameters.Add("@tag", Microsoft.Data.Sqlite.SqliteType.Text);
            nodeCmd.Parameters.Add("@x",   Microsoft.Data.Sqlite.SqliteType.Real);
            nodeCmd.Parameters.Add("@y",   Microsoft.Data.Sqlite.SqliteType.Real);
            nodeCmd.Parameters.Add("@z",   Microsoft.Data.Sqlite.SqliteType.Real);
            nodeCmd.Parameters.Add("@dm",  Microsoft.Data.Sqlite.SqliteType.Integer);
            nodeCmd.CommandText += "; SELECT last_insert_rowid();";
            var newNodeIds = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var n in nodes)
            {
               nodeCmd.Parameters["@sid"].Value = schemaId;
               nodeCmd.Parameters["@tag"].Value = n.NodeTag;
               nodeCmd.Parameters["@x"].Value   = n.X;
               nodeCmd.Parameters["@y"].Value   = n.Y;
               nodeCmd.Parameters["@z"].Value   = n.Z;
               nodeCmd.Parameters["@dm"].Value  = n.DofMask;
               newNodeIds[n.NodeTag] = (int)(long)nodeCmd.ExecuteScalar()!;
            }

            using var loadCmd = _connection.CreateCommand();
            loadCmd.CommandText = """
               INSERT INTO fem_node_loads
                  (schema_id, load_case_id, node_id, fx, fy, fz, mx, my, mz)
               VALUES (@sid, @lc, @nid, @fx, @fy, @fz, @mx, @my, @mz)
            """;
            loadCmd.Parameters.Add("@sid", Microsoft.Data.Sqlite.SqliteType.Integer);
            loadCmd.Parameters.Add("@lc",  Microsoft.Data.Sqlite.SqliteType.Integer);
            loadCmd.Parameters.Add("@nid", Microsoft.Data.Sqlite.SqliteType.Integer);
            loadCmd.Parameters.Add("@fx",  Microsoft.Data.Sqlite.SqliteType.Real);
            loadCmd.Parameters.Add("@fy",  Microsoft.Data.Sqlite.SqliteType.Real);
            loadCmd.Parameters.Add("@fz",  Microsoft.Data.Sqlite.SqliteType.Real);
            loadCmd.Parameters.Add("@mx",  Microsoft.Data.Sqlite.SqliteType.Real);
            loadCmd.Parameters.Add("@my",  Microsoft.Data.Sqlite.SqliteType.Real);
            loadCmd.Parameters.Add("@mz",  Microsoft.Data.Sqlite.SqliteType.Real);
            foreach (var (nodeTag, load) in preservedLoads)
            {
               loadCmd.Parameters["@sid"].Value = schemaId;
               loadCmd.Parameters["@lc"].Value  = load.LoadCaseId;
               loadCmd.Parameters["@nid"].Value = newNodeIds[nodeTag];
               loadCmd.Parameters["@fx"].Value  = load.Fx;
               loadCmd.Parameters["@fy"].Value  = load.Fy;
               loadCmd.Parameters["@fz"].Value  = load.Fz;
               loadCmd.Parameters["@mx"].Value  = load.Mx;
               loadCmd.Parameters["@my"].Value  = load.My;
               loadCmd.Parameters["@mz"].Value  = load.Mz;
               loadCmd.ExecuteNonQuery();
            }

            using var elemCmd = _connection.CreateCommand();
            elemCmd.CommandText = """
               INSERT INTO fem_members (schema_id, elem_tag, elem_type, node_ids_json, section_tag, material_tag, thickness_m,
                                         cross_section_id, gj_strategy, gj_manual_value, gj_torsion_task_id,
                                         target_mesh_length_m)
               VALUES (@sid, @tag, @etype, @nids, @stag, @mtag, @thk, @csid, @gjs, @gjv, @gjt, @tml)
            """;
            elemCmd.Parameters.Add("@sid",  Microsoft.Data.Sqlite.SqliteType.Integer);
            elemCmd.Parameters.Add("@tag",  Microsoft.Data.Sqlite.SqliteType.Text);
            elemCmd.Parameters.Add("@etype",Microsoft.Data.Sqlite.SqliteType.Text);
            elemCmd.Parameters.Add("@nids", Microsoft.Data.Sqlite.SqliteType.Text);
            elemCmd.Parameters.Add("@stag", Microsoft.Data.Sqlite.SqliteType.Text);
            elemCmd.Parameters.Add("@mtag", Microsoft.Data.Sqlite.SqliteType.Text);
            elemCmd.Parameters.Add("@thk",  Microsoft.Data.Sqlite.SqliteType.Real);
            elemCmd.Parameters.Add("@csid", Microsoft.Data.Sqlite.SqliteType.Integer);
            elemCmd.Parameters.Add("@gjs",  Microsoft.Data.Sqlite.SqliteType.Text);
            elemCmd.Parameters.Add("@gjv",  Microsoft.Data.Sqlite.SqliteType.Real);
            elemCmd.Parameters.Add("@gjt",  Microsoft.Data.Sqlite.SqliteType.Integer);
            elemCmd.Parameters.Add("@tml",  Microsoft.Data.Sqlite.SqliteType.Real);
            foreach (var e in members)
            {
               elemCmd.Parameters["@sid"].Value   = schemaId;
               elemCmd.Parameters["@tag"].Value   = e.ElemTag;
               elemCmd.Parameters["@etype"].Value = e.ElemType;
               elemCmd.Parameters["@nids"].Value  = e.NodeIdsJson;
               elemCmd.Parameters["@stag"].Value  = (object?)e.SectionTag ?? DBNull.Value;
               elemCmd.Parameters["@mtag"].Value  = (object?)e.MaterialTag ?? DBNull.Value;
               elemCmd.Parameters["@thk"].Value   = e.ThicknessM.HasValue ? e.ThicknessM.Value : DBNull.Value;
               elemCmd.Parameters["@csid"].Value  = (object?)e.CrossSectionId ?? DBNull.Value;
               elemCmd.Parameters["@gjs"].Value   = e.GjStrategy;
               elemCmd.Parameters["@gjv"].Value   = (object?)e.GjManualValue ?? DBNull.Value;
               elemCmd.Parameters["@gjt"].Value   = (object?)e.GjTorsionTaskId ?? DBNull.Value;
               elemCmd.Parameters["@tml"].Value   = (object?)e.TargetMeshLengthM ?? DBNull.Value;
               elemCmd.ExecuteNonQuery();
            }

            var schema = FemSchemas.FirstOrDefault(s => s.Id == schemaId);
            foreach (var g in memberGroups)
            {
               // fem_member_groups для схемы уже полностью удалены выше — старый g.Id (если он остался
               // от предыдущего сохранения) указывал бы на строку, которой больше нет, и
               // SaveFemMemberGroupCore молча выполнил бы UPDATE по несуществующему id.
               g.Id = 0;
               SaveFemMemberGroupCore(g, schemaId);
            }

            if (schema != null)
            {
               schema.MemberGroups.Clear();
               foreach (var g in memberGroups.Where(g => g.SchemaId == schemaId))
                  schema.MemberGroups.Add(g);
            }

            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }

      /// <summary>
      /// Атомарно заменяет группы конструктивных элементов схемы, не затрагивая узлы/элементы —
      /// используется импортом топологии напрямую в сетку (SaveFemMeshSnapshot), где узлы и
      /// элементы конструктивного слоя (fem_nodes/fem_members) не участвуют.
      /// </summary>
      public void SaveFemMemberGroups(int schemaId, IReadOnlyList<CScore.Fem.FemMemberGroup> memberGroups)
      {
         using var tx = _connection.BeginTransaction();
         try
         {
            using (var delCmd = _connection.CreateCommand())
            {
               delCmd.CommandText = "DELETE FROM fem_member_groups WHERE schema_id=@sid";
               delCmd.Parameters.AddWithValue("@sid", schemaId);
               delCmd.ExecuteNonQuery();
            }

            foreach (var g in memberGroups)
            {
               g.Id = 0;
               SaveFemMemberGroupCore(g, schemaId);
            }

            var schema = FemSchemas.FirstOrDefault(s => s.Id == schemaId);
            if (schema != null)
            {
               schema.MemberGroups.Clear();
               foreach (var g in memberGroups.Where(g => g.SchemaId == schemaId))
                  schema.MemberGroups.Add(g);
            }

            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }

      /// <summary>
      /// Атомарно заменяет узлы, элементы, члены, загружения и узловые нагрузки схемы
      /// слепком из FemSchemaEditSession. В отличие от SaveFemTopology, не консервирует
      /// старые нагрузки по тегу — сессия уже несёт актуальный набор.
      /// </summary>
      public void SaveFemSchemaEdit(
         int schemaId,
         IReadOnlyList<CScore.Fem.FemNode>        nodes,
         IReadOnlyList<CScore.Fem.FemMember>      members,
         IReadOnlyList<CScore.Fem.FemMemberGroup> memberGroups,
         IReadOnlyList<CScore.Fem.FemLoadCase>    loadCases,
         IReadOnlyList<CScore.Fem.FemNodeLoad>    nodeLoads,
         IReadOnlyList<CScore.Fem.FemLoadDefinition>? definitions = null)
      {
         var newTags = nodes.Select(n => n.NodeTag).ToList();
         if (newTags.Count != newTags.Distinct(StringComparer.Ordinal).Count())
            throw new InvalidOperationException("Теги узлов FEM-схемы должны быть уникальными.");

         using var tx = _connection.BeginTransaction();
         try
         {
            using (var delCmd = _connection.CreateCommand())
            {
               delCmd.CommandText = """
                  DELETE FROM fem_node_loads    WHERE schema_id=@sid;
                  DELETE FROM fem_load_definitions WHERE schema_id=@sid;
                  DELETE FROM fem_load_cases    WHERE schema_id=@sid;
                  DELETE FROM fem_member_groups WHERE schema_id=@sid;
                  DELETE FROM fem_members       WHERE schema_id=@sid;
                  DELETE FROM fem_nodes         WHERE schema_id=@sid;
               """;
               delCmd.Parameters.AddWithValue("@sid", schemaId);
               delCmd.ExecuteNonQuery();
            }

            var newNodeIdByOld = new Dictionary<int, int>();
            using (var nodeCmd = _connection.CreateCommand())
            {
               nodeCmd.CommandText = """
                  INSERT INTO fem_nodes (schema_id, node_tag, x, y, z, dof_mask)
                  VALUES (@sid, @tag, @x, @y, @z, @dm);
                  SELECT last_insert_rowid();
               """;
               nodeCmd.Parameters.Add("@sid", Microsoft.Data.Sqlite.SqliteType.Integer);
               nodeCmd.Parameters.Add("@tag", Microsoft.Data.Sqlite.SqliteType.Text);
               nodeCmd.Parameters.Add("@x",   Microsoft.Data.Sqlite.SqliteType.Real);
               nodeCmd.Parameters.Add("@y",   Microsoft.Data.Sqlite.SqliteType.Real);
               nodeCmd.Parameters.Add("@z",   Microsoft.Data.Sqlite.SqliteType.Real);
               nodeCmd.Parameters.Add("@dm",  Microsoft.Data.Sqlite.SqliteType.Integer);
               foreach (var n in nodes)
               {
                  int oldId = n.Id;
                  nodeCmd.Parameters["@sid"].Value = schemaId;
                  nodeCmd.Parameters["@tag"].Value = n.NodeTag;
                  nodeCmd.Parameters["@x"].Value   = n.X;
                  nodeCmd.Parameters["@y"].Value   = n.Y;
                  nodeCmd.Parameters["@z"].Value   = n.Z;
                  nodeCmd.Parameters["@dm"].Value  = n.DofMask;
                  int newId = (int)(long)nodeCmd.ExecuteScalar()!;
                  newNodeIdByOld[oldId] = newId;
                  n.Id = newId;
                  n.SchemaId = schemaId;
               }
            }

            using (var elemCmd = _connection.CreateCommand())
            {
               elemCmd.CommandText = """
                  INSERT INTO fem_members (schema_id, elem_tag, elem_type, node_ids_json, section_tag, material_tag, thickness_m,
                                            cross_section_id, gj_strategy, gj_manual_value, gj_torsion_task_id,
                                            target_mesh_length_m)
                  VALUES (@sid, @tag, @etype, @nids, @stag, @mtag, @thk, @csid, @gjs, @gjv, @gjt, @tml);
                  SELECT last_insert_rowid();
               """;
               elemCmd.Parameters.Add("@sid",  Microsoft.Data.Sqlite.SqliteType.Integer);
               elemCmd.Parameters.Add("@tag",  Microsoft.Data.Sqlite.SqliteType.Text);
               elemCmd.Parameters.Add("@etype",Microsoft.Data.Sqlite.SqliteType.Text);
               elemCmd.Parameters.Add("@nids", Microsoft.Data.Sqlite.SqliteType.Text);
               elemCmd.Parameters.Add("@stag", Microsoft.Data.Sqlite.SqliteType.Text);
               elemCmd.Parameters.Add("@mtag", Microsoft.Data.Sqlite.SqliteType.Text);
               elemCmd.Parameters.Add("@thk",  Microsoft.Data.Sqlite.SqliteType.Real);
               elemCmd.Parameters.Add("@csid", Microsoft.Data.Sqlite.SqliteType.Integer);
               elemCmd.Parameters.Add("@gjs",  Microsoft.Data.Sqlite.SqliteType.Text);
               elemCmd.Parameters.Add("@gjv",  Microsoft.Data.Sqlite.SqliteType.Real);
               elemCmd.Parameters.Add("@gjt",  Microsoft.Data.Sqlite.SqliteType.Integer);
               elemCmd.Parameters.Add("@tml",  Microsoft.Data.Sqlite.SqliteType.Real);
               foreach (var e in members)
               {
                  elemCmd.Parameters["@sid"].Value   = schemaId;
                  elemCmd.Parameters["@tag"].Value   = e.ElemTag;
                  elemCmd.Parameters["@etype"].Value = e.ElemType;
                  elemCmd.Parameters["@nids"].Value  = e.NodeIdsJson;
                  elemCmd.Parameters["@stag"].Value  = (object?)e.SectionTag ?? DBNull.Value;
                  elemCmd.Parameters["@mtag"].Value  = (object?)e.MaterialTag ?? DBNull.Value;
                  elemCmd.Parameters["@thk"].Value   = e.ThicknessM.HasValue ? e.ThicknessM.Value : DBNull.Value;
                  elemCmd.Parameters["@csid"].Value  = (object?)e.CrossSectionId ?? DBNull.Value;
                  elemCmd.Parameters["@gjs"].Value   = e.GjStrategy;
                  elemCmd.Parameters["@gjv"].Value   = (object?)e.GjManualValue ?? DBNull.Value;
                  elemCmd.Parameters["@gjt"].Value   = (object?)e.GjTorsionTaskId ?? DBNull.Value;
                  elemCmd.Parameters["@tml"].Value   = (object?)e.TargetMeshLengthM ?? DBNull.Value;
                  int newId = (int)(long)elemCmd.ExecuteScalar()!;
                  e.Id = newId;
                  e.SchemaId = schemaId;
               }
            }

            foreach (var g in memberGroups)
            {
               // fem_member_groups для схемы уже полностью удалены выше — старый g.Id (если он остался
               // от предыдущего сохранения) указывал бы на строку, которой больше нет, и
               // SaveFemMemberGroupCore молча выполнил бы UPDATE по несуществующему id.
               g.Id = 0;
               SaveFemMemberGroupCore(g, schemaId);
            }

            var newLoadCaseIdByOld = new Dictionary<int, int>();
            using (var lcCmd = _connection.CreateCommand())
            {
               lcCmd.CommandText = """
                  INSERT INTO fem_load_cases
                     (schema_id, tag, load_type, sp20_type, sp20_group, gamma_f_unfav, gamma_f_fav, psi1, psi2)
                  VALUES (@sid, @tag, @lt, @st, @sg, @gu, @gf, @p1, @p2);
                  SELECT last_insert_rowid();
               """;
               foreach (var lc in loadCases)
               {
                  lcCmd.Parameters.Clear();
                  lcCmd.Parameters.AddWithValue("@sid", schemaId);
                  lcCmd.Parameters.AddWithValue("@tag", lc.Tag);
                  lcCmd.Parameters.AddWithValue("@lt",  (object?)lc.LoadType   ?? DBNull.Value);
                  lcCmd.Parameters.AddWithValue("@st",  lc.Sp20Type);
                  lcCmd.Parameters.AddWithValue("@sg",  (object?)lc.Sp20Group ?? DBNull.Value);
                  lcCmd.Parameters.AddWithValue("@gu",  (object?)lc.GammaFUnfav ?? DBNull.Value);
                  lcCmd.Parameters.AddWithValue("@gf",  (object?)lc.GammaFFav   ?? DBNull.Value);
                  lcCmd.Parameters.AddWithValue("@p1",  (object?)lc.Psi1 ?? DBNull.Value);
                  lcCmd.Parameters.AddWithValue("@p2",  (object?)lc.Psi2 ?? DBNull.Value);
                  int newId = (int)(long)lcCmd.ExecuteScalar()!;
                  newLoadCaseIdByOld[lc.Id] = newId;
                  lc.Id = newId;
                  lc.SchemaId = schemaId;
               }
            }

            using (var loadCmd = _connection.CreateCommand())
            {
               loadCmd.CommandText = """
                  INSERT INTO fem_node_loads (schema_id, load_case_id, node_id, fx, fy, fz, mx, my, mz)
                  VALUES (@sid, @lc, @nid, @fx, @fy, @fz, @mx, @my, @mz)
               """;
               foreach (var load in nodeLoads)
               {
                  loadCmd.Parameters.Clear();
                  loadCmd.Parameters.AddWithValue("@sid", schemaId);
                  loadCmd.Parameters.AddWithValue("@lc",  newLoadCaseIdByOld.TryGetValue(load.LoadCaseId, out var lcId) ? lcId : load.LoadCaseId);
                  if (!newNodeIdByOld.TryGetValue(load.NodeId, out int nodeId))
                     throw new InvalidOperationException($"Узловая нагрузка ссылается на отсутствующий узел {load.NodeId}.");
                  loadCmd.Parameters.AddWithValue("@nid", nodeId);
                  loadCmd.Parameters.AddWithValue("@fx", load.Fx);
                  loadCmd.Parameters.AddWithValue("@fy", load.Fy);
                  loadCmd.Parameters.AddWithValue("@fz", load.Fz);
                  loadCmd.Parameters.AddWithValue("@mx", load.Mx);
                  loadCmd.Parameters.AddWithValue("@my", load.My);
                  loadCmd.Parameters.AddWithValue("@mz", load.Mz);
                  loadCmd.ExecuteNonQuery();
                  load.NodeId = nodeId;
                  load.LoadCaseId = newLoadCaseIdByOld.TryGetValue(load.LoadCaseId, out var mappedLoadCaseId)
                     ? mappedLoadCaseId : load.LoadCaseId;
               }
            }

            var savedDefinitions = definitions ?? [];
            using (var definitionCmd = _connection.CreateCommand())
            {
               definitionCmd.CommandText = """
                  INSERT INTO fem_load_definitions (schema_id, tag, description, expression_json, source_kind, combination_type)
                  VALUES (@sid, @tag, @desc, @expr, @source, @type); SELECT last_insert_rowid();
               """;
               foreach (var definition in savedDefinitions)
               {
                  var expression = definition.GetExpression();
                  var rewritten = new CScore.Fem.FemLoadExpression
                  {
                     Mode = expression.Mode,
                     LoadCaseIds = expression.LoadCaseIds.Select(id => newLoadCaseIdByOld.TryGetValue(id, out var mapped) ? mapped : id).ToList(),
                     Terms = expression.Terms.Select(term => new CScore.Fem.FemLoadTerm
                     {
                        LoadCaseId = newLoadCaseIdByOld.TryGetValue(term.LoadCaseId, out var mapped) ? mapped : term.LoadCaseId,
                        Coefficient = term.Coefficient
                     }).ToList(),
                     CombinationType = expression.CombinationType
                  };
                  definition.SetExpression(rewritten);
                  definitionCmd.Parameters.Clear();
                  AddFemLoadDefinitionParameters(definitionCmd, definition);
                  definition.Id = (int)(long)definitionCmd.ExecuteScalar()!;
                  definition.SchemaId = schemaId;
               }
            }

            using (var analysisCmd = _connection.CreateCommand())
            {
               analysisCmd.CommandText = "SELECT id, load_expression_json FROM fem_analyses WHERE schema_id=@sid";
               analysisCmd.Parameters.AddWithValue("@sid", schemaId);
               var analysesToUpdate = new List<(int Id, string Json)>();
               using (var rdr = analysisCmd.ExecuteReader())
               {
                  while (rdr.Read())
                  {
                     if (!rdr.IsDBNull(1))
                        analysesToUpdate.Add((rdr.GetInt32(0), rdr.GetString(1)));
                  }
               }
               foreach (var a in analysesToUpdate)
               {
                  var expression = System.Text.Json.JsonSerializer.Deserialize<CScore.Fem.FemLoadExpression>(a.Json);
                  if (expression != null)
                  {
                     var rewritten = new CScore.Fem.FemLoadExpression
                     {
                        Mode = expression.Mode,
                        LoadCaseIds = expression.LoadCaseIds.Select(id => newLoadCaseIdByOld.TryGetValue(id, out var mapped) ? mapped : id).ToList(),
                        Terms = expression.Terms.Select(term => new CScore.Fem.FemLoadTerm
                        {
                           LoadCaseId = newLoadCaseIdByOld.TryGetValue(term.LoadCaseId, out var mapped) ? mapped : term.LoadCaseId,
                           Coefficient = term.Coefficient
                        }).ToList(),
                        CombinationType = expression.CombinationType
                     };
                     
                     var newJson = System.Text.Json.JsonSerializer.Serialize(rewritten);
                     if (newJson != a.Json)
                     {
                        using var updateCmd = _connection.CreateCommand();
                        updateCmd.CommandText = "UPDATE fem_analyses SET load_expression_json=@json WHERE id=@id";
                        updateCmd.Parameters.AddWithValue("@json", newJson);
                        updateCmd.Parameters.AddWithValue("@id", a.Id);
                        updateCmd.ExecuteNonQuery();
                        
                        var sc = FemSchemas.FirstOrDefault(s => s.Id == schemaId);
                        if (sc != null)
                        {
                           var analysisObj = sc.Analyses.FirstOrDefault(an => an.Id == a.Id);
                           if (analysisObj != null) analysisObj.LoadExpressionJson = newJson;
                        }
                     }
                  }
               }
            }

            var schema = FemSchemas.FirstOrDefault(s => s.Id == schemaId);
            if (schema != null)
            {
               schema.MemberGroups.Clear();
               foreach (var g in memberGroups) schema.MemberGroups.Add(g);
               schema.LoadCases.Clear();
               foreach (var lc in loadCases) schema.LoadCases.Add(lc);
               schema.LoadDefinitions.Clear();
               foreach (var definition in savedDefinitions) schema.LoadDefinitions.Add(definition);
            }

            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }

      void SaveFemMemberGroupCore(CScore.Fem.FemMemberGroup g, int schemaId)
      {
         using var cmd = _connection.CreateCommand();
         if (g.Id == 0)
         {
            cmd.CommandText = """
               INSERT INTO fem_member_groups
                   (schema_id, tag, member_type, member_tags_json, plate_section_id, force_set_id, design_params_json)
               VALUES (@sid, @tag, @mtype, @mtags, @psid, @fsid, @dp);
               SELECT last_insert_rowid();
            """;
            cmd.Parameters.AddWithValue("@sid",   schemaId);
            cmd.Parameters.AddWithValue("@tag",   g.Tag);
            cmd.Parameters.AddWithValue("@mtype", (object?)g.MemberType       ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@mtags", g.MemberTagsJson);
            cmd.Parameters.AddWithValue("@psid",  (object?)g.PlateSectionId   ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@fsid",  (object?)g.ForceSetId       ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@dp",    (object?)g.DesignParamsJson ?? DBNull.Value);
            g.Id = (int)(long)cmd.ExecuteScalar()!;
            g.SchemaId = schemaId;
         }
         else
         {
            cmd.CommandText = """
               UPDATE fem_member_groups SET tag=@tag, member_type=@mtype, member_tags_json=@mtags,
               plate_section_id=@psid, force_set_id=@fsid, design_params_json=@dp WHERE id=@id
            """;
            cmd.Parameters.AddWithValue("@tag",   g.Tag);
            cmd.Parameters.AddWithValue("@mtype", (object?)g.MemberType       ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@mtags", g.MemberTagsJson);
            cmd.Parameters.AddWithValue("@psid",  (object?)g.PlateSectionId   ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@fsid",  (object?)g.ForceSetId       ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@dp",    (object?)g.DesignParamsJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id",    g.Id);
            cmd.ExecuteNonQuery();
         }
      }

      /// <summary>
      /// Создаёт заглушки FemMember для элементов из списка, которых ещё нет в схеме.
      /// Используется при импорте усилий из ЛИРЫ без предварительного импорта топологии.
      /// </summary>
      public void AddFemElementStubs(int schemaId, IReadOnlyList<int> liraElemIds)
      {
         if (liraElemIds.Count == 0) return;
         var existing = GetFemMembers(schemaId)
            .Select(e => e.ElemTag)
            .ToHashSet();

         using var tx = _connection.BeginTransaction();
         try
         {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
               INSERT INTO fem_members (schema_id, elem_tag, elem_type, node_ids_json, material_tag, target_mesh_length_m)
               VALUES (@sid, @tag, 'beam', '[]', NULL, NULL)
            """;
            cmd.Parameters.Add("@sid", Microsoft.Data.Sqlite.SqliteType.Integer);
            cmd.Parameters.Add("@tag", Microsoft.Data.Sqlite.SqliteType.Text);
            cmd.Parameters["@sid"].Value = schemaId;
            foreach (int id in liraElemIds)
            {
               var tag = id.ToString();
               if (existing.Contains(tag)) continue;
               cmd.Parameters["@tag"].Value = tag;
               cmd.ExecuteNonQuery();
               existing.Add(tag);
            }
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }

      /// <summary>Возвращает все узлы схемы.</summary>
      public List<CScore.Fem.FemNode> GetFemNodes(int schemaId)
      {
         var result = new List<CScore.Fem.FemNode>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT id, node_tag, x, y, z, dof_mask FROM fem_nodes WHERE schema_id=@sid ORDER BY CAST(node_tag AS INTEGER)";
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var rdr = cmd.ExecuteReader();
         while (rdr.Read())
            result.Add(new CScore.Fem.FemNode
            {
               Id       = rdr.GetInt32(0),
               SchemaId = schemaId,
               NodeTag  = rdr.GetString(1),
               X        = rdr.GetDouble(2),
               Y        = rdr.GetDouble(3),
               Z        = rdr.GetDouble(4),
               DofMask  = rdr.GetInt32(5),
            });
         return result;
      }

      /// <summary>Возвращает все конструктивные элементы схемы (без загрузки в наблюдаемые коллекции).</summary>
      public List<CScore.Fem.FemMember> GetFemMembers(int schemaId)
      {
         var result = new List<CScore.Fem.FemMember>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT id, elem_tag, elem_type, node_ids_json, section_tag, material_tag, thickness_m, cross_section_id, gj_strategy, gj_manual_value, gj_torsion_task_id, target_mesh_length_m, plate_section_id, force_set_id, design_params_json FROM fem_members WHERE schema_id=@sid";
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var rdr = cmd.ExecuteReader();
         while (rdr.Read())
            result.Add(new CScore.Fem.FemMember
            {
               Id          = rdr.GetInt32(0),
               SchemaId    = schemaId,
               ElemTag     = rdr.GetString(1),
               ElemType    = rdr.GetString(2),
               NodeIdsJson = rdr.GetString(3),
               SectionTag  = rdr.IsDBNull(4) ? null : rdr.GetString(4),
               MaterialTag = rdr.IsDBNull(5) ? null : rdr.GetString(5),
               ThicknessM  = rdr.IsDBNull(6) ? null : rdr.GetDouble(6),
               CrossSectionId  = rdr.IsDBNull(7) ? null : rdr.GetInt32(7),
               GjStrategy      = rdr.GetString(8),
               GjManualValue   = rdr.IsDBNull(9) ? null : rdr.GetDouble(9),
               GjTorsionTaskId = rdr.IsDBNull(10) ? null : rdr.GetInt32(10),
               TargetMeshLengthM = rdr.IsDBNull(11) ? null : rdr.GetDouble(11),
               PlateSectionId    = rdr.IsDBNull(12) ? null : rdr.GetInt32(12),
               ForceSetId        = rdr.IsDBNull(13) ? null : rdr.GetInt32(13),
               DesignParamsJson  = rdr.IsDBNull(14) ? null : rdr.GetString(14),
            });
         return result;
      }

      /// <summary>Атомарно заменяет сохранённый mesh-слепок FEM-схемы.</summary>
      public void SaveFemMeshSnapshot(
         int schemaId,
         IReadOnlyList<CScore.Fem.FemMeshNode> nodes,
         IReadOnlyList<CScore.Fem.FemElement> elements)
      {
         using var tx = _connection.BeginTransaction();
         try
         {
            using (var deleteCmd = _connection.CreateCommand())
            {
               deleteCmd.CommandText = """
                  DELETE FROM fem_elements    WHERE schema_id=@sid;
                  DELETE FROM fem_mesh_nodes  WHERE schema_id=@sid;
               """;
               deleteCmd.Parameters.AddWithValue("@sid", schemaId);
               deleteCmd.ExecuteNonQuery();
            }

            using (var nodeCmd = _connection.CreateCommand())
            {
               nodeCmd.CommandText = """
                  INSERT INTO fem_mesh_nodes
                     (schema_id, node_tag, x, y, z, source_node_tag, source_member_tag)
                  VALUES (@sid, @tag, @x, @y, @z, @source_node_tag, @source_member_tag);
                  SELECT last_insert_rowid();
               """;
               nodeCmd.Parameters.Add("@sid", Microsoft.Data.Sqlite.SqliteType.Integer);
               nodeCmd.Parameters.Add("@tag", Microsoft.Data.Sqlite.SqliteType.Text);
               nodeCmd.Parameters.Add("@x", Microsoft.Data.Sqlite.SqliteType.Real);
               nodeCmd.Parameters.Add("@y", Microsoft.Data.Sqlite.SqliteType.Real);
               nodeCmd.Parameters.Add("@z", Microsoft.Data.Sqlite.SqliteType.Real);
               nodeCmd.Parameters.Add("@source_node_tag", Microsoft.Data.Sqlite.SqliteType.Text);
               nodeCmd.Parameters.Add("@source_member_tag", Microsoft.Data.Sqlite.SqliteType.Text);

               foreach (var node in nodes)
               {
                  nodeCmd.Parameters["@sid"].Value = schemaId;
                  nodeCmd.Parameters["@tag"].Value = node.NodeTag;
                  nodeCmd.Parameters["@x"].Value = node.X;
                  nodeCmd.Parameters["@y"].Value = node.Y;
                  nodeCmd.Parameters["@z"].Value = node.Z;
                  nodeCmd.Parameters["@source_node_tag"].Value = (object?)node.SourceNodeTag ?? DBNull.Value;
                  nodeCmd.Parameters["@source_member_tag"].Value = (object?)node.SourceMemberTag ?? DBNull.Value;
                  node.Id = (int)(long)nodeCmd.ExecuteScalar()!;
                  node.SchemaId = schemaId;
               }
            }

            using (var elementCmd = _connection.CreateCommand())
            {
               elementCmd.CommandText = """
                  INSERT INTO fem_elements
                     (schema_id, elem_tag, node_ids_json, source_member_tag, cross_section_id,
                      gj_strategy, gj_manual_value, gj_torsion_task_id, elem_type, section_tag,
                      material_tag, thickness_m)
                  VALUES (@sid, @tag, @node_ids, @source_member_tag, @cross_section_id,
                          @gj_strategy, @gj_manual_value, @gj_torsion_task_id, @elem_type,
                          @section_tag, @material_tag, @thickness_m);
                  SELECT last_insert_rowid();
               """;
               elementCmd.Parameters.Add("@sid", Microsoft.Data.Sqlite.SqliteType.Integer);
               elementCmd.Parameters.Add("@tag", Microsoft.Data.Sqlite.SqliteType.Text);
               elementCmd.Parameters.Add("@node_ids", Microsoft.Data.Sqlite.SqliteType.Text);
               elementCmd.Parameters.Add("@source_member_tag", Microsoft.Data.Sqlite.SqliteType.Text);
               elementCmd.Parameters.Add("@cross_section_id", Microsoft.Data.Sqlite.SqliteType.Integer);
               elementCmd.Parameters.Add("@gj_strategy", Microsoft.Data.Sqlite.SqliteType.Text);
               elementCmd.Parameters.Add("@gj_manual_value", Microsoft.Data.Sqlite.SqliteType.Real);
               elementCmd.Parameters.Add("@gj_torsion_task_id", Microsoft.Data.Sqlite.SqliteType.Integer);
               elementCmd.Parameters.Add("@elem_type", Microsoft.Data.Sqlite.SqliteType.Text);
               elementCmd.Parameters.Add("@section_tag", Microsoft.Data.Sqlite.SqliteType.Text);
               elementCmd.Parameters.Add("@material_tag", Microsoft.Data.Sqlite.SqliteType.Text);
               elementCmd.Parameters.Add("@thickness_m", Microsoft.Data.Sqlite.SqliteType.Real);

               foreach (var element in elements)
               {
                  elementCmd.Parameters["@sid"].Value = schemaId;
                  elementCmd.Parameters["@tag"].Value = element.ElemTag;
                  elementCmd.Parameters["@node_ids"].Value = element.NodeIdsJson;
                  elementCmd.Parameters["@source_member_tag"].Value = (object?)element.SourceMemberTag ?? DBNull.Value;
                  elementCmd.Parameters["@cross_section_id"].Value = (object?)element.CrossSectionId ?? DBNull.Value;
                  elementCmd.Parameters["@gj_strategy"].Value = element.GjStrategy;
                  elementCmd.Parameters["@gj_manual_value"].Value = (object?)element.GjManualValue ?? DBNull.Value;
                  elementCmd.Parameters["@gj_torsion_task_id"].Value = (object?)element.GjTorsionTaskId ?? DBNull.Value;
                  elementCmd.Parameters["@elem_type"].Value = element.ElemType;
                  elementCmd.Parameters["@section_tag"].Value = (object?)element.SectionTag ?? DBNull.Value;
                  elementCmd.Parameters["@material_tag"].Value = (object?)element.MaterialTag ?? DBNull.Value;
                  elementCmd.Parameters["@thickness_m"].Value = (object?)element.ThicknessM ?? DBNull.Value;
                  element.Id = (int)(long)elementCmd.ExecuteScalar()!;
                  element.SchemaId = schemaId;
               }
            }

            tx.Commit();
         }
         catch
         {
            tx.Rollback();
            throw;
         }
      }

      /// <summary>Возвращает сохранённые узлы mesh-слепка FEM-схемы.</summary>
      public List<CScore.Fem.FemMeshNode> GetFemMeshNodes(int schemaId)
      {
         var result = new List<CScore.Fem.FemMeshNode>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT id, node_tag, x, y, z, source_node_tag, source_member_tag
            FROM fem_mesh_nodes
            WHERE schema_id=@sid
            ORDER BY id
         """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var rdr = cmd.ExecuteReader();
         while (rdr.Read())
            result.Add(new CScore.Fem.FemMeshNode
            {
               Id = rdr.GetInt32(0),
               SchemaId = schemaId,
               NodeTag = rdr.GetString(1),
               X = rdr.GetDouble(2),
               Y = rdr.GetDouble(3),
               Z = rdr.GetDouble(4),
               SourceNodeTag = rdr.IsDBNull(5) ? null : rdr.GetString(5),
               SourceMemberTag = rdr.IsDBNull(6) ? null : rdr.GetString(6),
            });
         return result;
      }

      /// <summary>Возвращает сохранённые конечные элементы mesh-слепка FEM-схемы.</summary>
      public List<CScore.Fem.FemElement> GetFemMeshElements(int schemaId)
      {
         var result = new List<CScore.Fem.FemElement>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT id, elem_tag, node_ids_json, source_member_tag, cross_section_id,
                   gj_strategy, gj_manual_value, gj_torsion_task_id, elem_type, section_tag,
                   material_tag, thickness_m
            FROM fem_elements
            WHERE schema_id=@sid
            ORDER BY id
         """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var rdr = cmd.ExecuteReader();
         while (rdr.Read())
            result.Add(new CScore.Fem.FemElement
            {
               Id = rdr.GetInt32(0),
               SchemaId = schemaId,
               ElemTag = rdr.GetString(1),
               NodeIdsJson = rdr.GetString(2),
               SourceMemberTag = rdr.IsDBNull(3) ? null : rdr.GetString(3),
               CrossSectionId = rdr.IsDBNull(4) ? null : rdr.GetInt32(4),
               GjStrategy = rdr.GetString(5),
               GjManualValue = rdr.IsDBNull(6) ? null : rdr.GetDouble(6),
               GjTorsionTaskId = rdr.IsDBNull(7) ? null : rdr.GetInt32(7),
               ElemType = rdr.GetString(8),
               SectionTag = rdr.IsDBNull(9) ? null : rdr.GetString(9),
               MaterialTag = rdr.IsDBNull(10) ? null : rdr.GetString(10),
               ThicknessM = rdr.IsDBNull(11) ? null : rdr.GetDouble(11),
            });
         return result;
      }

      /// <summary>Возвращает (nodeCount, barCount, shellCount) для быстрого отображения в дереве.</summary>
      public (int nodes, int bars, int shells) GetFemTopologyCounts(int schemaId)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM fem_nodes   WHERE schema_id=@sid),
              (SELECT COUNT(*) FROM fem_members WHERE schema_id=@sid AND elem_type='beam'),
              (SELECT COUNT(*) FROM fem_members WHERE schema_id=@sid AND elem_type='shell')
         """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var r = cmd.ExecuteReader();
         if (!r.Read()) return (0, 0, 0);
         return (r.GetInt32(0), r.GetInt32(1), r.GetInt32(2));
      }

      /// <summary>Возвращает число узлов и элементов сохранённой расчётной сетки.</summary>
      /// <summary>Возвращает число узлов, стержневых и пластинчатых элементов сохранённой расчётной сетки.</summary>
      public (int nodes, int bars, int shells) GetFemMeshSnapshotCounts(int schemaId)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM fem_mesh_nodes WHERE schema_id=@sid),
              (SELECT COUNT(*) FROM fem_elements   WHERE schema_id=@sid AND elem_type='beam'),
              (SELECT COUNT(*) FROM fem_elements   WHERE schema_id=@sid AND elem_type='shell')
         """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var r = cmd.ExecuteReader();
         if (!r.Read()) return (0, 0, 0);
         return (r.GetInt32(0), r.GetInt32(1), r.GetInt32(2));
      }

      public void SaveFemMemberGroup(CScore.Fem.FemMemberGroup g)
      {
         using var tx = _connection.BeginTransaction();
         try { SaveFemMemberGroupCore(g, g.SchemaId); tx.Commit(); }
         catch { tx.Rollback(); throw; }
      }

      public void DeleteFemMemberGroup(CScore.Fem.FemMemberGroup g)
      {
         if (g.Id == 0) return;
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "DELETE FROM fem_member_groups WHERE id = @id";
         cmd.Parameters.AddWithValue("@id", g.Id);
         cmd.ExecuteNonQuery();
         var schema = FemSchemas.FirstOrDefault(s => s.Id == g.SchemaId);
         schema?.MemberGroups.Remove(g);
      }

      /// <summary>Сохраняет один конструктивный элемент (INSERT/UPDATE по m.Id). Используется точечными
      /// операциями вне полной пересборки топологии — например, массовым назначением сечения всем
      /// элементам группы из FemMemberEditorPage.</summary>
      public void SaveFemMember(CScore.Fem.FemMember m)
      {
         using var cmd = _connection.CreateCommand();
         if (m.Id == 0)
         {
            cmd.CommandText = """
               INSERT INTO fem_members (schema_id, elem_tag, elem_type, node_ids_json, section_tag, material_tag, thickness_m,
                                         cross_section_id, gj_strategy, gj_manual_value, gj_torsion_task_id,
                                         target_mesh_length_m, plate_section_id, force_set_id, design_params_json)
               VALUES (@sid, @tag, @etype, @nids, @stag, @mtag, @thk, @csid, @gjs, @gjv, @gjt, @tml, @psid, @fsid, @dp);
               SELECT last_insert_rowid();
            """;
            cmd.Parameters.AddWithValue("@sid",   m.SchemaId);
            cmd.Parameters.AddWithValue("@tag",   m.ElemTag);
            cmd.Parameters.AddWithValue("@etype", m.ElemType);
            cmd.Parameters.AddWithValue("@nids",  m.NodeIdsJson);
            cmd.Parameters.AddWithValue("@stag",  (object?)m.SectionTag ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@mtag",  (object?)m.MaterialTag ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@thk",   m.ThicknessM.HasValue ? m.ThicknessM.Value : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@csid",  (object?)m.CrossSectionId  ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@gjs",   m.GjStrategy);
            cmd.Parameters.AddWithValue("@gjv",   (object?)m.GjManualValue   ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@gjt",   (object?)m.GjTorsionTaskId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@tml",   (object?)m.TargetMeshLengthM ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@psid",  (object?)m.PlateSectionId   ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@fsid",  (object?)m.ForceSetId       ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@dp",    (object?)m.DesignParamsJson ?? DBNull.Value);
            m.Id = (int)(long)cmd.ExecuteScalar()!;
         }
         else
         {
            cmd.CommandText = """
               UPDATE fem_members SET elem_tag=@tag, elem_type=@etype, node_ids_json=@nids, section_tag=@stag,
               material_tag=@mtag, thickness_m=@thk, cross_section_id=@csid, gj_strategy=@gjs, gj_manual_value=@gjv,
               gj_torsion_task_id=@gjt, target_mesh_length_m=@tml, plate_section_id=@psid, force_set_id=@fsid,
               design_params_json=@dp
               WHERE id=@id
            """;
            cmd.Parameters.AddWithValue("@tag",   m.ElemTag);
            cmd.Parameters.AddWithValue("@etype", m.ElemType);
            cmd.Parameters.AddWithValue("@nids",  m.NodeIdsJson);
            cmd.Parameters.AddWithValue("@stag",  (object?)m.SectionTag ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@mtag",  (object?)m.MaterialTag ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@thk",   m.ThicknessM.HasValue ? m.ThicknessM.Value : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@csid",  (object?)m.CrossSectionId  ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@gjs",   m.GjStrategy);
            cmd.Parameters.AddWithValue("@gjv",   (object?)m.GjManualValue   ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@gjt",   (object?)m.GjTorsionTaskId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@tml",   (object?)m.TargetMeshLengthM ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@psid",  (object?)m.PlateSectionId   ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@fsid",  (object?)m.ForceSetId       ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@dp",    (object?)m.DesignParamsJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id",    m.Id);
            cmd.ExecuteNonQuery();
         }
      }

      public void SaveFemCheck(CScore.Fem.FemCheck check)
      {
         using var tx = _connection.BeginTransaction();
         try
         {
            using var cmd = _connection.CreateCommand();
            if (check.Id == 0)
            {
               cmd.CommandText = """
                  INSERT INTO fem_checks (schema_id, member_id, norm_code, params_json, result_id,
                                          tag, force_set_ids_json, calc_type_override, element_id)
                  VALUES (@sid, @mid, @nc, @pj, @rid, @tag, @fsids, @cto, @eid);
                  SELECT last_insert_rowid();
               """;
               cmd.Parameters.AddWithValue("@sid",  check.SchemaId);
               cmd.Parameters.AddWithValue("@mid",  check.MemberId);
               cmd.Parameters.AddWithValue("@nc",   check.NormCode);
               cmd.Parameters.AddWithValue("@pj",   (object?)check.ParamsJson ?? DBNull.Value);
               cmd.Parameters.AddWithValue("@rid",  (object?)check.ResultId   ?? DBNull.Value);
               cmd.Parameters.AddWithValue("@tag",  check.Tag);
               cmd.Parameters.AddWithValue("@fsids", check.ForceSetIdsJson);
               cmd.Parameters.AddWithValue("@cto",  (object?)check.CalcTypeOverride ?? DBNull.Value);
               cmd.Parameters.AddWithValue("@eid",  (object?)check.ElementId ?? DBNull.Value);
               check.Id = (int)(long)cmd.ExecuteScalar()!;
               FemChecks.Add(check);
            }
            else
            {
               cmd.CommandText = """
                  UPDATE fem_checks SET norm_code=@nc, params_json=@pj, result_id=@rid,
                  tag=@tag, force_set_ids_json=@fsids, calc_type_override=@cto WHERE id=@id
               """;
               cmd.Parameters.AddWithValue("@nc",   check.NormCode);
               cmd.Parameters.AddWithValue("@pj",   (object?)check.ParamsJson ?? DBNull.Value);
               cmd.Parameters.AddWithValue("@rid",  (object?)check.ResultId   ?? DBNull.Value);
               cmd.Parameters.AddWithValue("@tag",  check.Tag);
               cmd.Parameters.AddWithValue("@fsids", check.ForceSetIdsJson);
               cmd.Parameters.AddWithValue("@cto",  (object?)check.CalcTypeOverride ?? DBNull.Value);
               cmd.Parameters.AddWithValue("@id",   check.Id);
               cmd.ExecuteNonQuery();
            }
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }

      public void DeleteFemCheck(CScore.Fem.FemCheck check)
      {
         if (check.Id == 0) return;
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "DELETE FROM fem_checks WHERE id = @id";
         cmd.Parameters.AddWithValue("@id", check.Id);
         cmd.ExecuteNonQuery();
         FemChecks.Remove(check);
      }

      public void DeleteAllFemChecks()
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "DELETE FROM fem_checks";
         cmd.ExecuteNonQuery();
         foreach (var s in FemSchemas)
            foreach (var m in s.MemberGroups)
               m.Checks.Clear();
         FemChecks.Clear();
      }

      /// <summary>Загружает последний CalcResult для указанного FemCheck по fem_check_id.</summary>
      public CalcResult? GetCalcResultByFemCheck(int femCheckId)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT id, task_id, task_kind, task_tag, created, status, data_json FROM calc_results WHERE fem_check_id = @fid ORDER BY id DESC LIMIT 1";
         cmd.Parameters.AddWithValue("@fid", femCheckId);
         using var r = cmd.ExecuteReader();
         if (!r.Read()) return null;
         return new CalcResult
         {
            Id       = r.GetInt32(0),
            TaskId   = r.GetInt32(1),
            TaskKind = r.GetString(2),
            TaskTag  = r.GetString(3),
            Created  = r.GetString(4),
            Status   = r.GetString(5),
            DataJson = r.GetString(6)
         };
      }

      /// <summary>Сохраняет CalcResult, связанный с FemCheck (task_id = 0, sentinel).</summary>
      public void SaveCalcResultRaw(CalcResult r, int femCheckId)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            INSERT INTO calc_results (task_id, task_kind, task_tag, created, status, data_json, fem_check_id)
            VALUES (0, @kind, @tag, @created, @status, @data, @fid);
            SELECT last_insert_rowid();
         """;
         cmd.Parameters.AddWithValue("@kind",    r.TaskKind);
         cmd.Parameters.AddWithValue("@tag",     r.TaskTag);
         cmd.Parameters.AddWithValue("@created", r.Created);
         cmd.Parameters.AddWithValue("@status",  r.Status);
         cmd.Parameters.AddWithValue("@data",    r.DataJson);
         cmd.Parameters.AddWithValue("@fid",     femCheckId);
         r.Id = (int)(long)cmd.ExecuteScalar()!;
         CalcResults.Add(r);
      }

      #endregion

      public void Dispose()
      {
         CheckpointAndClose();
      }
   }
}
