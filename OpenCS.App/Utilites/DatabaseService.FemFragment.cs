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
             source_member_tag TEXT,
             reinforcement_type_ids TEXT,
             thickness_m REAL
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

      /// <summary>Миграция v93: ТЗА и толщина КЭ опорной сетки фрагмента, программа-источник родителя (срез 4).</summary>
      void MigrateV93()
      {
         MigrateV92();
         if (!ColumnExists("fem_fragment_ref_elements", "reinforcement_type_ids"))
            MigExec("ALTER TABLE fem_fragment_ref_elements ADD COLUMN reinforcement_type_ids TEXT");
         if (!ColumnExists("fem_fragment_ref_elements", "thickness_m"))
            MigExec("ALTER TABLE fem_fragment_ref_elements ADD COLUMN thickness_m REAL");
         if (!ColumnExists("fem_schemas", "parent_source_type"))
            MigExec("ALTER TABLE fem_schemas ADD COLUMN parent_source_type TEXT");
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

      /// <summary>Заменяет рассечения, опорную сетку и нагрузки-поля фрагмента (одной транзакцией); маски и ГУ
      /// прежних разрезов удаляются.</summary>
      public void ReplaceStoreyFragmentData(int schemaId, IEnumerable<FragmentCut> cuts, IEnumerable<FragmentRefElement> refElements,
         IEnumerable<FragmentFieldLoad> fieldLoads)
      {
         using var tx = _connection.BeginTransaction();
         try
         {
            using (var del = _connection.CreateCommand())
            {
               del.CommandText = """
                  DELETE FROM fem_fragment_mask_items   WHERE mask_id IN (SELECT id FROM fem_fragment_masks WHERE schema_id=@sid);
                  DELETE FROM fem_fragment_masks        WHERE schema_id=@sid;
                  DELETE FROM fem_fragment_cut_bc       WHERE cut_id IN (SELECT id FROM fem_fragment_cuts WHERE schema_id=@sid);
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
      /// <paramref name="mapLoadCase"/> переводит Id загружений нагрузок-полей. Возвращает Id вставленных разрезов по порядку.</summary>
      List<int> InsertStoreyFragmentDataCore(int schemaId, IEnumerable<FragmentCut> cuts, IEnumerable<FragmentRefElement> refElements,
         IEnumerable<FragmentFieldLoad> fieldLoads, Func<int, int> mapLoadCase)
      {
         var cutIds = new List<int>();
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = """
               INSERT INTO fem_fragment_cuts (schema_id, member_tag, kind, side, z, node_tag, segments_json)
               VALUES (@sid, @tag, @kind, @side, @z, @node, @seg);
               SELECT last_insert_rowid();
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
               cutIds.Add((int)(long)cmd.ExecuteScalar()!);
            }
         }
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = """
               INSERT INTO fem_fragment_ref_elements (schema_id, tag, elem_type, points_blob, stiffness_num, foundation_c1, source_member_tag,
                                                      reinforcement_type_ids, thickness_m)
               VALUES (@sid, @tag, @type, @pts, @snum, @c1, @smt, @rti, @h)
               """;
            cmd.Parameters.AddWithValue("@sid", schemaId);
            var tag = cmd.Parameters.Add("@tag", SqliteType.Text);
            var type = cmd.Parameters.Add("@type", SqliteType.Text);
            var pts = cmd.Parameters.Add("@pts", SqliteType.Blob);
            var snum = cmd.Parameters.Add("@snum", SqliteType.Integer);
            var c1 = cmd.Parameters.Add("@c1", SqliteType.Real);
            var smt = cmd.Parameters.Add("@smt", SqliteType.Text);
            var rti = cmd.Parameters.Add("@rti", SqliteType.Text);
            var h = cmd.Parameters.Add("@h", SqliteType.Real);
            foreach (var e in refElements)
            {
               rti.Value = (object?)e.ReinforcementTypeIds ?? DBNull.Value;
               h.Value = (object?)e.ThicknessM ?? DBNull.Value;
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
         return cutIds;
      }

      public List<FragmentCut> GetFragmentCuts(int schemaId) => GetFragmentCutsWithIds(schemaId).Select(c => c.Cut).ToList();

      /// <summary>Рассечения фрагмента с Id строк (ссылки масок и ГУ разрезов), по порядку записи.</summary>
      public List<(int Id, FragmentCut Cut)> GetFragmentCutsWithIds(int schemaId)
      {
         var result = new List<(int, FragmentCut)>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT member_tag, kind, side, z, node_tag, segments_json, id FROM fem_fragment_cuts WHERE schema_id=@sid ORDER BY id";
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var r = cmd.ExecuteReader();
         while (r.Read())
         {
            var segments = (JsonSerializer.Deserialize<double[][]>(r.GetString(5)) ?? [])
               .Select(a => (new PlanarVector3(a[0], a[1], a[2]), new PlanarVector3(a[3], a[4], a[5]))).ToList();
            result.Add((r.GetInt32(6), new FragmentCut(r.GetString(0), r.GetString(1) == "end" ? FragmentCutKind.End : FragmentCutKind.Edge,
               r.GetString(2) == "bottom" ? FragmentCutSide.Bottom : FragmentCutSide.Top, r.GetDouble(3),
               r.IsDBNull(4) ? null : r.GetString(4), segments)));
         }
         return result;
      }

      public List<FragmentRefElement> GetFragmentRefElements(int schemaId)
      {
         var result = new List<FragmentRefElement>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT tag, elem_type, points_blob, stiffness_num, foundation_c1, source_member_tag, reinforcement_type_ids, thickness_m
            FROM fem_fragment_ref_elements WHERE schema_id=@sid ORDER BY id
            """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var r = cmd.ExecuteReader();
         while (r.Read())
            result.Add(new FragmentRefElement(r.GetString(0), r.GetString(1), FragmentStoreCodec.DecodePoints((byte[])r[2]),
               r.IsDBNull(3) ? null : r.GetInt32(3), r.IsDBNull(4) ? null : r.GetDouble(4), r.IsDBNull(5) ? null : r.GetString(5),
               r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetDouble(7)));
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

      /// <summary>Схема — фрагмент этажа и программа-источник её родителя (null — собран до v93 или не фрагмент).</summary>
      public (bool IsFragment, string? ParentSourceType) GetFemSchemaFragmentSource(int schemaId)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT parent_schema_id, parent_source_type FROM fem_schemas WHERE id=@sid";
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var r = cmd.ExecuteReader();
         return r.Read() ? (!r.IsDBNull(0), r.IsDBNull(1) ? null : r.GetString(1)) : (false, null);
      }

      /// <summary>Есть ли у схемы нагрузки-поля фрагмента (без чтения значений).</summary>
      public bool HasFragmentFieldLoads(int schemaId)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM fem_fragment_field_loads WHERE schema_id=@sid)";
         cmd.Parameters.AddWithValue("@sid", schemaId);
         return (long)cmd.ExecuteScalar()! != 0;
      }

      /// <summary>Есть ли в опорной сетке фрагмента КЭ родителя с c1 &gt; 0 (без чтения опорной сетки).</summary>
      public bool HasFragmentRefFoundationC1(int schemaId)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM fem_fragment_ref_elements WHERE schema_id=@sid AND foundation_c1 > 0)";
         cmd.Parameters.AddWithValue("@sid", schemaId);
         return (long)cmd.ExecuteScalar()! != 0;
      }

      /// <summary>Диапазон z узлов сетки схемы (подсказка отметок фрагмента); нет сетки — null.</summary>
      public (double Min, double Max)? GetFemMeshZRange(int schemaId)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT MIN(z), MAX(z) FROM fem_mesh_nodes WHERE schema_id=@sid";
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var r = cmd.ExecuteReader();
         return r.Read() && !r.IsDBNull(0) ? (r.GetDouble(0), r.GetDouble(1)) : null;
      }

      /// <summary>
      /// Координаты узлов сетки по тегам узлов и узлов КЭ по тегам КЭ (отметка перекрытия по выделению в 3D) — отбор в SQL,
      /// без чтения всей сетки.
      /// </summary>
      public List<PlanarVector3> GetFemPickPoints(int schemaId, IReadOnlyCollection<string> meshElemTags,
         IReadOnlyCollection<string> meshNodeTags)
      {
         var result = new List<PlanarVector3>();
         if (meshElemTags.Count == 0 && meshNodeTags.Count == 0) return result;
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT n.x, n.y, n.z FROM fem_mesh_nodes n
            WHERE n.schema_id=@sid AND (
               n.node_tag IN (SELECT value FROM json_each(@nodes))
               OR n.node_tag IN (SELECT CAST(j.value AS TEXT)
                                 FROM fem_elements e, json_each(e.node_ids_json) j
                                 WHERE e.schema_id=@sid AND e.elem_tag IN (SELECT value FROM json_each(@elems))))
            """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         cmd.Parameters.AddWithValue("@nodes", JsonSerializer.Serialize(meshNodeTags));
         cmd.Parameters.AddWithValue("@elems", JsonSerializer.Serialize(meshElemTags));
         using var r = cmd.ExecuteReader();
         while (r.Read()) result.Add(new PlanarVector3(r.GetDouble(0), r.GetDouble(1), r.GetDouble(2)));
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
      /// нагрузки на группы, пружины и закрепления этих узлов (пружины узлов схемы — все) — фильтром в SQL через
      /// временные таблицы. Порядок соединения в отборе КЭ задан CROSS JOIN: КЭ → его узлы → узел сетки по тегу; без
      /// него планировщик брал индекс узлов по (schema_id, z) и перебирал все узлы схемы на каждый КЭ (импорт на 120 тыс.
      /// КЭ — десятки минут вместо секунды).
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
                     FROM fem_elements e CROSS JOIN json_each(e.node_ids_json) j CROSS JOIN fem_mesh_nodes n
                     WHERE e.schema_id=@sid AND n.schema_id=@sid AND n.node_tag=CAST(j.value AS TEXT)
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
               Springs = QueryFemSprings(parentId, $"""
                  target_kind='{FemSpringTargetKinds.Node}'
                  OR (target_kind='{FemSpringTargetKinds.MeshNode}' AND node_tag IN (SELECT tag FROM temp.frag_nodes))
                  """),
               MeshNodeSupports = QueryFemMeshNodeSupports(parentId, "node_tag IN (SELECT tag FROM temp.frag_nodes)"),
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

      /// <summary>
      /// Пишет черновик фрагмента новой схемой одной транзакцией (план среза 3, решение 5): строка схемы с полями
      /// фрагмента, узлы, области, КонЭ, загружения (Id родителя → новые), определения, нагрузки, рассечения, опорная
      /// сетка, нагрузки-поля, копии источников армирования. Любая ошибка — откат, схемы нет. Сетку не строит.
      /// </summary>
      public FemSchema CreateStoreyFragmentSchema(int parentId, StoreyFragmentRequest request, StoreyFragmentDraft draft, string fingerprint)
      {
         ArgumentNullException.ThrowIfNull(draft);
         if (draft.HasErrors) throw new InvalidOperationException("Черновик фрагмента содержит отказы — схема не создаётся.");
         var (barStep, plateStep) = GetFemSchemaMeshSteps(parentId);
         var (_, angle) = GetFemSchemaRigidJunctions(parentId);
         var schema = new FemSchema
         {
            Tag = request.Name, SourceType = "internal", Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            MeshBarStepM = barStep, MeshPlateStepM = plateStep, AutoRigidJunctions = true, RigidJunctionAngleDeg = angle,
            ParentSchemaId = parentId, FragmentZ1 = request.Z1, FragmentZ2 = request.Z2, ParentFingerprint = fingerprint,
            FragmentBuildReportJson = StoreyFragmentBuildReport.ToJson(draft.Diagnostics),
            ParentSourceType = GetFemSchemaSourceType(parentId),
         };

         using var tx = _connection.BeginTransaction();
         try
         {
            using (var cmd = _connection.CreateCommand())
            {
               cmd.CommandText = """
                  INSERT INTO fem_schemas (tag, source_type, created, source_path, mesh_bar_step_m, mesh_plate_step_m,
                                           auto_rigid_junctions, rigid_junction_angle_deg,
                                           parent_schema_id, fragment_z1, fragment_z2, parent_fingerprint, fragment_build_report_json,
                                           parent_source_type)
                  VALUES (@tag, @src, @created, NULL, @bar, @plate, @auto, @angle, @parent, @z1, @z2, @fp, @report, @pst);
                  SELECT last_insert_rowid();
                  """;
               cmd.Parameters.AddWithValue("@tag", schema.Tag);
               cmd.Parameters.AddWithValue("@src", schema.SourceType);
               cmd.Parameters.AddWithValue("@created", schema.Created);
               cmd.Parameters.AddWithValue("@bar", (object?)schema.MeshBarStepM ?? DBNull.Value);
               cmd.Parameters.AddWithValue("@plate", (object?)schema.MeshPlateStepM ?? DBNull.Value);
               cmd.Parameters.AddWithValue("@auto", schema.AutoRigidJunctions ? 1 : 0);
               cmd.Parameters.AddWithValue("@angle", schema.RigidJunctionAngleDeg);
               AddFemSchemaFragmentParameters(cmd, schema);
               schema.Id = (int)(long)cmd.ExecuteScalar()!;
            }
            int sid = schema.Id;

            var nodeId = new Dictionary<string, int>(StringComparer.Ordinal);
            using (var cmd = _connection.CreateCommand())
            {
               cmd.CommandText = """
                  INSERT INTO fem_nodes (schema_id, node_tag, x, y, z, dof_mask) VALUES (@sid, @tag, @x, @y, @z, @dm);
                  SELECT last_insert_rowid();
                  """;
               cmd.Parameters.AddWithValue("@sid", sid);
               var tag = cmd.Parameters.Add("@tag", SqliteType.Text);
               var x = cmd.Parameters.Add("@x", SqliteType.Real);
               var y = cmd.Parameters.Add("@y", SqliteType.Real);
               var z = cmd.Parameters.Add("@z", SqliteType.Real);
               var dm = cmd.Parameters.Add("@dm", SqliteType.Integer);
               foreach (var n in draft.Nodes)
               {
                  if (nodeId.ContainsKey(n.NodeTag))
                     throw new InvalidOperationException($"Узел фрагмента {n.NodeTag} повторяется.");
                  tag.Value = n.NodeTag; x.Value = n.X; y.Value = n.Y; z.Value = n.Z; dm.Value = n.DofMask;
                  nodeId[n.NodeTag] = (int)(long)cmd.ExecuteScalar()!;
               }
            }

            var memberId = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var part in draft.Members)
            {
               var m = part.Member;
               if (memberId.ContainsKey(m.ElemTag))
                  throw new InvalidOperationException($"КонЭ фрагмента «{m.ElemTag}» повторяется.");
               if (part.Region is { } region) m.PlanarRegionId = AddPlanarRegion(region, sid);
               m.Id = 0;
               m.SchemaId = sid;
               SaveFemMember(m);
               memberId[m.ElemTag] = m.Id;
            }

            var lcMap = new Dictionary<int, int>();
            using (var cmd = _connection.CreateCommand())
            {
               cmd.CommandText = """
                  INSERT INTO fem_load_cases
                     (schema_id, tag, load_type, sp20_type, sp20_group, gamma_f_unfav, gamma_f_fav, psi1, psi2,
                      self_weight_factor, origin, source_load_num, long_term_share, parent_load_case_id)
                  VALUES (@sid, @tag, @lt, @st, @sg, @gu, @gf, @p1, @p2, @swf, @origin, @sln, @lts, @plc);
                  SELECT last_insert_rowid();
                  """;
               foreach (var lc in draft.LoadCases)
               {
                  cmd.Parameters.Clear();
                  cmd.Parameters.AddWithValue("@sid", sid);
                  cmd.Parameters.AddWithValue("@tag", lc.Tag);
                  cmd.Parameters.AddWithValue("@lt", (object?)lc.LoadType ?? DBNull.Value);
                  cmd.Parameters.AddWithValue("@st", lc.Sp20Type);
                  cmd.Parameters.AddWithValue("@sg", (object?)lc.Sp20Group ?? DBNull.Value);
                  cmd.Parameters.AddWithValue("@gu", (object?)lc.GammaFUnfav ?? DBNull.Value);
                  cmd.Parameters.AddWithValue("@gf", (object?)lc.GammaFFav ?? DBNull.Value);
                  cmd.Parameters.AddWithValue("@p1", (object?)lc.Psi1 ?? DBNull.Value);
                  cmd.Parameters.AddWithValue("@p2", (object?)lc.Psi2 ?? DBNull.Value);
                  // Копия ссылается на загружение родителя (lc.Id черновика — Id родителя): карта для масок.
                  lc.ParentLoadCaseId = lc.Id;
                  AddFemLoadCaseExtraParameters(cmd, lc);
                  int id = (int)(long)cmd.ExecuteScalar()!;
                  lcMap[lc.Id] = id;
                  schema.LoadCases.Add(new FemLoadCase
                  {
                     Id = id, SchemaId = sid, Tag = lc.Tag, LoadType = lc.LoadType, Sp20Type = lc.Sp20Type,
                     Sp20Group = lc.Sp20Group, GammaFUnfav = lc.GammaFUnfav, GammaFFav = lc.GammaFFav, Psi1 = lc.Psi1,
                     Psi2 = lc.Psi2, LongTermShare = lc.LongTermShare, SelfWeightFactor = lc.SelfWeightFactor,
                     Origin = lc.Origin, SourceLoadNum = lc.SourceLoadNum, ParentLoadCaseId = lc.Id,
                  });
               }
            }
            int Lc(int parentLc) => lcMap.TryGetValue(parentLc, out int id) ? id
               : throw new InvalidOperationException($"Нагрузка фрагмента ссылается на загружение {parentLc}, которого нет в копиях.");
            int Node(string tag) => nodeId.TryGetValue(tag, out int id) ? id
               : throw new InvalidOperationException($"Нагрузка фрагмента ссылается на узел {tag}, которого нет во фрагменте.");

            foreach (var d in draft.LoadDefinitions)
            {
               var copy = FragmentLoadCopy.RemapLoadCases(d, lcMap);
               copy.Id = 0;
               copy.SchemaId = sid;
               SaveFemLoadDefinition(copy);
               schema.LoadDefinitions.Add(copy);
            }

            using (var cmd = _connection.CreateCommand())
            {
               cmd.CommandText = """
                  INSERT INTO fem_member_loads
                     (schema_id, load_case_id, member_id, coordinate_system, distribution_type,
                      start_offset_m, end_offset_m, qx_start, qy_start, qz_start, qx_end, qy_end, qz_end, mx, my, mz)
                  VALUES (@sid, @lc, @mid, @cs, @dt, @so, @eo, @qxs, @qys, @qzs, @qxe, @qye, @qze, @mx, @my, @mz)
                  """;
               foreach (var (tag, l) in draft.MemberLoads.Select(x => (x.MemberTag, x.Load)))
               {
                  if (!memberId.TryGetValue(tag, out int mid))
                     throw new InvalidOperationException($"Нагрузка фрагмента ссылается на КонЭ «{tag}», которого нет во фрагменте.");
                  cmd.Parameters.Clear();
                  cmd.Parameters.AddWithValue("@sid", sid);
                  cmd.Parameters.AddWithValue("@lc", Lc(l.LoadCaseId));
                  cmd.Parameters.AddWithValue("@mid", mid);
                  cmd.Parameters.AddWithValue("@cs", l.CoordinateSystem);
                  cmd.Parameters.AddWithValue("@dt", l.DistributionType);
                  cmd.Parameters.AddWithValue("@so", l.StartOffsetM);
                  cmd.Parameters.AddWithValue("@eo", l.EndOffsetM);
                  cmd.Parameters.AddWithValue("@qxs", l.QxStart);
                  cmd.Parameters.AddWithValue("@qys", l.QyStart);
                  cmd.Parameters.AddWithValue("@qzs", l.QzStart);
                  cmd.Parameters.AddWithValue("@qxe", l.QxEnd);
                  cmd.Parameters.AddWithValue("@qye", l.QyEnd);
                  cmd.Parameters.AddWithValue("@qze", l.QzEnd);
                  cmd.Parameters.AddWithValue("@mx", l.Mx);
                  cmd.Parameters.AddWithValue("@my", l.My);
                  cmd.Parameters.AddWithValue("@mz", l.Mz);
                  cmd.ExecuteNonQuery();
               }
            }

            using (var cmd = _connection.CreateCommand())
            {
               cmd.CommandText = """
                  INSERT INTO fem_node_loads (schema_id, load_case_id, node_id, fx, fy, fz, mx, my, mz)
                  VALUES (@sid, @lc, @nid, @fx, @fy, @fz, @mx, @my, @mz)
                  """;
               foreach (var (tag, l) in draft.NodeLoads.Select(x => (x.NodeTag, x.Load)))
               {
                  cmd.Parameters.Clear();
                  cmd.Parameters.AddWithValue("@sid", sid);
                  cmd.Parameters.AddWithValue("@lc", Lc(l.LoadCaseId));
                  cmd.Parameters.AddWithValue("@nid", Node(tag));
                  cmd.Parameters.AddWithValue("@fx", l.Fx);
                  cmd.Parameters.AddWithValue("@fy", l.Fy);
                  cmd.Parameters.AddWithValue("@fz", l.Fz);
                  cmd.Parameters.AddWithValue("@mx", l.Mx);
                  cmd.Parameters.AddWithValue("@my", l.My);
                  cmd.Parameters.AddWithValue("@mz", l.Mz);
                  cmd.ExecuteNonQuery();
               }
            }

            using (var cmd = _connection.CreateCommand())
            {
               cmd.CommandText = """
                  INSERT INTO fem_kinematic_loads (schema_id, load_case_id, node_id, dof, value)
                  VALUES (@sid, @lc, @nid, @dof, @value)
                  """;
               foreach (var (tag, l) in draft.KinematicLoads.Select(x => (x.NodeTag, x.Load)))
               {
                  cmd.Parameters.Clear();
                  cmd.Parameters.AddWithValue("@sid", sid);
                  cmd.Parameters.AddWithValue("@lc", Lc(l.LoadCaseId));
                  cmd.Parameters.AddWithValue("@nid", Node(tag));
                  cmd.Parameters.AddWithValue("@dof", l.Dof);
                  cmd.Parameters.AddWithValue("@value", l.Value);
                  cmd.ExecuteNonQuery();
               }
            }

            foreach (var s in draft.Springs) Node(s.NodeTag);
            InsertFemBoundaryCore(sid, [], draft.Springs, [], updateObjects: false);
            foreach (var b in draft.NodeRigidBodies) { Node(b.MasterNodeTag); foreach (var t in b.SlaveNodeTags) Node(t); }
            InsertRigidBodiesCore("fem_node_rigid_bodies", sid, draft.NodeRigidBodies, updateObjects: false);
            InsertFemMeshLevelLoadsCore(sid, draft.ElementLoads, [], Lc, _ => null, updateObjects: false);
            InsertStoreyFragmentDataCore(sid, draft.Cuts, draft.RefElements, draft.FieldLoads, Lc);
            CopyFragmentRebarSourcesCore(parentId, sid);
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
         FemSchemas.Add(schema);
         return schema;
      }

      /// <summary>Копия данных фрагмента в схему-копию (внутри транзакции <see cref="DuplicateFemSchema"/>): рассечения,
      /// опорная сетка, нагрузки-поля (Id загружений — через <paramref name="mapLoadCase"/>), источники армирования; у
      /// фрагмента — и жёсткости родителя (часть того же снимка).</summary>
      void CopyStoreyFragmentDataCore(int fromSchemaId, int toSchemaId, Func<int, int> mapLoadCase, bool isFragment)
      {
         var cuts = GetFragmentCutsWithIds(fromSchemaId);
         var newCutIds = InsertStoreyFragmentDataCore(toSchemaId, cuts.Select(c => c.Cut), GetFragmentRefElements(fromSchemaId),
            GetFragmentFieldLoads(fromSchemaId), mapLoadCase);
         var cutMap = new Dictionary<int, int>();
         for (int i = 0; i < cuts.Count; i++) cutMap[cuts[i].Id] = newCutIds[i];
         CopyFragmentMasksCore(fromSchemaId, toSchemaId, cutMap, mapLoadCase);
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            INSERT INTO fem_fragment_rebar_sources (schema_id, kind, file_name, data)
               SELECT @to, kind, file_name, data FROM fem_fragment_rebar_sources WHERE schema_id=@from;
            """;
         if (isFragment)
            cmd.CommandText += """
               DELETE FROM fem_schema_stiffnesses WHERE schema_id=@to;
               INSERT INTO fem_schema_stiffnesses (schema_id, num, kind_code, name, params, section_unit_m)
                  SELECT @to, num, kind_code, name, params, section_unit_m FROM fem_schema_stiffnesses WHERE schema_id=@from;
               """;
         cmd.Parameters.AddWithValue("@to", toSchemaId);
         cmd.Parameters.AddWithValue("@from", fromSchemaId);
         cmd.ExecuteNonQuery();
      }

      /// <summary>Параметры @parent, @z1, @z2, @fp, @report, @pst, @bcp — поля фрагмента строки <c>fem_schemas</c>.</summary>
      static void AddFemSchemaFragmentParameters(SqliteCommand cmd, FemSchema schema)
      {
         cmd.Parameters.AddWithValue("@bcp", (object?)schema.FragmentBcPreset ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@pst", (object?)schema.ParentSourceType ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@parent", (object?)schema.ParentSchemaId ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@z1", (object?)schema.FragmentZ1 ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@z2", (object?)schema.FragmentZ2 ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@fp", (object?)schema.ParentFingerprint ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@report", (object?)schema.FragmentBuildReportJson ?? DBNull.Value);
      }
   }
}
