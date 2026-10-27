using CScore.Fem;
using Microsoft.Data.Sqlite;

namespace OpenCS.Utilites
{
   /// <summary>Нагрузки сеточного уровня схемы: на КЭ (<see cref="FemElementLoad"/>) и на узлы сетки (<see cref="FemMeshNodeLoad"/>).</summary>
   public partial class DatabaseService
   {
      /// <summary>Нагрузки на КЭ схемы (все загружения или одно).</summary>
      public List<FemElementLoad> GetFemElementLoads(int schemaId, int? loadCaseId = null) =>
         QueryFemElementLoads(schemaId, "@lc IS NULL OR load_case_id=@lc", loadCaseId);

      /// <summary>Нагрузки на КЭ схемы с условием SQL <paramref name="where"/> (параметры @sid, @lc заданы).</summary>
      List<FemElementLoad> QueryFemElementLoads(int schemaId, string where, int? loadCaseId = null)
      {
         var result = new List<FemElementLoad>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = $"""
            SELECT id, load_case_id, origin, target_kind, target_tags_json, group_id, load_kind,
                   coordinate_system, axis, values_json
            FROM fem_element_loads
            WHERE schema_id=@sid AND ({where})
            ORDER BY id
         """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         cmd.Parameters.AddWithValue("@lc", (object?)loadCaseId ?? DBNull.Value);
         // Происхождение, вид, система координат и ось у сотен тысяч строк одни и те же — один экземпляр строки на значение.
         var pool = new HashSet<string>(StringComparer.Ordinal);
         string Pooled(string s) { if (pool.TryGetValue(s, out var known)) return known; pool.Add(s); return s; }
         using var r = cmd.ExecuteReader();
         while (r.Read())
            result.Add(new FemElementLoad
            {
               Id = r.GetInt32(0), SchemaId = schemaId, LoadCaseId = r.GetInt32(1), Origin = Pooled(r.GetString(2)),
               TargetKind = Pooled(r.GetString(3)), TargetTagsJson = r.GetString(4), GroupId = r.IsDBNull(5) ? null : r.GetInt32(5),
               LoadKind = Pooled(r.GetString(6)), CoordinateSystem = Pooled(r.GetString(7)), Axis = Pooled(r.GetString(8)),
               ValuesJson = r.GetString(9),
            });
         return result;
      }

      /// <summary>Узловые нагрузки на узлы сетки схемы (все загружения или одно).</summary>
      public List<FemMeshNodeLoad> GetFemMeshNodeLoads(int schemaId, int? loadCaseId = null) =>
         QueryFemMeshNodeLoads(schemaId, "@lc IS NULL OR load_case_id=@lc", loadCaseId);

