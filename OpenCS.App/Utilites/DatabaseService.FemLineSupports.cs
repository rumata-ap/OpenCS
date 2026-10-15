using CScore.Fem;
using CScore.Planar;

namespace OpenCS.Utilites
{
   /// <summary>Линейные связи на гранях пластинчатых КонЭ (<see cref="FemLineSupport"/>).</summary>
   public partial class DatabaseService
   {
      const string FemLineSupportsDdl = """
         CREATE TABLE IF NOT EXISTS fem_line_supports (
             id INTEGER PRIMARY KEY AUTOINCREMENT,
             schema_id INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
             member_tag TEXT NOT NULL,
             loop TEXT NOT NULL DEFAULT 'outer',
             hole_index INTEGER NOT NULL DEFAULT 0,
             start_vertex INTEGER NOT NULL DEFAULT 0,
             x1 REAL NOT NULL DEFAULT 0, y1 REAL NOT NULL DEFAULT 0, z1 REAL NOT NULL DEFAULT 0,
             x2 REAL NOT NULL DEFAULT 0, y2 REAL NOT NULL DEFAULT 0, z2 REAL NOT NULL DEFAULT 0,
             mask INTEGER NOT NULL DEFAULT 0,
             fix_edge_rotation INTEGER NOT NULL DEFAULT 0,
             kx REAL NOT NULL DEFAULT 0, ky REAL NOT NULL DEFAULT 0, kz REAL NOT NULL DEFAULT 0,
             kux REAL NOT NULL DEFAULT 0, kuy REAL NOT NULL DEFAULT 0, kuz REAL NOT NULL DEFAULT 0,
             origin TEXT NOT NULL DEFAULT 'manual'
         );
         CREATE INDEX IF NOT EXISTS idx_fem_line_supports_schema ON fem_line_supports(schema_id);
         """;

      /// <summary>Миграция v86: линейные связи на гранях пластинчатых КонЭ.</summary>
      void MigrateV86() => MigExec(FemLineSupportsDdl);

      /// <summary>Линейные связи схемы.</summary>
      public List<FemLineSupport> GetFemLineSupports(int schemaId)
      {
         var result = new List<FemLineSupport>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT id, member_tag, loop, hole_index, start_vertex, x1, y1, z1, x2, y2, z2,
                   mask, kx, ky, kz, kux, kuy, kuz, origin, fix_edge_rotation
            FROM fem_line_supports WHERE schema_id=@sid ORDER BY id
            """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var r = cmd.ExecuteReader();
         while (r.Read())
            result.Add(new FemLineSupport
            {
               Id = r.GetInt32(0), SchemaId = schemaId, MemberTag = r.GetString(1),
               Loop = r.GetString(2) == "hole" ? BoundaryLoop.Hole : BoundaryLoop.Outer,
               HoleIndex = r.GetInt32(3), StartVertex = r.GetInt32(4),
               X1 = r.GetDouble(5), Y1 = r.GetDouble(6), Z1 = r.GetDouble(7),
               X2 = r.GetDouble(8), Y2 = r.GetDouble(9), Z2 = r.GetDouble(10),
               Mask = r.GetInt32(11),
               Kx = r.GetDouble(12), Ky = r.GetDouble(13), Kz = r.GetDouble(14),
               Kux = r.GetDouble(15), Kuy = r.GetDouble(16), Kuz = r.GetDouble(17),
               Origin = r.GetString(18), FixEdgeRotation = r.GetInt32(19) != 0,
            });
         return result;
      }

      /// <summary>
      /// Полная замена линейных связей схемы одной транзакцией: объектам проставляются схема и новые Id (ловушка
      /// деструктивного сохранения — старые Id теряют смысл).
      /// </summary>
      public void ReplaceFemLineSupports(int schemaId, IReadOnlyList<FemLineSupport> supports)
      {
         using var tx = _connection.BeginTransaction();
         try
         {
            ReplaceFemLineSupportsCore(schemaId, supports);
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }

      /// <summary>Тело <see cref="ReplaceFemLineSupports"/> внутри внешней транзакции.</summary>
      void ReplaceFemLineSupportsCore(int schemaId, IReadOnlyList<FemLineSupport> supports)
      {
         using (var del = _connection.CreateCommand())
         {
            del.CommandText = "DELETE FROM fem_line_supports WHERE schema_id=@sid";
            del.Parameters.AddWithValue("@sid", schemaId);
            del.ExecuteNonQuery();
         }
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            INSERT INTO fem_line_supports (schema_id, member_tag, loop, hole_index, start_vertex,
                x1, y1, z1, x2, y2, z2, mask, kx, ky, kz, kux, kuy, kuz, origin, fix_edge_rotation)
            VALUES (@sid, @mt, @loop, @hole, @sv, @x1, @y1, @z1, @x2, @y2, @z2, @mask,
                @kx, @ky, @kz, @kux, @kuy, @kuz, @origin, @fer);
            SELECT last_insert_rowid();
            """;
         foreach (var s in supports)
         {
            if (!s.IsActive) continue;
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@sid", schemaId);
            cmd.Parameters.AddWithValue("@mt", s.MemberTag);
            cmd.Parameters.AddWithValue("@loop", s.Loop == BoundaryLoop.Hole ? "hole" : "outer");
            cmd.Parameters.AddWithValue("@hole", s.HoleIndex);
            cmd.Parameters.AddWithValue("@sv", s.StartVertex);
            cmd.Parameters.AddWithValue("@x1", s.X1);
            cmd.Parameters.AddWithValue("@y1", s.Y1);
            cmd.Parameters.AddWithValue("@z1", s.Z1);
            cmd.Parameters.AddWithValue("@x2", s.X2);
            cmd.Parameters.AddWithValue("@y2", s.Y2);
            cmd.Parameters.AddWithValue("@z2", s.Z2);
            cmd.Parameters.AddWithValue("@mask", s.Mask & FemBoundaryDofs.All);
            cmd.Parameters.AddWithValue("@kx", s.Kx);
            cmd.Parameters.AddWithValue("@ky", s.Ky);
            cmd.Parameters.AddWithValue("@kz", s.Kz);
            cmd.Parameters.AddWithValue("@kux", s.Kux);
            cmd.Parameters.AddWithValue("@kuy", s.Kuy);
            cmd.Parameters.AddWithValue("@kuz", s.Kuz);
            cmd.Parameters.AddWithValue("@origin", s.Origin);
            cmd.Parameters.AddWithValue("@fer", s.FixEdgeRotation ? 1 : 0);
            s.Id = (int)(long)cmd.ExecuteScalar()!;
            s.SchemaId = schemaId;
         }
      }

      /// <summary>
      /// Линейные связи схемы, разложенные на узлы сетки, — для расчёта, сводки ГУ и реакций по грани. Грани берутся из
      /// областей КонЭ схемы.
      /// </summary>
      public FemLineSupportNodal ResolveFemLineSupports(int schemaId, IReadOnlyList<FemMember> members,
         IReadOnlyList<FemMeshNode> meshNodes, IReadOnlyList<FemElement> meshElements,
         IReadOnlyList<PlanarRegion>? regions = null)
      {
         var supports = GetFemLineSupports(schemaId);
         if (supports.Count == 0) return FemLineSupportNodal.Empty;
         var edges = FemLineSupportGeometry.EdgesByMember(members, regions ?? GetPlanarRegions(schemaId));
         return FemLineSupportResolver.ToNodal(supports, edges, meshNodes, meshElements);
      }
   }
}
