using CScore.Fem;
using CScore.Fem.Fragment;
using Microsoft.Data.Sqlite;

namespace OpenCS.Utilites
{
   /// <summary>Итог переноса при пересборке фрагмента: сколько перенесено и что не перенесено (строки отчёта).</summary>
   public sealed record FragmentRebuildTransferReport(
      int Analyses, int Checks, int CutBc, int Imperfections, IReadOnlyList<FemValidationDiagnostic> Lost);

   public partial class DatabaseService
   {
      /// <summary>
      /// Пересборка фрагмента на месте (тот же Id и имя, хвост вехи «Аудит GUI + CLI»): одной транзакцией прежний состав
      /// (узлы, КонЭ, области, группы, загружения, комбинации, нагрузки, разрезы, опорная сетка, сетка, маски, результаты)
      /// заменяется черновиком <paramref name="draft"/> из родителя; остаются и переносятся постановки (Id загружений —
      /// через загружение родителя, узлы пути — по тегу; результат сброшен), проверки (группа и КонЭ — по имени),
      /// несовершенства (загружение — через родителя), ГУ разрезов (пресеты схемы и правки — по ключу разреза), поля,
      /// пресеты ГУ и шаги сетки. Чего нет в новом составе — строка <see cref="FragmentRebuildTransferReport.Lost"/>.
      /// Ошибка — откат, фрагмент прежний.
      /// </summary>
      public FragmentRebuildTransferReport RebuildStoreyFragmentSchema(FemSchema schema, StoreyFragmentRequest request,
         StoreyFragmentDraft draft, string fingerprint)
      {
         ArgumentNullException.ThrowIfNull(schema);
         ArgumentNullException.ThrowIfNull(request);
         ArgumentNullException.ThrowIfNull(draft);
         if (schema.ParentSchemaId is not int parentId) throw new InvalidOperationException($"Схема «{schema.Tag}» не фрагмент.");
         if (draft.HasErrors) throw new InvalidOperationException("Черновик фрагмента содержит отказы — фрагмент не пересобирается.");
         int sid = schema.Id;
         var lost = new List<FemValidationDiagnostic>();
         List<int> resultIds;
         int analyses = 0, checks = 0, cutBc = 0, imperfections = 0;

         using var tx = _connection.BeginTransaction();
         try
         {
            // ── Прежний состав: по чему сопоставлять после замены ─────────────────────────────────────────────
            var oldLoadCases = ReadRows("SELECT id, parent_load_case_id, tag FROM fem_load_cases WHERE schema_id=@sid", sid,
               r => (Id: r.GetInt32(0), Parent: r.IsDBNull(1) ? (int?)null : r.GetInt32(1), Tag: r.GetString(2)));
            var oldNodeTag = ReadRows("SELECT id, node_tag FROM fem_nodes WHERE schema_id=@sid", sid,
               r => (Id: r.GetInt32(0), Tag: r.GetString(1))).ToDictionary(x => x.Id, x => x.Tag);
            var oldMemberTag = ReadRows("SELECT id, elem_tag FROM fem_members WHERE schema_id=@sid", sid,
               r => (Id: r.GetInt32(0), Tag: r.GetString(1))).ToDictionary(x => x.Id, x => x.Tag);
            var oldGroupTag = ReadRows("SELECT id, tag FROM fem_member_groups WHERE schema_id=@sid", sid,
               r => (Id: r.GetInt32(0), Tag: r.GetString(1))).ToDictionary(x => x.Id, x => x.Tag);
            var oldBytes = GetFragmentCutBc(sid);
            var oldCutBc = GetFragmentCutsWithIds(sid).Where(c => oldBytes.ContainsKey(c.Id))
               .Select(c => (c.Cut, oldBytes[c.Id])).ToList();

            // ── Результаты — сброс; затем прежний состав ────────────────────────────────────────────────────────
            resultIds = CollectFemSchemaResultIds(sid);
            Exec("""
               UPDATE fem_analyses SET result_id=NULL, status='created' WHERE schema_id=@sid;
               UPDATE fem_checks SET result_id=NULL WHERE schema_id=@sid;
               """, sid);
            DeleteCalcResultsByIds(resultIds);
            Exec("""
               DELETE FROM fem_load_definitions   WHERE schema_id=@sid;
               DELETE FROM fem_node_loads         WHERE schema_id=@sid;
               DELETE FROM fem_member_loads       WHERE schema_id=@sid;
               DELETE FROM fem_kinematic_loads    WHERE schema_id=@sid;
               DELETE FROM fem_element_loads      WHERE schema_id=@sid;
               DELETE FROM fem_mesh_node_loads    WHERE schema_id=@sid;
               DELETE FROM fem_mesh_node_supports WHERE schema_id=@sid;
               DELETE FROM fem_springs            WHERE schema_id=@sid;
               DELETE FROM fem_line_supports      WHERE schema_id=@sid;
               DELETE FROM fem_rigid_bodies       WHERE schema_id=@sid;
               DELETE FROM fem_node_rigid_bodies  WHERE schema_id=@sid;
               DELETE FROM fem_load_cases         WHERE schema_id=@sid;
               DELETE FROM fem_member_groups      WHERE schema_id=@sid;
               DELETE FROM fem_elements           WHERE schema_id=@sid;
               DELETE FROM fem_fragment_mask_items   WHERE mask_id IN (SELECT id FROM fem_fragment_masks WHERE schema_id=@sid);
               DELETE FROM fem_fragment_masks        WHERE schema_id=@sid;
               DELETE FROM fem_fragment_cut_bc       WHERE cut_id IN (SELECT id FROM fem_fragment_cuts WHERE schema_id=@sid);
               DELETE FROM fem_fragment_cuts         WHERE schema_id=@sid;
               DELETE FROM fem_fragment_ref_elements WHERE schema_id=@sid;
               DELETE FROM fem_fragment_field_loads  WHERE schema_id=@sid;
               DELETE FROM planar_connection_mappings WHERE connection_id IN (SELECT id FROM planar_connections
                 WHERE region_a_id IN (SELECT id FROM planar_regions WHERE schema_id=@sid)
                    OR region_b_id IN (SELECT id FROM planar_regions WHERE schema_id=@sid));
               DELETE FROM planar_connections
                 WHERE region_a_id IN (SELECT id FROM planar_regions WHERE schema_id=@sid)
                    OR region_b_id IN (SELECT id FROM planar_regions WHERE schema_id=@sid);
               DELETE FROM planar_regions         WHERE schema_id=@sid;
               DELETE FROM fem_mesh_nodes         WHERE schema_id=@sid;
               DELETE FROM fem_members            WHERE schema_id=@sid;
               DELETE FROM fem_nodes              WHERE schema_id=@sid;
               """, sid);

            // ── Новый состав в ту же схему ─────────────────────────────────────────────────────────────────────
            schema.FragmentZ1 = request.Z1;
            schema.FragmentZ2 = request.Z2;
            schema.ParentFingerprint = fingerprint;
            schema.FragmentBuildReportJson = StoreyFragmentBuildReport.ToJson(draft.Diagnostics);
            schema.ParentSourceType = GetFemSchemaSourceType(parentId);
            schema.FragmentPrismJson = request.Prism is { } prism
               ? FragmentPrismCodec.Encode((request.PrismInfo ?? new FragmentPrismInfo(prism)) with { Prism = prism })
               : null;
            schema.FragmentKeepParentMesh = request.KeepParentMesh;
            using (var cmd = _connection.CreateCommand())
            {
               cmd.CommandText = """
                  UPDATE fem_schemas SET parent_schema_id=@parent, fragment_z1=@z1, fragment_z2=@z2, parent_fingerprint=@fp,
                         fragment_build_report_json=@report, parent_source_type=@pst, fragment_prism_json=@prism,
                         fragment_keep_parent_mesh=@keep
                  WHERE id=@sid
                  """;
               AddFemSchemaFragmentParameters(cmd, schema);
               cmd.Parameters.AddWithValue("@sid", sid);
               cmd.ExecuteNonQuery();
            }
            schema.LoadCases.Clear();
            schema.LoadDefinitions.Clear();
            schema.MemberGroups.Clear();
            WriteStoreyFragmentContentCore(parentId, schema, draft);

            // ── Перенос ────────────────────────────────────────────────────────────────────────────────────────
            var newLcByParent = schema.LoadCases.Where(lc => lc.ParentLoadCaseId is int)
               .GroupBy(lc => lc.ParentLoadCaseId!.Value).ToDictionary(g => g.Key, g => g.First().Id);
            var lcMap = new Dictionary<int, int>();
            foreach (var lc in oldLoadCases)
               if (lc.Parent is int p && newLcByParent.TryGetValue(p, out int next)) lcMap[lc.Id] = next;
            var oldLcTag = oldLoadCases.ToDictionary(x => x.Id, x => x.Tag);
            int MapLc(int id) => lcMap.TryGetValue(id, out int next) ? next : id;

            var newNodeByTag = ReadRows("SELECT id, node_tag FROM fem_nodes WHERE schema_id=@sid", sid,
               r => (Id: r.GetInt32(0), Tag: r.GetString(1))).GroupBy(x => x.Tag, StringComparer.Ordinal)
               .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);
            var nodeMap = oldNodeTag.Where(kv => newNodeByTag.ContainsKey(kv.Value)).ToDictionary(kv => kv.Key, kv => newNodeByTag[kv.Value]);
            var newMemberByTag = ReadRows("SELECT id, elem_tag FROM fem_members WHERE schema_id=@sid", sid,
               r => (Id: r.GetInt32(0), Tag: r.GetString(1))).GroupBy(x => x.Tag, StringComparer.Ordinal)
               .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);
            var newGroupByTag = schema.MemberGroups.GroupBy(g => g.Tag, StringComparer.Ordinal)
               .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);

