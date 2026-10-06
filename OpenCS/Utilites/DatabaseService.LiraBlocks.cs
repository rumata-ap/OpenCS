using System.Text.Json;
using CScore.Fem;
using CScore.Import;

namespace OpenCS.Utilites
{
   /// <summary>Конструктивные блоки ЛИРЫ импортированной схемы и их преобразование в конструктивные элементы.</summary>
   public partial class DatabaseService
   {
      /// <summary>Заменяет сохранённые кБ ЛИРЫ схемы (таблица 31 при API-импорте).</summary>
      public void SaveFemSchemaConstructiveBlocks(int schemaId, IReadOnlyList<LiraConstructiveBlockRecord> blocks)
      {
         using var tx = _connection.BeginTransaction();
         try
         {
            using (var delete = _connection.CreateCommand())
            {
               delete.CommandText = "DELETE FROM fem_schema_constructive_blocks WHERE schema_id=@sid";
               delete.Parameters.AddWithValue("@sid", schemaId);
               delete.ExecuteNonQuery();
            }
            using var insert = _connection.CreateCommand();
            insert.CommandText = """
               INSERT OR REPLACE INTO fem_schema_constructive_blocks
                  (schema_id, block_id, type, floor, mark, comment, element_ids_json)
               VALUES (@sid, @id, @type, @floor, @mark, @comment, @ids)
            """;
            foreach (var b in blocks)
            {
               insert.Parameters.Clear();
               insert.Parameters.AddWithValue("@sid", schemaId);
               insert.Parameters.AddWithValue("@id", b.Id);
               insert.Parameters.AddWithValue("@type", b.Type ?? "");
               insert.Parameters.AddWithValue("@floor", b.Floor ?? "");
               insert.Parameters.AddWithValue("@mark", b.Mark ?? "");
               insert.Parameters.AddWithValue("@comment", b.Comment ?? "");
               insert.Parameters.AddWithValue("@ids", JsonSerializer.Serialize(b.ElementIds));
               insert.ExecuteNonQuery();
            }
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }

      /// <summary>
      /// кБ ЛИРЫ схемы по номеру. Для схем, импортированных до сохранения таблицы кБ (БД &lt; v66), —
      /// из групп КЭ по кБ: номер, тип, этаж и марка разбираются из имени группы.
      /// </summary>
      public List<LiraBlockInfo> GetLiraBlocks(int schemaId)
      {
         var result = new List<LiraBlockInfo>();
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = """
               SELECT block_id, type, floor, mark, element_ids_json
               FROM fem_schema_constructive_blocks WHERE schema_id=@sid ORDER BY block_id
            """;
            cmd.Parameters.AddWithValue("@sid", schemaId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
               result.Add(new LiraBlockInfo(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3),
                  ElementTags(r.GetString(4))));
         }
         if (result.Count > 0) return result;

         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = """
               SELECT tag, member_tags_json FROM fem_member_groups
               WHERE schema_id=@sid AND kind='mesh' AND member_type IS NULL ORDER BY id
            """;
            cmd.Parameters.AddWithValue("@sid", schemaId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
               if (LiraBlockTags.TryParse(r.GetString(0), out var id, out var type, out var floor, out var mark)
                   && result.All(b => b.Id != id))
                  result.Add(new LiraBlockInfo(id, type, floor, mark, ElementTags(r.GetString(1))));
         }
         return result.OrderBy(b => b.Id).ToList();
      }

      static List<string> ElementTags(string json) => [.. FemMemberGroup.ParseTags(json)];

      /// <summary>
      /// Сохраняет результат дискретизации схемы с импортированной сеткой: удаляются и вставляются заново
      /// только строки сетки с <c>origin='generated'</c>; импортированные узлы и КЭ не трогаются (Id сохраняются).
      /// </summary>
      public void SaveFemMeshSnapshotKeepingImported(int schemaId, IReadOnlyList<FemMeshNode> nodes, IReadOnlyList<FemElement> elements)
      {
         using var tx = _connection.BeginTransaction();
         try
         {
            using (var delete = _connection.CreateCommand())
            {
               delete.CommandText = """
                  DELETE FROM fem_elements   WHERE schema_id=@sid AND origin<>'imported';
                  DELETE FROM fem_mesh_nodes WHERE schema_id=@sid AND origin<>'imported';
               """;
               delete.Parameters.AddWithValue("@sid", schemaId);
               delete.ExecuteNonQuery();
            }
            InsertFemMeshSnapshot(schemaId,
               nodes.Where(n => n.Origin != FemMember.MeshSourceImported).ToList(),
               elements.Where(e => e.Origin != FemMember.MeshSourceImported).ToList());
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }

      /// <summary>Есть ли у схемы импортированная сетка (дискретизация должна её сохранить).</summary>
      public bool HasImportedMesh(int schemaId)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM fem_elements WHERE schema_id=@sid AND origin='imported')";
         cmd.Parameters.AddWithValue("@sid", schemaId);
         return Convert.ToInt64(cmd.ExecuteScalar()) != 0;
      }

