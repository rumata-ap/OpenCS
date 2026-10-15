using CScore.Fem;

namespace OpenCS.Utilites
{
   /// <summary>
   /// ГУ схемы для дерева: сводки ГУ КЭ сетки (шарниры стержней, упругое основание) запросами, без чтения всей сетки
   /// (импортированные схемы — сотни тысяч КЭ), и правка узловых связей группы узлов сетки.
   /// </summary>
   public partial class DatabaseService
   {
      /// <summary>Сочетания освобождений концов I/J стержней схемы (0 — жёсткое примыкание) и число КЭ с каждым.</summary>
      public List<(int ReleaseI, int ReleaseJ, int Count)> GetFemElementReleaseGroups(int schemaId)
      {
         var result = new List<(int, int, int)>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT COALESCE(release_i, 0) AS ri, COALESCE(release_j, 0) AS rj, COUNT(*)
            FROM fem_elements WHERE schema_id=@sid AND (COALESCE(release_i, 0) <> 0 OR COALESCE(release_j, 0) <> 0)
            GROUP BY ri, rj ORDER BY ri, rj
            """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var r = cmd.ExecuteReader();
         while (r.Read()) result.Add((r.GetInt32(0), r.GetInt32(1), r.GetInt32(2)));
         return result;
      }

      /// <summary>Номера КЭ схемы с освобождениями концов <paramref name="releaseI"/> / <paramref name="releaseJ"/>.</summary>
      public List<string> GetFemElementTagsByReleases(int schemaId, int releaseI, int releaseJ)
      {
         var result = new List<string>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT elem_tag FROM fem_elements
            WHERE schema_id=@sid AND COALESCE(release_i, 0)=@ri AND COALESCE(release_j, 0)=@rj ORDER BY id
            """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         cmd.Parameters.AddWithValue("@ri", releaseI);
         cmd.Parameters.AddWithValue("@rj", releaseJ);
         using var r = cmd.ExecuteReader();
         while (r.Read()) result.Add(r.GetString(0));
         return result;
      }

      /// <summary>Число КЭ схемы на упругом основании (C1 ≠ 0).</summary>
      public int GetFemFoundationElementCount(int schemaId)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT COUNT(*) FROM fem_elements WHERE schema_id=@sid AND COALESCE(foundation_c1, 0) <> 0";
         cmd.Parameters.AddWithValue("@sid", schemaId);
         return Convert.ToInt32(cmd.ExecuteScalar());
      }

      /// <summary>
      /// Ручная правка узловых связей группы узлов сетки одной транзакцией — как <see cref="SetFemMeshNodeBoundary"/> для
      /// каждого узла: записи узлов любого происхождения заменяются ручными.
      /// </summary>
      /// <param name="stiffnesses">Жёсткости X Y Z UX UY UZ, Н/м и Н·м/рад.</param>
      public void SetFemMeshNodesBoundary(int schemaId, IReadOnlyCollection<string> nodeTags, int mask,
         IReadOnlyList<double> stiffnesses)
      {
         using var tx = _connection.BeginTransaction();
         try
         {
            using (var cmd = _connection.CreateCommand())
            {
               cmd.CommandText = """
                  DELETE FROM fem_mesh_node_supports WHERE schema_id=@sid AND node_tag=@tag;
                  DELETE FROM fem_springs WHERE schema_id=@sid AND node_tag=@tag AND target_kind=@kind;
                  """;
               cmd.Parameters.AddWithValue("@sid", schemaId);
               var tag = cmd.Parameters.Add("@tag", Microsoft.Data.Sqlite.SqliteType.Text);
               cmd.Parameters.AddWithValue("@kind", FemSpringTargetKinds.MeshNode);
               foreach (var nodeTag in nodeTags)
               {
                  tag.Value = nodeTag;
                  cmd.ExecuteNonQuery();
               }
            }
            var supports = nodeTags.Select(t => new FemMeshNodeSupport
               { NodeTag = t, Mask = mask & FemBoundaryDofs.All, Origin = FemLoadOrigin.Manual }).ToList();
            var springs = new List<FemSpring>();
            foreach (var t in nodeTags)
            {
               var spring = new FemSpring { TargetKind = FemSpringTargetKinds.MeshNode, NodeTag = t, Origin = FemLoadOrigin.Manual };
               spring.SetStiffnesses(stiffnesses);
               if (spring.ActiveMask != 0) springs.Add(spring);
            }
            InsertFemBoundaryCore(schemaId, supports, springs, []);
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }
   }
}
