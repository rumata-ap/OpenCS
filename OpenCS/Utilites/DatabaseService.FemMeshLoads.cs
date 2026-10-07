using CScore.Fem;
using Microsoft.Data.Sqlite;

namespace OpenCS.Utilites
{
   /// <summary>Нагрузки сеточного уровня схемы: на КЭ (<see cref="FemElementLoad"/>) и на узлы сетки (<see cref="FemMeshNodeLoad"/>).</summary>
   public partial class DatabaseService
   {
      /// <summary>Нагрузки на КЭ схемы (все загружения или одно).</summary>
      public List<FemElementLoad> GetFemElementLoads(int schemaId, int? loadCaseId = null)
      {
         var result = new List<FemElementLoad>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT id, load_case_id, origin, target_kind, target_tags_json, group_id, load_kind,
                   coordinate_system, axis, values_json
            FROM fem_element_loads
            WHERE schema_id=@sid AND (@lc IS NULL OR load_case_id=@lc)
            ORDER BY id
         """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         cmd.Parameters.AddWithValue("@lc", (object?)loadCaseId ?? DBNull.Value);
         using var r = cmd.ExecuteReader();
         while (r.Read())
            result.Add(new FemElementLoad
            {
               Id = r.GetInt32(0), SchemaId = schemaId, LoadCaseId = r.GetInt32(1), Origin = r.GetString(2),
               TargetKind = r.GetString(3), TargetTagsJson = r.GetString(4), GroupId = r.IsDBNull(5) ? null : r.GetInt32(5),
               LoadKind = r.GetString(6), CoordinateSystem = r.GetString(7), Axis = r.GetString(8), ValuesJson = r.GetString(9),
            });
         return result;
      }

      /// <summary>Узловые нагрузки на узлы сетки схемы (все загружения или одно).</summary>
      public List<FemMeshNodeLoad> GetFemMeshNodeLoads(int schemaId, int? loadCaseId = null)
      {
         var result = new List<FemMeshNodeLoad>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT id, load_case_id, mesh_node_tag, origin, fx, fy, fz, mx, my, mz
            FROM fem_mesh_node_loads
            WHERE schema_id=@sid AND (@lc IS NULL OR load_case_id=@lc)
            ORDER BY id
         """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         cmd.Parameters.AddWithValue("@lc", (object?)loadCaseId ?? DBNull.Value);
         using var r = cmd.ExecuteReader();
         while (r.Read())
            result.Add(new FemMeshNodeLoad
            {
               Id = r.GetInt32(0), SchemaId = schemaId, LoadCaseId = r.GetInt32(1), MeshNodeTag = r.GetString(2),
               Origin = r.GetString(3), Fx = r.GetDouble(4), Fy = r.GetDouble(5), Fz = r.GetDouble(6),
               Mx = r.GetDouble(7), My = r.GetDouble(8), Mz = r.GetDouble(9),
            });
         return result;
      }

