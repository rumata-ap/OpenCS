using CScore.Fem;
using CScore.Fem.Fragment;
using Microsoft.Data.Sqlite;

namespace OpenCS.Utilites
{
   /// <summary>Снятие замка сохранённой сетки с КонЭ фрагмента (лестница перемешивания, срез 4).</summary>
   public partial class DatabaseService
   {
      /// <summary>
      /// Записывает снятие замка (<see cref="FemKeptMeshUnlock.Plan"/>) вместе с прошедшей проверку сеткой одной
      /// транзакцией: замок КонЭ → <c>generated</c>; маски и новые узлы схемы; узловые нагрузки, нагрузки КонЭ и пружины
      /// узлов; ГУ, нагрузки и тела удаляемых узлов сетки — удаляются; нагрузки на КЭ — замена; поле и опорная сетка —
      /// дописываются; импортные КЭ и узлы снятых КонЭ — удаляются; сгенерированная сетка — как
      /// <see cref="SaveFemMeshSnapshotKeepingImported"/>. Без сетки (<paramref name="meshNodes"/> = null, ручная команда
      /// «Перестроить сетку КонЭ») сгенерированные строки не трогаются — сетка схемы становится устаревшей.
      /// </summary>
      public void ApplyFemKeptMeshUnlock(int schemaId, FemKeptMeshUnlockPlan plan, IReadOnlyList<FemMeshNode>? meshNodes,
         IReadOnlyList<FemElement>? meshElements)
      {
         ArgumentNullException.ThrowIfNull(plan);
         using var tx = _connection.BeginTransaction();
         try
         {
            Each("UPDATE fem_members SET mesh_source='generated' WHERE schema_id=@sid AND elem_tag=@tag", plan.UnlockedMemberTags);

            using (var cmd = _connection.CreateCommand())
            {
               cmd.CommandText = "UPDATE fem_nodes SET dof_mask=@m WHERE schema_id=@sid AND node_tag=@tag";
               cmd.Parameters.AddWithValue("@sid", schemaId);
               var tag = cmd.Parameters.Add("@tag", SqliteType.Text);
               var mask = cmd.Parameters.Add("@m", SqliteType.Integer);
               foreach (var (t, m) in plan.NodeMasks) { tag.Value = t; mask.Value = m; cmd.ExecuteNonQuery(); }
            }
            using (var cmd = _connection.CreateCommand())
            {
               cmd.CommandText = "INSERT INTO fem_nodes (schema_id, node_tag, x, y, z, dof_mask) VALUES (@sid, @tag, @x, @y, @z, @dm)";
               foreach (var n in plan.NewNodes)
               {
                  cmd.Parameters.Clear();
                  cmd.Parameters.AddWithValue("@sid", schemaId);
                  cmd.Parameters.AddWithValue("@tag", n.NodeTag);
                  cmd.Parameters.AddWithValue("@x", n.X);
                  cmd.Parameters.AddWithValue("@y", n.Y);
                  cmd.Parameters.AddWithValue("@z", n.Z);
                  cmd.Parameters.AddWithValue("@dm", n.DofMask);
                  cmd.ExecuteNonQuery();
               }
            }
            var nodeIds = Ids("SELECT id, node_tag FROM fem_nodes WHERE schema_id=@sid");
            var memberIds = Ids("SELECT id, elem_tag FROM fem_members WHERE schema_id=@sid");

            using (var cmd = _connection.CreateCommand())
            {
               cmd.CommandText = """
                  INSERT INTO fem_node_loads (schema_id, load_case_id, node_id, fx, fy, fz, mx, my, mz)
                  VALUES (@sid, @lc, @nid, @fx, @fy, @fz, @mx, @my, @mz)
                  """;
               foreach (var (tag, l) in plan.NodeLoads)
               {
                  cmd.Parameters.Clear();
                  cmd.Parameters.AddWithValue("@sid", schemaId);
                  cmd.Parameters.AddWithValue("@lc", l.LoadCaseId);
                  cmd.Parameters.AddWithValue("@nid", nodeIds.TryGetValue(tag, out int id) ? id
                     : throw new InvalidOperationException($"Узловая нагрузка ссылается на узел {tag}, которого нет в схеме."));
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
                  INSERT INTO fem_member_loads
                     (schema_id, load_case_id, member_id, coordinate_system, distribution_type,
                      start_offset_m, end_offset_m, qx_start, qy_start, qz_start, qx_end, qy_end, qz_end, mx, my, mz)
                  VALUES (@sid, @lc, @mid, @cs, @dt, @so, @eo, @qxs, @qys, @qzs, @qxe, @qye, @qze, @mx, @my, @mz)
                  """;
               foreach (var (tag, l) in plan.MemberLoads)
               {
                  cmd.Parameters.Clear();
                  cmd.Parameters.AddWithValue("@sid", schemaId);
                  cmd.Parameters.AddWithValue("@lc", l.LoadCaseId);
                  cmd.Parameters.AddWithValue("@mid", memberIds.TryGetValue(tag, out int id) ? id
                     : throw new InvalidOperationException($"Нагрузка ссылается на КонЭ «{tag}», которого нет в схеме."));
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

            // ГУ, нагрузки и тела удаляемых узлов сетки.
            Each("""
               DELETE FROM fem_mesh_node_supports WHERE schema_id=@sid AND node_tag=@tag;
               DELETE FROM fem_springs WHERE schema_id=@sid AND target_kind='mesh_node' AND node_tag=@tag;
               DELETE FROM fem_mesh_node_loads WHERE schema_id=@sid AND mesh_node_tag=@tag;
               """, plan.RemovedNodeTags);
            EachId("DELETE FROM fem_rigid_bodies WHERE id=@id", plan.RemovedRigidBodies.Select(b => b.Id));
            InsertRigidBodiesCore("fem_node_rigid_bodies", schemaId, plan.NodeRigidBodies, updateObjects: false);
            InsertFemBoundaryCore(schemaId, [], plan.NodeSprings, [], updateObjects: false);

            EachId("DELETE FROM fem_element_loads WHERE id=@id", plan.ReplacedElementLoads.Select(l => l.Id));
            InsertFemMeshLevelLoadsCore(schemaId, plan.AddedElementLoads, [], id => id, group => group, updateObjects: false);
            InsertStoreyFragmentDataCore(schemaId, [], plan.RefElements, plan.FieldLoads, id => id);

            Each("DELETE FROM fem_elements WHERE schema_id=@sid AND origin='imported' AND elem_tag=@tag", plan.RemovedElementTags);
            Each("DELETE FROM fem_mesh_nodes WHERE schema_id=@sid AND origin='imported' AND node_tag=@tag", plan.RemovedNodeTags);
            if (meshNodes != null && meshElements != null)
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
                  meshNodes.Where(n => n.Origin != FemMember.MeshSourceImported).ToList(),
                  meshElements.Where(e => e.Origin != FemMember.MeshSourceImported).ToList());
            }
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }

         void Each(string sql, IEnumerable<string> tags)
         {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@sid", schemaId);
            var tag = cmd.Parameters.Add("@tag", SqliteType.Text);
            foreach (var t in tags) { tag.Value = t; cmd.ExecuteNonQuery(); }
         }

         void EachId(string sql, IEnumerable<int> ids)
         {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            var id = cmd.Parameters.Add("@id", SqliteType.Integer);
            foreach (var i in ids.Where(i => i > 0)) { id.Value = i; cmd.ExecuteNonQuery(); }
         }

         Dictionary<string, int> Ids(string sql)
         {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@sid", schemaId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) result.TryAdd(r.GetString(1), r.GetInt32(0));
            return result;
         }
      }
   }
}
