using System.Text.Json;
using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Planar;
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

      /// <summary>Виды вложений родителя, копируемые во фрагмент как источники армирования (решение 2 плана среза 3):
      /// RBT и ASP ЛИРЫ + вложения SCAD/ЛИРЫ про армирование, стальные профили и единицы.</summary>
      public static readonly IReadOnlyList<string> FragmentRebarSourceFileKinds =
      [
         FemSchemaSourceFileKind.ScadSelectedRebar, FemSchemaSourceFileKind.ScadConcreteGroups,
         FemSchemaSourceFileKind.ScadAssignedRebar, FemSchemaSourceFileKind.ScadSteelProfiles,
         FemSchemaSourceFileKind.LiraSteelProfiles, FemSchemaSourceFileKind.ScadSteelGroups, FemSchemaSourceFileKind.LiraUnits,
      ];

      /// <summary>Вид источника «типы армирования ЛИРЫ (RBT)» в <c>fem_fragment_rebar_sources</c>.</summary>
      public const string FragmentRebarSourceLiraRbt = "lira_rbt";
      /// <summary>Вид источника «подобранная арматура ЛИРЫ (ASP)» в <c>fem_fragment_rebar_sources</c>.</summary>
      public const string FragmentRebarSourceLiraAsp = "lira_asp";

      /// <summary>Заменяет рассечения, опорную сетку и нагрузки-поля фрагмента (одной транзакцией).</summary>
      public void ReplaceStoreyFragmentData(int schemaId, IEnumerable<FragmentCut> cuts, IEnumerable<FragmentRefElement> refElements,
         IEnumerable<FragmentFieldLoad> fieldLoads)
      {
         using var tx = _connection.BeginTransaction();
         try
         {
            using (var del = _connection.CreateCommand())
            {
               del.CommandText = """
                  DELETE FROM fem_fragment_cuts         WHERE schema_id=@sid;
                  DELETE FROM fem_fragment_ref_elements WHERE schema_id=@sid;
                  DELETE FROM fem_fragment_field_loads  WHERE schema_id=@sid;
                  """;
               del.Parameters.AddWithValue("@sid", schemaId);
               del.ExecuteNonQuery();
            }
            InsertStoreyFragmentDataCore(schemaId, cuts, refElements, fieldLoads, id => id);
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }

      /// <summary>Вставляет рассечения, опорную сетку и нагрузки-поля (внутри транзакции вызывающего);
      /// <paramref name="mapLoadCase"/> переводит Id загружений нагрузок-полей.</summary>
      void InsertStoreyFragmentDataCore(int schemaId, IEnumerable<FragmentCut> cuts, IEnumerable<FragmentRefElement> refElements,
         IEnumerable<FragmentFieldLoad> fieldLoads, Func<int, int> mapLoadCase)
      {
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = """
               INSERT INTO fem_fragment_cuts (schema_id, member_tag, kind, side, z, node_tag, segments_json)
               VALUES (@sid, @tag, @kind, @side, @z, @node, @seg)
               """;
            cmd.Parameters.AddWithValue("@sid", schemaId);
            var tag = cmd.Parameters.Add("@tag", SqliteType.Text);
            var kind = cmd.Parameters.Add("@kind", SqliteType.Text);
            var side = cmd.Parameters.Add("@side", SqliteType.Text);
            var z = cmd.Parameters.Add("@z", SqliteType.Real);
            var node = cmd.Parameters.Add("@node", SqliteType.Text);
            var seg = cmd.Parameters.Add("@seg", SqliteType.Text);
            foreach (var c in cuts)
            {
               tag.Value = c.MemberTag;
               kind.Value = c.Kind == FragmentCutKind.End ? "end" : "edge";
               side.Value = c.Side == FragmentCutSide.Bottom ? "bottom" : "top";
               z.Value = c.Z;
               node.Value = (object?)c.NodeTag ?? DBNull.Value;
               seg.Value = JsonSerializer.Serialize(c.Segments.Select(s => new[] { s.P.X, s.P.Y, s.P.Z, s.Q.X, s.Q.Y, s.Q.Z }));
               cmd.ExecuteNonQuery();
            }
         }
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = """
               INSERT INTO fem_fragment_ref_elements (schema_id, tag, elem_type, points_blob, stiffness_num, foundation_c1, source_member_tag)
               VALUES (@sid, @tag, @type, @pts, @snum, @c1, @smt)
               """;
            cmd.Parameters.AddWithValue("@sid", schemaId);
            var tag = cmd.Parameters.Add("@tag", SqliteType.Text);
            var type = cmd.Parameters.Add("@type", SqliteType.Text);
            var pts = cmd.Parameters.Add("@pts", SqliteType.Blob);
            var snum = cmd.Parameters.Add("@snum", SqliteType.Integer);
            var c1 = cmd.Parameters.Add("@c1", SqliteType.Real);
            var smt = cmd.Parameters.Add("@smt", SqliteType.Text);
            foreach (var e in refElements)
            {
               tag.Value = e.Tag;
               type.Value = e.ElemType;
               pts.Value = FragmentStoreCodec.EncodePoints(e.Points);
               snum.Value = (object?)e.StiffnessNum ?? DBNull.Value;
               c1.Value = (object?)e.FoundationC1 ?? DBNull.Value;
               smt.Value = (object?)e.SourceMemberTag ?? DBNull.Value;
               cmd.ExecuteNonQuery();
            }
         }
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = """
               INSERT INTO fem_fragment_field_loads (schema_id, load_case_id, load_kind, axis, coordinate_system, entries_blob)
               VALUES (@sid, @lc, @kind, @axis, @cs, @entries)
               """;
            cmd.Parameters.AddWithValue("@sid", schemaId);
            var lc = cmd.Parameters.Add("@lc", SqliteType.Integer);
            var kind = cmd.Parameters.Add("@kind", SqliteType.Text);
            var axis = cmd.Parameters.Add("@axis", SqliteType.Text);
            var cs = cmd.Parameters.Add("@cs", SqliteType.Text);
            var entries = cmd.Parameters.Add("@entries", SqliteType.Blob);
            foreach (var f in fieldLoads)
            {
               lc.Value = mapLoadCase(f.LoadCaseId);
               kind.Value = f.LoadKind;
               axis.Value = f.Axis;
               cs.Value = f.CoordinateSystem;
               entries.Value = FragmentStoreCodec.EncodeEntries(f.Entries);
               cmd.ExecuteNonQuery();
            }
         }
      }

      public List<FragmentCut> GetFragmentCuts(int schemaId)
      {
         var result = new List<FragmentCut>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT member_tag, kind, side, z, node_tag, segments_json FROM fem_fragment_cuts WHERE schema_id=@sid ORDER BY id";
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var r = cmd.ExecuteReader();
         while (r.Read())
         {
            var segments = (JsonSerializer.Deserialize<double[][]>(r.GetString(5)) ?? [])
               .Select(a => (new PlanarVector3(a[0], a[1], a[2]), new PlanarVector3(a[3], a[4], a[5]))).ToList();
            result.Add(new FragmentCut(r.GetString(0), r.GetString(1) == "end" ? FragmentCutKind.End : FragmentCutKind.Edge,
               r.GetString(2) == "bottom" ? FragmentCutSide.Bottom : FragmentCutSide.Top, r.GetDouble(3),
               r.IsDBNull(4) ? null : r.GetString(4), segments));
         }
         return result;
      }

      public List<FragmentRefElement> GetFragmentRefElements(int schemaId)
      {
         var result = new List<FragmentRefElement>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT tag, elem_type, points_blob, stiffness_num, foundation_c1, source_member_tag
            FROM fem_fragment_ref_elements WHERE schema_id=@sid ORDER BY id
            """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var r = cmd.ExecuteReader();
         while (r.Read())
            result.Add(new FragmentRefElement(r.GetString(0), r.GetString(1), FragmentStoreCodec.DecodePoints((byte[])r[2]),
               r.IsDBNull(3) ? null : r.GetInt32(3), r.IsDBNull(4) ? null : r.GetDouble(4), r.IsDBNull(5) ? null : r.GetString(5)));
         return result;
      }

      public List<FragmentFieldLoad> GetFragmentFieldLoads(int schemaId)
      {
         var result = new List<FragmentFieldLoad>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT load_case_id, load_kind, axis, entries_blob FROM fem_fragment_field_loads WHERE schema_id=@sid ORDER BY id";
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var r = cmd.ExecuteReader();
         while (r.Read())
            result.Add(new FragmentFieldLoad(r.GetInt32(0), r.GetString(1), r.GetString(2), FragmentStoreCodec.DecodeEntries((byte[])r[3])));
         return result;
      }

      /// <summary>Копии источников армирования родителя у фрагмента: вид, имя файла, данные.</summary>
      public List<(string Kind, string FileName, byte[] Data)> GetFragmentRebarSources(int schemaId)
      {
         var result = new List<(string, string, byte[])>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT kind, file_name, data FROM fem_fragment_rebar_sources WHERE schema_id=@sid ORDER BY kind";
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var r = cmd.ExecuteReader();
         while (r.Read()) result.Add((r.GetString(0), r.GetString(1), (byte[])r[2]));
         return result;
      }

      /// <summary>Копирует вложения-источники армирования и жёсткости схемы <paramref name="parentId"/> во фрагмент
      /// <paramref name="schemaId"/> (одной транзакцией).</summary>
      public void CopyFragmentRebarSources(int parentId, int schemaId)
      {
         using var tx = _connection.BeginTransaction();
         try
         {
            CopyFragmentRebarSourcesCore(parentId, schemaId);
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }

      /// <summary>SQL-копия вложений (RBT, ASP, виды <see cref="FragmentRebarSourceFileKinds"/>) и жёсткостей родителя —
      /// внутри транзакции вызывающего; прежние копии фрагмента заменяются.</summary>
      void CopyFragmentRebarSourcesCore(int parentId, int schemaId)
      {
         string kinds = string.Join(", ", FragmentRebarSourceFileKinds.Select(k => $"'{k}'"));
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = $"""
            DELETE FROM fem_fragment_rebar_sources WHERE schema_id=@sid;
            INSERT INTO fem_fragment_rebar_sources (schema_id, kind, file_name, data)
               SELECT @sid, '{FragmentRebarSourceLiraRbt}', file_name, data FROM fem_schema_reinforcement_files WHERE schema_id=@pid;
            INSERT INTO fem_fragment_rebar_sources (schema_id, kind, file_name, data)
               SELECT @sid, '{FragmentRebarSourceLiraAsp}', file_name, data FROM fem_schema_selected_reinforcement_files WHERE schema_id=@pid;
            INSERT INTO fem_fragment_rebar_sources (schema_id, kind, file_name, data)
               SELECT @sid, kind, file_name, data FROM fem_schema_source_files WHERE schema_id=@pid AND kind IN ({kinds});
            DELETE FROM fem_schema_stiffnesses WHERE schema_id=@sid;
            INSERT INTO fem_schema_stiffnesses (schema_id, num, kind_code, name, params, section_unit_m)
               SELECT @sid, num, kind_code, name, params, section_unit_m FROM fem_schema_stiffnesses WHERE schema_id=@pid;
            """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         cmd.Parameters.AddWithValue("@pid", parentId);
         cmd.ExecuteNonQuery();
      }

      /// <summary>
      /// Снимок полосы родителя для построителя (план среза 3, решение 3). Схемный уровень читается целиком и
      /// отсекается <see cref="StoreyFragmentBand.Trim"/>; сетка — КЭ, заходящие в полосу (тот же критерий, что у
      /// построителя: max z &gt; z₁ + tol и min z &lt; z₂ − tol), их узлы, нагрузки на эти КЭ/узлы и на оставшиеся КонЭ,
      /// нагрузки на группы — фильтром в SQL через временные таблицы.
      /// </summary>
      public StoreyFragmentParentSnapshot ReadStoreyFragmentSnapshot(int parentId, StoreyFragmentRequest request)
      {
         var holder = new FemSchema { Id = parentId };
         LoadFemSchemaCollections(new Dictionary<int, FemSchema> { [parentId] = holder }, parentId);
         var schemaLevel = StoreyFragmentBand.Trim(new StoreyFragmentParentSnapshot
         {
            Nodes = GetFemNodes(parentId),
            Members = GetFemMembers(parentId),
            Regions = GetPlanarRegions(parentId),
            LoadCases = GetFemLoadCases(parentId),
            LoadDefinitions = GetFemLoadDefinitions(parentId),
            MemberLoads = GetFemMemberLoads(parentId),
            NodeLoads = GetFemNodeLoads(parentId),
            KinematicLoads = GetFemKinematicLoads(parentId),
            RigidBodies = GetFemRigidBodies(parentId),
            Groups = holder.MemberGroups.ToList(),
         }, request);

         try
         {
            using (var cmd = _connection.CreateCommand())
            {
               cmd.CommandText = """
                  DROP TABLE IF EXISTS temp.frag_elems;
                  DROP TABLE IF EXISTS temp.frag_nodes;
                  DROP TABLE IF EXISTS temp.frag_members;
                  CREATE TEMP TABLE frag_elems (id INTEGER PRIMARY KEY, tag TEXT NOT NULL);
                  CREATE TEMP TABLE frag_nodes (tag TEXT PRIMARY KEY);
                  CREATE TEMP TABLE frag_members (tag TEXT PRIMARY KEY);
                  INSERT INTO temp.frag_elems (id, tag)
                     SELECT e.id, e.elem_tag
                     FROM fem_elements e, json_each(e.node_ids_json) j
                     JOIN fem_mesh_nodes n ON n.schema_id=@sid AND n.node_tag=CAST(j.value AS TEXT)
                     WHERE e.schema_id=@sid
                     GROUP BY e.id
                     HAVING MAX(n.z) > @lo AND MIN(n.z) < @hi;
                  CREATE INDEX temp.ix_frag_elems_tag ON frag_elems(tag);
                  INSERT OR IGNORE INTO temp.frag_nodes (tag)
                     SELECT CAST(j.value AS TEXT)
                     FROM fem_elements e JOIN temp.frag_elems f ON f.id=e.id, json_each(e.node_ids_json) j;
                  """;
               cmd.Parameters.AddWithValue("@sid", parentId);
               cmd.Parameters.AddWithValue("@lo", request.Z1 + request.ToleranceM);
               cmd.Parameters.AddWithValue("@hi", request.Z2 - request.ToleranceM);
               cmd.ExecuteNonQuery();
            }
            using (var cmd = _connection.CreateCommand())
            {
               cmd.CommandText = "INSERT OR IGNORE INTO temp.frag_members (tag) VALUES (@tag)";
               var tag = cmd.Parameters.Add("@tag", SqliteType.Text);
               foreach (var m in schemaLevel.Members)
               {
                  tag.Value = m.ElemTag;
                  cmd.ExecuteNonQuery();
               }
            }

            return new StoreyFragmentParentSnapshot
            {
               Nodes = schemaLevel.Nodes,
               Members = schemaLevel.Members,
               Regions = schemaLevel.Regions,
               MeshNodes = QueryFemMeshNodes(parentId, "node_tag IN (SELECT tag FROM temp.frag_nodes)"),
               MeshElements = QueryFemMeshElements(parentId, "id IN (SELECT id FROM temp.frag_elems)"),
               RigidBodies = schemaLevel.RigidBodies,
               LoadCases = schemaLevel.LoadCases,
               LoadDefinitions = schemaLevel.LoadDefinitions,
               MemberLoads = schemaLevel.MemberLoads,
               ElementLoads = QueryFemElementLoads(parentId, $"""
                  target_kind='{FemLoadTargetKinds.Group}'
                  OR (target_kind='{FemLoadTargetKinds.Elements}' AND EXISTS (SELECT 1 FROM json_each(target_tags_json) j
                      WHERE CAST(j.value AS TEXT) IN (SELECT tag FROM temp.frag_elems)))
                  OR (target_kind='{FemLoadTargetKinds.Members}' AND EXISTS (SELECT 1 FROM json_each(target_tags_json) j
                      WHERE CAST(j.value AS TEXT) IN (SELECT tag FROM temp.frag_members)))
                  """),
               MeshNodeLoads = QueryFemMeshNodeLoads(parentId, "mesh_node_tag IN (SELECT tag FROM temp.frag_nodes)"),
               NodeLoads = schemaLevel.NodeLoads,
               KinematicLoads = schemaLevel.KinematicLoads,
               Groups = schemaLevel.Groups,
            };
         }
         finally
         {
            using var drop = _connection.CreateCommand();
            drop.CommandText = """
               DROP TABLE IF EXISTS temp.frag_elems;
               DROP TABLE IF EXISTS temp.frag_nodes;
               DROP TABLE IF EXISTS temp.frag_members;
               """;
            drop.ExecuteNonQuery();
         }
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