      /// <summary>Конструктивные элементы из кБ ЛИРЫ: прежние элементы этих кБ заменяются (см. <see cref="ApplyMeshMembers"/>).</summary>
      public void ApplyLiraBlockMembers(int schemaId, IReadOnlyList<LiraBlockInfo> blocks, IReadOnlyList<MeshMemberBuild> builds)
         => ApplyMeshMembers(schemaId, blocks.Select(b => b.Tag).ToList(), builds);

      /// <summary>
      /// Записывает конструктивные элементы, собранные из КЭ сетки, одной транзакцией. Прежние элементы с именами
      /// <paramref name="replacedTags"/> (имя или «имя · k») заменяются: удаляются вместе с регионами, КЭ сетки
      /// отвязываются, узлы их концов, которыми больше никто не пользуется и на которых нет нагрузок, удаляются.
      /// Затем добавляются новые узлы (существующий узел с тем же тегом переиспользуется), регионы и элементы,
      /// а КЭ сетки и узлы концов связываются с ними (<c>source_member_tag</c>, <c>source_node_tag</c>).
      /// Прежний элемент с распределённой нагрузкой не удаляется — бросается исключение.
      /// </summary>
      public void ApplyMeshMembers(int schemaId, IReadOnlyList<string> replacedTags, IReadOnlyList<MeshMemberBuild> builds)
      {
         var existingMembers = GetFemMembers(schemaId);
         var replaced = existingMembers.Where(m => replacedTags.Any(t => MeshMemberBuilder.IsPartTag(m.ElemTag, t))).ToList();
         var replacedIds = replaced.Select(m => m.Id).ToHashSet();
         var loaded = GetFemMemberLoads(schemaId).Where(l => replacedIds.Contains(l.MemberId)).Select(l => l.MemberId).ToHashSet();
         if (loaded.Count > 0)
            throw new InvalidOperationException("На элементах "
               + string.Join(", ", replaced.Where(m => loaded.Contains(m.Id)).Select(m => $"«{m.ElemTag}»"))
               + " есть распределённые нагрузки — снимите их перед повторной сборкой элементов.");

         var remaining = existingMembers.Where(m => !replacedIds.Contains(m.Id)).ToList();
         var usedNodeTags = remaining.SelectMany(m => ReadIntTags(m.NodeIdsJson)).ToHashSet(StringComparer.Ordinal);
         foreach (var build in builds)
            foreach (var part in build.Parts)
               foreach (var t in ReadIntTags(part.Member.NodeIdsJson)) usedNodeTags.Add(t);
         var existingNodes = GetFemNodes(schemaId);

         using var tx = _connection.BeginTransaction();
         try
         {
            // --- снять прежние элементы этих кБ
            foreach (var m in replaced)
            {
               Exec("UPDATE fem_elements SET source_member_tag=NULL WHERE schema_id=@sid AND source_member_tag=@tag",
                  ("@sid", schemaId), ("@tag", m.ElemTag));
               Exec("DELETE FROM fem_members WHERE id=@id", ("@id", m.Id));
               if (m.PlanarRegionId is int regionId)
                  Exec("""
                     DELETE FROM planar_connection_mappings WHERE connection_id IN (SELECT id FROM planar_connections WHERE region_a_id=@id OR region_b_id=@id);
                     DELETE FROM planar_connections WHERE region_a_id=@id OR region_b_id=@id;
                     DELETE FROM planar_regions WHERE id=@id;
                  """, ("@id", regionId));
            }
            var loadedNodeIds = new HashSet<int>();
            using (var cmd = _connection.CreateCommand())
            {
               cmd.CommandText = """
                  SELECT node_id FROM fem_node_loads WHERE schema_id=@sid
                  UNION SELECT node_id FROM fem_kinematic_loads WHERE schema_id=@sid
               """;
               cmd.Parameters.AddWithValue("@sid", schemaId);
               using var r = cmd.ExecuteReader();
               while (r.Read()) loadedNodeIds.Add(r.GetInt32(0));
            }
            foreach (var tag in replaced.SelectMany(m => ReadIntTags(m.NodeIdsJson)).Distinct())
               if (!usedNodeTags.Contains(tag))
                  foreach (var node in existingNodes.Where(n => n.NodeTag == tag && !loadedNodeIds.Contains(n.Id)))
                  {
                     Exec("DELETE FROM fem_nodes WHERE id=@id", ("@id", node.Id));
                     Exec("UPDATE fem_mesh_nodes SET source_node_tag=NULL WHERE schema_id=@sid AND node_tag=@tag",
                        ("@sid", schemaId), ("@tag", tag));
                  }

            // --- новые узлы, регионы, элементы и связи с сеткой
            InsertMembersOverMesh(schemaId, builds.SelectMany(b => b.Nodes),
               builds.SelectMany(b => b.Parts).Select(p => new CScore.Fem.Import.FemImportMember(p.Member, p.Region, p.ElementTags)));
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }

      /// <summary>
      /// Вставляет конструктивные элементы поверх уже сохранённой сетки (вызывается внутри транзакции):
      /// узлы концов (существующие теги не дублируются) с привязкой узлов сетки, плоские регионы,
      /// элементы и <c>source_member_tag</c> их КЭ сетки. Общий путь импорта и преобразования кБ ЛИРЫ.
      /// </summary>
      void InsertMembersOverMesh(int schemaId, IEnumerable<FemNode> nodes, IEnumerable<CScore.Fem.Import.FemImportMember> members)
      {
         var nodeTags = GetFemNodes(schemaId).Select(n => n.NodeTag).ToHashSet(StringComparer.Ordinal);
         foreach (var node in nodes)
         {
            if (nodeTags.Add(node.NodeTag))
               Exec("INSERT INTO fem_nodes (schema_id, node_tag, x, y, z, dof_mask) VALUES (@sid, @tag, @x, @y, @z, @dm)",
                  ("@sid", schemaId), ("@tag", node.NodeTag), ("@x", node.X), ("@y", node.Y), ("@z", node.Z), ("@dm", node.DofMask));
            Exec("UPDATE fem_mesh_nodes SET source_node_tag=@tag WHERE schema_id=@sid AND node_tag=@tag",
               ("@sid", schemaId), ("@tag", node.NodeTag));
         }
         using var link = _connection.CreateCommand();
         link.CommandText = "UPDATE fem_elements SET source_member_tag=@tag WHERE schema_id=@sid AND elem_tag=@elem";
         link.Parameters.AddWithValue("@sid", schemaId);
         var tagParam = link.Parameters.Add("@tag", Microsoft.Data.Sqlite.SqliteType.Text);
         var elem = link.Parameters.Add("@elem", Microsoft.Data.Sqlite.SqliteType.Text);
         foreach (var (member, region, elementTags) in members)
         {
            member.Id = 0;
            member.SchemaId = schemaId;
            if (region is { } r)
               member.PlanarRegionId = AddPlanarRegion(r, schemaId);
            SaveFemMember(member);
            tagParam.Value = member.ElemTag;
            foreach (var t in elementTags)
            {
               elem.Value = t;
               link.ExecuteNonQuery();
            }
         }
      }

      /// <summary>
      /// Сохраняет импорт внешней программы в новую (пустую) схему одной транзакцией: сетка → узлы и
      /// конструктивные элементы с привязкой КЭ → группы. Результат предварительно проверяется
      /// (<see cref="CScore.Fem.Import.FemImportResult.Validate"/>); при ошибках и непустой схеме — исключение.
      /// Возвращает предупреждения проверки — их показывает вызывающий.
      /// </summary>
      public IReadOnlyList<FemValidationDiagnostic> SaveFemImport(int schemaId, CScore.Fem.Import.FemImportResult result)
      {
         var diagnostics = result.Validate();
         var errors = diagnostics.Where(d => d.IsError).ToList();
         if (errors.Count > 0)
            throw new InvalidOperationException("Импорт схемы не согласован:" + Environment.NewLine
               + string.Join(Environment.NewLine, errors.Take(20).Select(e => e.Message))
               + (errors.Count > 20 ? $"{Environment.NewLine}… и ещё {errors.Count - 20}" : ""));

         using var tx = _connection.BeginTransaction();
         try
         {
            using (var check = _connection.CreateCommand())
            {
               check.CommandText = """
                  SELECT EXISTS (SELECT 1 FROM fem_nodes WHERE schema_id=@sid)
                      OR EXISTS (SELECT 1 FROM fem_members WHERE schema_id=@sid)
                      OR EXISTS (SELECT 1 FROM fem_elements WHERE schema_id=@sid)
                      OR EXISTS (SELECT 1 FROM fem_member_groups WHERE schema_id=@sid)
               """;
               check.Parameters.AddWithValue("@sid", schemaId);
               if (Convert.ToInt64(check.ExecuteScalar()) != 0)
                  throw new InvalidOperationException("Импорт сохраняется только в новую схему, а у этой уже есть данные.");
            }

            foreach (var n in result.MeshNodes) n.SchemaId = schemaId;
            foreach (var e in result.MeshElements) e.SchemaId = schemaId;
            InsertFemMeshSnapshot(schemaId, result.MeshNodes, result.MeshElements);
            InsertMembersOverMesh(schemaId, result.MemberNodes, result.Members);
            foreach (var g in result.Groups)
            {
               g.Id = 0;
               SaveFemMemberGroupCore(g, schemaId);
            }
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
         return diagnostics.Where(d => !d.IsError).ToList();
      }

      void Exec(string sql, params (string Name, object Value)[] parameters)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = sql;
         foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
         cmd.ExecuteNonQuery();
      }

      static IEnumerable<string> ReadIntTags(string json)
      {
         try
         {
            return (JsonSerializer.Deserialize<int[]>(json) ?? [])
               .Select(t => t.ToString(System.Globalization.CultureInfo.InvariantCulture));
         }
         catch (JsonException) { return []; }
      }
   }
}