      /// <summary>
      /// Вставляет нагрузки сеточного уровня в схему <paramref name="schemaId"/> (внутри транзакции вызывающего).
      /// <paramref name="mapLoadCase"/> и <paramref name="mapGroup"/> переводят Id загружений и групп (при полной
      /// перезаписи и копировании схемы); объектам проставляются новые Id, схема и загружение.
      /// </summary>
      void InsertFemMeshLevelLoadsCore(int schemaId, IEnumerable<FemElementLoad> elementLoads,
         IEnumerable<FemMeshNodeLoad> meshNodeLoads, Func<int, int> mapLoadCase, Func<int?, int?> mapGroup,
         bool updateObjects = true)
      {
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = """
               INSERT INTO fem_element_loads (schema_id, load_case_id, origin, target_kind, target_tags_json, group_id,
                                              load_kind, coordinate_system, axis, values_json)
               VALUES (@sid, @lc, @origin, @tk, @tags, @gid, @kind, @cs, @axis, @vals);
               SELECT last_insert_rowid();
            """;
            foreach (var load in elementLoads)
            {
               int lc = mapLoadCase(load.LoadCaseId);
               int? group = mapGroup(load.GroupId);
               cmd.Parameters.Clear();
               cmd.Parameters.AddWithValue("@sid", schemaId);
               cmd.Parameters.AddWithValue("@lc", lc);
               cmd.Parameters.AddWithValue("@origin", load.Origin);
               cmd.Parameters.AddWithValue("@tk", load.TargetKind);
               cmd.Parameters.AddWithValue("@tags", load.TargetTagsJson);
               cmd.Parameters.AddWithValue("@gid", (object?)group ?? DBNull.Value);
               cmd.Parameters.AddWithValue("@kind", load.LoadKind);
               cmd.Parameters.AddWithValue("@cs", load.CoordinateSystem);
               cmd.Parameters.AddWithValue("@axis", load.Axis);
               cmd.Parameters.AddWithValue("@vals", load.ValuesJson);
               int id = (int)(long)cmd.ExecuteScalar()!;
               if (!updateObjects) continue;
               load.Id = id;
               load.SchemaId = schemaId;
               load.LoadCaseId = lc;
               load.GroupId = group;
            }
         }
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = """
               INSERT INTO fem_mesh_node_loads (schema_id, load_case_id, mesh_node_tag, origin, fx, fy, fz, mx, my, mz)
               VALUES (@sid, @lc, @tag, @origin, @fx, @fy, @fz, @mx, @my, @mz);
               SELECT last_insert_rowid();
            """;
            foreach (var load in meshNodeLoads)
            {
               int lc = mapLoadCase(load.LoadCaseId);
               cmd.Parameters.Clear();
               cmd.Parameters.AddWithValue("@sid", schemaId);
               cmd.Parameters.AddWithValue("@lc", lc);
               cmd.Parameters.AddWithValue("@tag", load.MeshNodeTag);
               cmd.Parameters.AddWithValue("@origin", load.Origin);
               cmd.Parameters.AddWithValue("@fx", load.Fx);
               cmd.Parameters.AddWithValue("@fy", load.Fy);
               cmd.Parameters.AddWithValue("@fz", load.Fz);
               cmd.Parameters.AddWithValue("@mx", load.Mx);
               cmd.Parameters.AddWithValue("@my", load.My);
               cmd.Parameters.AddWithValue("@mz", load.Mz);
               int id = (int)(long)cmd.ExecuteScalar()!;
               if (!updateObjects) continue;
               load.Id = id;
               load.SchemaId = schemaId;
               load.LoadCaseId = lc;
            }
         }
      }

      /// <summary>
      /// Полная перезапись загружений без переданных нагрузок сеточного уровня: их строки переводятся на новые Id
      /// загружений, строки удалённых загружений удаляются. Новые Id (AUTOINCREMENT) больше любых старых, поэтому
      /// последовательные UPDATE не сталкиваются.
      /// </summary>
      void RemapFemMeshLevelLoadsCore(int schemaId, IReadOnlyDictionary<int, int> newLoadCaseIdByOld)
      {
         foreach (var table in new[] { "fem_element_loads", "fem_mesh_node_loads" })
         {
            using (var cmd = _connection.CreateCommand())
            {
               cmd.CommandText = $"UPDATE {table} SET load_case_id=@new WHERE schema_id=@sid AND load_case_id=@old";
               cmd.Parameters.AddWithValue("@sid", schemaId);
               var oldParam = cmd.Parameters.Add("@old", SqliteType.Integer);
               var newParam = cmd.Parameters.Add("@new", SqliteType.Integer);
               foreach (var (oldId, newId) in newLoadCaseIdByOld)
               {
                  if (oldId == newId) continue;
                  oldParam.Value = oldId;
                  newParam.Value = newId;
                  cmd.ExecuteNonQuery();
               }
            }
            using (var cmd = _connection.CreateCommand())
            {
               cmd.CommandText = $"""
                  DELETE FROM {table} WHERE schema_id=@sid
                    AND load_case_id NOT IN (SELECT id FROM fem_load_cases WHERE schema_id=@sid)
                  """;
               cmd.Parameters.AddWithValue("@sid", schemaId);
               cmd.ExecuteNonQuery();
            }
         }
      }

      /// <summary>Удаляет нагрузки сеточного уровня схемы (внутри транзакции вызывающего).</summary>
      void DeleteFemMeshLevelLoadsCore(int schemaId)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            DELETE FROM fem_element_loads   WHERE schema_id=@sid;
            DELETE FROM fem_mesh_node_loads WHERE schema_id=@sid;
            """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         cmd.ExecuteNonQuery();
      }
   }
}