            foreach (var a in ReadRows("SELECT id, tag, load_expression_json, params_json FROM fem_analyses WHERE schema_id=@sid", sid,
                        r => (Id: r.GetInt32(0), Tag: r.GetString(1), Expr: r.GetString(2), Params: r.GetString(3))))
            {
               analyses++;
               var probe = new FemAnalysis { LoadExpressionJson = a.Expr, ParamsJson = a.Params };
               var missing = OpenCS.Tasks.FemAnalysisParams.ReferencedLoadCases(probe).Where(id => !lcMap.ContainsKey(id)).ToList();
               if (missing.Count > 0)
                  lost.Add(new("fragment_rebuild_load_cases_lost", string.Format(Loc.S("FragmentRebuildLoadCasesLost"), a.Tag,
                     string.Join(", ", missing.Select(id => oldLcTag.TryGetValue(id, out var t) ? $"«{t}»" : $"#{id}"))), IsError: false));
               string expr = FemLoadExpression.Parse(a.Expr).RemapLoadCases(MapLc).ToJson();
               var parameters = OpenCS.Tasks.FemAnalysisParams.Parse(a.Params);
               parameters.RemapLoadCases(MapLc);
               parameters.RemapNodes(nodeMap);
               using var cmd = _connection.CreateCommand();
               cmd.CommandText = "UPDATE fem_analyses SET load_expression_json=@e, params_json=@p WHERE id=@id";
               cmd.Parameters.AddWithValue("@e", expr);
               cmd.Parameters.AddWithValue("@p", parameters.ToJson());
               cmd.Parameters.AddWithValue("@id", a.Id);
               cmd.ExecuteNonQuery();
            }