      /// <summary>Нагрузки на узлы сетки схемы с условием SQL <paramref name="where"/> (параметры @sid, @lc заданы).</summary>
      List<FemMeshNodeLoad> QueryFemMeshNodeLoads(int schemaId, string where, int? loadCaseId = null)
      {
         var result = new List<FemMeshNodeLoad>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = $"""
            SELECT id, load_case_id, mesh_node_tag, origin, fx, fy, fz, mx, my, mz
            FROM fem_mesh_node_loads
            WHERE schema_id=@sid AND ({where})
            ORDER BY id
         """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         cmd.Parameters.AddWithValue("@lc", (object?)loadCaseId ?? DBNull.Value);
         var pool = new HashSet<string>(StringComparer.Ordinal);
         string Pooled(string s) { if (pool.TryGetValue(s, out var known)) return known; pool.Add(s); return s; }
         using var r = cmd.ExecuteReader();
         while (r.Read())
            result.Add(new FemMeshNodeLoad
            {
               Id = r.GetInt32(0), SchemaId = schemaId, LoadCaseId = r.GetInt32(1), MeshNodeTag = r.GetString(2),
               Origin = Pooled(r.GetString(3)), Fx = r.GetDouble(4), Fy = r.GetDouble(5), Fz = r.GetDouble(6),
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
         // Параметры создаются один раз, у строк меняются только значения, Id — sqlite3_last_insert_rowid: на сотнях тысяч
         // нагрузок (ФР19 — 0,6 млн) пересоздание параметров и второй оператор (SELECT last_insert_rowid() или RETURNING)
         // заметно дороже самой вставки.
         var handle = _connection.Handle!;
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = """
               INSERT INTO fem_element_loads (schema_id, load_case_id, origin, target_kind, target_tags_json, group_id,
                                              load_kind, coordinate_system, axis, values_json)
               VALUES (@sid, @lc, @origin, @tk, @tags, @gid, @kind, @cs, @axis, @vals)
            """;
            cmd.Parameters.AddWithValue("@sid", schemaId);
            var lcP = cmd.Parameters.Add("@lc", SqliteType.Integer);
            var originP = cmd.Parameters.Add("@origin", SqliteType.Text);
            var tkP = cmd.Parameters.Add("@tk", SqliteType.Text);
            var tagsP = cmd.Parameters.Add("@tags", SqliteType.Text);
            var gidP = cmd.Parameters.Add("@gid", SqliteType.Integer);
            var kindP = cmd.Parameters.Add("@kind", SqliteType.Text);
            var csP = cmd.Parameters.Add("@cs", SqliteType.Text);
            var axisP = cmd.Parameters.Add("@axis", SqliteType.Text);
            var valsP = cmd.Parameters.Add("@vals", SqliteType.Text);
            foreach (var load in elementLoads)
            {
               int lc = mapLoadCase(load.LoadCaseId);
               int? group = mapGroup(load.GroupId);
               lcP.Value = lc;
               originP.Value = load.Origin;
               tkP.Value = load.TargetKind;
               tagsP.Value = load.TargetTagsJson;
               gidP.Value = (object?)group ?? DBNull.Value;
               kindP.Value = load.LoadKind;
               csP.Value = load.CoordinateSystem;
               axisP.Value = load.Axis;
               valsP.Value = load.ValuesJson;
               cmd.ExecuteNonQuery();
               int id = (int)SQLitePCL.raw.sqlite3_last_insert_rowid(handle);
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
               VALUES (@sid, @lc, @tag, @origin, @fx, @fy, @fz, @mx, @my, @mz)
            """;
            cmd.Parameters.AddWithValue("@sid", schemaId);
            var lcP = cmd.Parameters.Add("@lc", SqliteType.Integer);
            var tagP = cmd.Parameters.Add("@tag", SqliteType.Text);
            var originP = cmd.Parameters.Add("@origin", SqliteType.Text);
            var fxP = cmd.Parameters.Add("@fx", SqliteType.Real);
            var fyP = cmd.Parameters.Add("@fy", SqliteType.Real);
            var fzP = cmd.Parameters.Add("@fz", SqliteType.Real);
            var mxP = cmd.Parameters.Add("@mx", SqliteType.Real);
            var myP = cmd.Parameters.Add("@my", SqliteType.Real);
            var mzP = cmd.Parameters.Add("@mz", SqliteType.Real);
            foreach (var load in meshNodeLoads)
            {
               int lc = mapLoadCase(load.LoadCaseId);
               lcP.Value = lc;
               tagP.Value = load.MeshNodeTag;
               originP.Value = load.Origin;
               fxP.Value = load.Fx;
               fyP.Value = load.Fy;
               fzP.Value = load.Fz;
               mxP.Value = load.Mx;
               myP.Value = load.My;
               mzP.Value = load.Mz;
               cmd.ExecuteNonQuery();
               int id = (int)SQLitePCL.raw.sqlite3_last_insert_rowid(handle);
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

      /// <summary>
      /// Записывает итог переноса нагрузок из программы-источника одной транзакцией: загружения (Id ≤ 0 — новые,
      /// иначе обновляются) и полный набор нагрузок сеточного уровня схемы (заменяет прежний). Конструктивный слой,
      /// определения и постановки не трогаются; загружения в памяти схемы обновляются.
      /// </summary>
      public void SaveFemLoadCasesAndMeshLoads(int schemaId, IReadOnlyList<FemLoadCase> loadCases,
         IReadOnlyList<FemElementLoad> elementLoads, IReadOnlyList<FemMeshNodeLoad> meshNodeLoads)
      {
         var newIdByTemp = new Dictionary<int, int>();
         using var tx = _connection.BeginTransaction();
         try
         {
            foreach (var lc in loadCases)
            {
               using var cmd = _connection.CreateCommand();
               lc.SchemaId = schemaId;
               if (lc.Id <= 0)
               {
                  cmd.CommandText = """
                     INSERT INTO fem_load_cases
                        (schema_id, tag, load_type, sp20_type, sp20_group, gamma_f_unfav, gamma_f_fav, psi1, psi2,
                         self_weight_factor, origin, source_load_num, long_term_share)
                     VALUES (@sid, @tag, @lt, @st, @sg, @gu, @gf, @p1, @p2, @swf, @origin, @sln, @lts);
                     SELECT last_insert_rowid();
                     """;
                  AddFemLoadCaseParameters(cmd, lc);
                  int newId = (int)(long)cmd.ExecuteScalar()!;
                  newIdByTemp[lc.Id] = newId;
                  lc.Id = newId;
               }
               else
               {
                  cmd.CommandText = """
                     UPDATE fem_load_cases SET tag=@tag, load_type=@lt, sp20_type=@st, sp20_group=@sg,
                        gamma_f_unfav=@gu, gamma_f_fav=@gf, psi1=@p1, psi2=@p2, self_weight_factor=@swf,
                        origin=@origin, source_load_num=@sln, long_term_share=@lts
                     WHERE id=@id AND schema_id=@sid
                     """;
                  AddFemLoadCaseParameters(cmd, lc);
                  cmd.Parameters.AddWithValue("@id", lc.Id);
                  cmd.ExecuteNonQuery();
               }
            }
            DeleteFemMeshLevelLoadsCore(schemaId);
            InsertFemMeshLevelLoadsCore(schemaId, elementLoads, meshNodeLoads,
               id => newIdByTemp.TryGetValue(id, out int mapped) ? mapped : id, group => group);
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }

         if (FemSchemas.FirstOrDefault(s => s.Id == schemaId) is { } schema)
            foreach (var lc in loadCases)
               if (!schema.LoadCases.Contains(lc) && schema.LoadCases.All(c => c.Id != lc.Id))
                  schema.LoadCases.Add(lc);
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