            foreach (var c in ReadRows("SELECT id, tag, member_id, element_id FROM fem_checks WHERE schema_id=@sid", sid,
                        r => (Id: r.GetInt32(0), Tag: r.GetString(1), Group: r.GetInt32(2), Element: r.IsDBNull(3) ? (int?)null : r.GetInt32(3))))
            {
               using var cmd = _connection.CreateCommand();
               cmd.Parameters.AddWithValue("@id", c.Id);
               if (c.Element is int eid)
               {
                  string tag = oldMemberTag.GetValueOrDefault(eid, $"#{eid}");
                  if (newMemberByTag.TryGetValue(tag, out int next))
                  {
                     cmd.CommandText = "UPDATE fem_checks SET element_id=@v WHERE id=@id";
                     cmd.Parameters.AddWithValue("@v", next);
                     checks++;
                  }
                  else
                  {
                     cmd.CommandText = "DELETE FROM fem_checks WHERE id=@id";
                     lost.Add(new("fragment_rebuild_check_lost", string.Format(Loc.S("FragmentRebuildCheckMemberLost"), c.Tag, tag), IsError: false));
                  }
               }
               else
               {
                  string tag = oldGroupTag.GetValueOrDefault(c.Group, $"#{c.Group}");
                  if (newGroupByTag.TryGetValue(tag, out int next))
                  {
                     cmd.CommandText = "UPDATE fem_checks SET member_id=@v WHERE id=@id";
                     cmd.Parameters.AddWithValue("@v", next);
                     checks++;
                  }
                  else
                  {
                     cmd.CommandText = "DELETE FROM fem_checks WHERE id=@id";
                     lost.Add(new("fragment_rebuild_check_lost", string.Format(Loc.S("FragmentRebuildCheckGroupLost"), c.Tag, tag), IsError: false));
                  }
               }
               cmd.ExecuteNonQuery();
            }

            foreach (var imp in ReadRows("SELECT id, tag, load_case_id FROM fem_imperfections WHERE schema_id=@sid", sid,
                        r => (Id: r.GetInt32(0), Tag: r.GetString(1), LoadCase: r.GetInt32(2))))
            {
               using var cmd = _connection.CreateCommand();
               cmd.Parameters.AddWithValue("@id", imp.Id);
               if (lcMap.TryGetValue(imp.LoadCase, out int next))
               {
                  cmd.CommandText = "UPDATE fem_imperfections SET load_case_id=@v WHERE id=@id";
                  cmd.Parameters.AddWithValue("@v", next);
                  imperfections++;
               }
               else
               {
                  cmd.CommandText = "DELETE FROM fem_imperfections WHERE id=@id";
                  lost.Add(new("fragment_rebuild_imperfection_lost", string.Format(Loc.S("FragmentRebuildImperfectionLost"), imp.Tag,
                     oldLcTag.GetValueOrDefault(imp.LoadCase, $"#{imp.LoadCase}")), IsError: false));
               }
               cmd.ExecuteNonQuery();
            }

            var newCuts = GetFragmentCutsWithIds(sid);
            var (byIndex, lostCuts) = FragmentRebuildTransfer.MapCutBc(oldCutBc, newCuts.Select(c => c.Cut).ToList());
            using (var cmd = _connection.CreateCommand())
            {
               cmd.CommandText = "INSERT INTO fem_fragment_cut_bc (cut_id, modes) VALUES (@cut, @modes)";
               var cut = cmd.Parameters.Add("@cut", SqliteType.Integer);
               var modes = cmd.Parameters.Add("@modes", SqliteType.Blob);
               foreach (var (index, bytes) in byIndex)
               {
                  cut.Value = newCuts[index].Id;
                  modes.Value = bytes;
                  cmd.ExecuteNonQuery();
                  cutBc++;
               }
            }
            foreach (var c in lostCuts)
               lost.Add(new("fragment_rebuild_cut_bc_lost", string.Format(Loc.S("FragmentRebuildCutBcLost"), c.MemberTag,
                  FragmentCutLabel.Side(c)), IsError: false));

            tx.Commit();
         }
         catch
         {
            tx.Rollback();
            // Коллекции схемы в памяти уже частично заменены — снова из базы (там прежний фрагмент).
            ReloadFemSchemaCollections(schema);
            RestoreFemSchemaFragmentFields(schema);
            throw;
         }
         ReloadFemSchemaCollections(schema);
         ForgetFemSchemaResults(sid, resultIds);
         LoadFemChecks();
         return new(analyses, checks, cutBc, imperfections, lost);
      }

      /// <summary>Поля фрагмента строки схемы — снова из базы (после отката пересборки).</summary>
      void RestoreFemSchemaFragmentFields(FemSchema schema)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT fragment_z1, fragment_z2, parent_fingerprint, fragment_build_report_json, parent_source_type,
                   fragment_prism_json, fragment_keep_parent_mesh
            FROM fem_schemas WHERE id=@sid
            """;
         cmd.Parameters.AddWithValue("@sid", schema.Id);
         using var r = cmd.ExecuteReader();
         if (!r.Read()) return;
         schema.FragmentZ1 = r.IsDBNull(0) ? null : r.GetDouble(0);
         schema.FragmentZ2 = r.IsDBNull(1) ? null : r.GetDouble(1);
         schema.ParentFingerprint = r.IsDBNull(2) ? null : r.GetString(2);
         schema.FragmentBuildReportJson = r.IsDBNull(3) ? null : r.GetString(3);
         schema.ParentSourceType = r.IsDBNull(4) ? null : r.GetString(4);
         schema.FragmentPrismJson = r.IsDBNull(5) ? null : r.GetString(5);
         schema.FragmentKeepParentMesh = !r.IsDBNull(6) && r.GetInt64(6) != 0;
      }

      List<T> ReadRows<T>(string sql, int schemaId, Func<SqliteDataReader, T> map)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = sql;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var r = cmd.ExecuteReader();
         var list = new List<T>();
         while (r.Read()) list.Add(map(r));
         return list;
      }

      void Exec(string sql, int schemaId)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = sql;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         cmd.ExecuteNonQuery();
      }
   }
}
