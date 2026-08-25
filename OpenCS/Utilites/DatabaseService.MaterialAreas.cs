using CScore;
using Microsoft.Data.Sqlite;

namespace OpenCS.Utilites
{
   /// <summary>Часть DatabaseService, отвечающая за дочерние данные MaterialArea.</summary>
   public partial class DatabaseService
   {
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
               Id = r.GetInt32(0), Num = r.GetInt32(1), Tag = r.GetString(2),
               Description = r.IsDBNull(3) ? null : r.GetString(3),
               MaterialId = r.IsDBNull(4) ? 0 : r.GetInt32(4),
               HostAreaId = r.IsDBNull(5) ? null : r.GetInt32(5),
               DiagrammType = Enum.Parse<DiagrammType>(r.GetString(6)),
               NX = r.GetInt32(7), NY = r.GetInt32(8), WKT = r.IsDBNull(9) ? null : r.GetString(9),
               Category = Enum.TryParse<AreaCategory>(r.GetString(10), true, out var cat) ? cat : AreaCategory.RebarGroup,
               PoolContourId = r.IsDBNull(11) ? null : r.GetInt32(11),
               MeshMethod = Enum.TryParse<CScore.MeshMethod>(r.IsDBNull(12) ? "grid" : r.GetString(12), true, out var mm) ? mm : CScore.MeshMethod.Grid,
               MeshMaxArea = r.IsDBNull(13) ? 0.01 : r.GetDouble(13),
               MeshMinAngle = r.IsDBNull(14) ? 30.0 : r.GetDouble(14),
               MeshMaxEdgeLen = r.IsDBNull(15) ? 0.0 : r.GetDouble(15),
               MeshSmoothIter = r.IsDBNull(16) ? 5 : r.GetInt32(16),
               SigSp = r.IsDBNull(17) ? 0.0 : r.GetDouble(17),
               GammaSp = r.IsDBNull(18) ? 1.0 : r.GetDouble(18)
            };
            if (area.WKT != null)
            {
               WktHelper.ParseWKTPolygon(area.WKT, out var outerX, out var outerY, out var holeXs, out var holeYs);
               if (outerX.Count >= 5) area.Contours.Add(new Contour(outerX, outerY, "hull") { Type = ContourType.Hull });
               if (holeXs != null)
                  for (var j = 0; j < holeXs.Count; j++)
                     if (holeXs[j].Count >= 5) area.Contours.Add(new Contour(holeXs[j], holeYs[j], $"hole{j}") { Type = ContourType.Hole });
            }
            MaterialAreas.Add(area);
         }
         LoadPointFibersForAreas(MaterialAreas, conn);
         LoadMeshFibersForAreas(MaterialAreas, conn);
         LoadClosedStirrupsForAreas(MaterialAreas, conn);
      }

      void LoadPointFibersForAreas(IEnumerable<MaterialArea> areas, SqliteConnection conn)
      {
         var byArea = areas.ToDictionary(a => a.Id);
         if (byArea.Count == 0) return;
         using var cmd = conn.CreateCommand();
         cmd.CommandText = $"SELECT area_id, x, y, area, diameter, eps_p FROM point_fibers WHERE area_id IN ({string.Join(",", byArea.Keys)})";
         using var r = cmd.ExecuteReader();
         while (r.Read())
         {
            if (!byArea.TryGetValue(r.GetInt32(0), out var area)) continue;
            area.Fibers.Add(new Fiber(r.GetDouble(1), r.GetDouble(2)) { Area = r.GetDouble(3), Diameter = r.GetDouble(4), Eps_p = r.GetDouble(5), TypeFiber = FiberType.point });
         }
      }

      void LoadMeshFibersForAreas(IEnumerable<MaterialArea> areas, SqliteConnection conn)
      {
         var byArea = areas.ToDictionary(a => a.Id);
         if (byArea.Count == 0) return;
         using var cmd = conn.CreateCommand();
         cmd.CommandText = $"SELECT area_id, type, x, y, area, wkt, eps_p FROM mesh_fibers WHERE area_id IN ({string.Join(",", byArea.Keys)})";
         using var r = cmd.ExecuteReader();
         while (r.Read())
         {
            if (!byArea.TryGetValue(r.GetInt32(0), out var area)) continue;
            area.Fibers.Add(new Fiber(r.GetDouble(2), r.GetDouble(3))
            {
               TypeFiber = Enum.TryParse<FiberType>(r.GetString(1), out var ft) ? ft : FiberType.poly,
               Area = r.GetDouble(4), WKT = r.IsDBNull(5) ? null : r.GetString(5), Eps_p = r.GetDouble(6)
            });
         }
      }

      void LoadClosedStirrupsForAreas(IEnumerable<MaterialArea> areas, SqliteConnection conn)
      {
         var byArea = areas.ToDictionary(a => a.Id);
         if (byArea.Count == 0) return;
         var groups = new Dictionary<int, ClosedStirrupGroup>();
         using var cmd = conn.CreateCommand();
         cmd.CommandText = $"SELECT id,area_id,material_id,spacing_m FROM material_area_closed_stirrup_groups WHERE area_id IN ({string.Join(",", byArea.Keys)}) ORDER BY id";
         using var reader = cmd.ExecuteReader();
         while (reader.Read())
         {
            if (!byArea.TryGetValue(reader.GetInt32(1), out var area)) continue;
            var group = new ClosedStirrupGroup { Id = reader.GetInt32(0), MaterialId = reader.GetInt32(2), SpacingM = reader.GetDouble(3) };
            area.ClosedStirrups.Add(group); groups[group.Id] = group;
         }
         if (groups.Count == 0) return;
         using var loopCmd = conn.CreateCommand();
         loopCmd.CommandText = $"SELECT id,group_id,centerline_wkt,bar_area_m2,bar_diameter_m FROM material_area_closed_stirrup_loops WHERE group_id IN ({string.Join(",", groups.Keys)}) ORDER BY id";
         using var loopReader = loopCmd.ExecuteReader();
         while (loopReader.Read())
         {
            if (!groups.TryGetValue(loopReader.GetInt32(1), out var group)) continue;
            group.Loops.Add(new ClosedStirrupLoop
            {
               Id = loopReader.GetInt32(0),
               CenterlineContour = new Contour(loopReader.GetString(2), "stirrup"),
               BarAreaM2 = loopReader.GetDouble(3),
               BarDiameterM = loopReader.GetDouble(4)
            });
         }
      }

      /// <summary>Сохраняет сеточные волокна (poly/tri) области и параметры её разбиения.</summary>
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
         foreach (var fiber in area.Fibers.Where(f => f.TypeFiber is FiberType.poly or FiberType.tri))
         {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "INSERT INTO mesh_fibers(area_id,type,x,y,area,wkt,eps_p) VALUES(@aid,@t,@x,@y,@a,@wkt,@ep)";
            cmd.Parameters.AddWithValue("@aid", area.Id);
            cmd.Parameters.AddWithValue("@t", fiber.TypeFiber.ToString());
            cmd.Parameters.AddWithValue("@x", fiber.X);
            cmd.Parameters.AddWithValue("@y", fiber.Y);
            cmd.Parameters.AddWithValue("@a", fiber.Area);
            cmd.Parameters.AddWithValue("@wkt", (object?)fiber.WKT ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ep", fiber.Eps_p);
            cmd.ExecuteNonQuery();
         }
         tx.Commit();
      }

      /// <summary>Удаляет область и её волокна, включая замкнутые хомуты.</summary>
      public void DeleteMaterialArea(MaterialArea area)
      {
         if (area.Id == 0) { MaterialAreas.Remove(area); return; }
         using var tx = _connection.BeginTransaction();
         try
         {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
               DELETE FROM point_fibers   WHERE area_id=@id;
               DELETE FROM mesh_fibers    WHERE area_id=@id;
               DELETE FROM material_area_closed_stirrup_loops
               WHERE group_id IN (SELECT id FROM material_area_closed_stirrup_groups WHERE area_id=@id);
               DELETE FROM material_area_closed_stirrup_groups WHERE area_id=@id;
               DELETE FROM material_areas WHERE id=@id;
            """;
            cmd.Parameters.AddWithValue("@id", area.Id);
            cmd.ExecuteNonQuery();
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
         MaterialAreas.Remove(area);
      }

      public void SaveMaterialArea(MaterialArea area)
      {
         using var conn = new SqliteConnection($"Data Source={_dataSource}");
         conn.Open();
         using (var fkCmd = conn.CreateCommand())
         {
            fkCmd.CommandText = "PRAGMA foreign_keys=OFF";
            fkCmd.ExecuteNonQuery();
         }
         using var tx = conn.BeginTransaction();
         var isNew = area.Id == 0;
         using (var cmd = conn.CreateCommand())
         {
            cmd.CommandText = isNew ? """
               INSERT INTO material_areas (num,tag,description,material_id,host_area_id,diagramm_type,nx,ny,wkt,category,pool_contour_id,mesh_method,mesh_max_area,mesh_min_angle,mesh_max_edge_len,mesh_smooth_iter,sig_sp,gamma_sp)
               VALUES (@num,@tag,@desc,@mid,@hid,@dtype,@nx,@ny,@wkt,@cat,@pcid,@mmethod,@mmaxarea,@mminangle,@mmaxedge,@msmoothiter,@sigsp,@gammasp);
               SELECT last_insert_rowid();
            """ : """
               UPDATE material_areas SET num=@num,tag=@tag,description=@desc,material_id=@mid,host_area_id=@hid,diagramm_type=@dtype,nx=@nx,ny=@ny,wkt=@wkt,category=@cat,pool_contour_id=@pcid,mesh_method=@mmethod,mesh_max_area=@mmaxarea,mesh_min_angle=@mminangle,mesh_max_edge_len=@mmaxedge,mesh_smooth_iter=@msmoothiter,sig_sp=@sigsp,gamma_sp=@gammasp WHERE id=@id;
            """;
            if (!isNew) cmd.Parameters.AddWithValue("@id", area.Id);
            cmd.Parameters.AddWithValue("@num", area.Num); cmd.Parameters.AddWithValue("@tag", area.Tag); cmd.Parameters.AddWithValue("@desc", (object?)area.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@mid", area.MaterialId == 0 ? DBNull.Value : (object)area.MaterialId); cmd.Parameters.AddWithValue("@hid", (object?)area.HostAreaId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@dtype", area.DiagrammType.ToString()); cmd.Parameters.AddWithValue("@nx", area.NX); cmd.Parameters.AddWithValue("@ny", area.NY); cmd.Parameters.AddWithValue("@wkt", (object?)area.WKT ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@cat", area.Category.ToString().ToLowerInvariant()); cmd.Parameters.AddWithValue("@pcid", (object?)area.PoolContourId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@mmethod", area.MeshMethod.ToString().ToLowerInvariant()); cmd.Parameters.AddWithValue("@mmaxarea", area.MeshMaxArea); cmd.Parameters.AddWithValue("@mminangle", area.MeshMinAngle);
            cmd.Parameters.AddWithValue("@mmaxedge", area.MeshMaxEdgeLen); cmd.Parameters.AddWithValue("@msmoothiter", area.MeshSmoothIter); cmd.Parameters.AddWithValue("@sigsp", area.SigSp); cmd.Parameters.AddWithValue("@gammasp", area.GammaSp);
            if (isNew) area.Id = (int)(long)cmd.ExecuteScalar()!; else cmd.ExecuteNonQuery();
         }
         ReplacePointFibers(area, conn);
         ReplaceClosedStirrups(area, conn);
         tx.Commit();
         if (isNew && !MaterialAreas.Contains(area)) MaterialAreas.Add(area);
      }

      void ReplacePointFibers(MaterialArea area, SqliteConnection conn)
      {
         using (var cmd = conn.CreateCommand())
         {
            cmd.CommandText = "DELETE FROM point_fibers WHERE area_id=@aid";
            cmd.Parameters.AddWithValue("@aid", area.Id);
            cmd.ExecuteNonQuery();
         }
         foreach (var fiber in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
         {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO point_fibers(area_id,x,y,area,diameter,eps_p) VALUES(@aid,@x,@y,@a,@d,@ep)";
            cmd.Parameters.AddWithValue("@aid", area.Id); cmd.Parameters.AddWithValue("@x", fiber.X); cmd.Parameters.AddWithValue("@y", fiber.Y);
            cmd.Parameters.AddWithValue("@a", fiber.Area); cmd.Parameters.AddWithValue("@d", fiber.Diameter); cmd.Parameters.AddWithValue("@ep", fiber.Eps_p);
            cmd.ExecuteNonQuery();
         }
      }

      void ReplaceClosedStirrups(MaterialArea area, SqliteConnection conn)
      {
         using (var cmd = conn.CreateCommand())
         {
            cmd.CommandText = "DELETE FROM material_area_closed_stirrup_groups WHERE area_id=@id";
            cmd.Parameters.AddWithValue("@id", area.Id);
            cmd.ExecuteNonQuery();
         }
         foreach (var group in area.ClosedStirrups)
         {
            group.ValidateFor(area);
            using var groupCmd = conn.CreateCommand();
            groupCmd.CommandText = "INSERT INTO material_area_closed_stirrup_groups(area_id,material_id,spacing_m) VALUES(@a,@m,@s); SELECT last_insert_rowid();";
            groupCmd.Parameters.AddWithValue("@a", area.Id); groupCmd.Parameters.AddWithValue("@m", group.MaterialId); groupCmd.Parameters.AddWithValue("@s", group.SpacingM);
            group.Id = (int)(long)groupCmd.ExecuteScalar()!;
            foreach (var loop in group.Loops)
            {
               using var loopCmd = conn.CreateCommand();
               loopCmd.CommandText = "INSERT INTO material_area_closed_stirrup_loops(group_id,centerline_wkt,bar_area_m2,bar_diameter_m) VALUES(@g,@w,@a,@d); SELECT last_insert_rowid();";
               loopCmd.Parameters.AddWithValue("@g", group.Id); loopCmd.Parameters.AddWithValue("@w", loop.CenterlineContour.WKT);
               loopCmd.Parameters.AddWithValue("@a", loop.BarAreaM2); loopCmd.Parameters.AddWithValue("@d", loop.BarDiameterM);
               loop.Id = (int)(long)loopCmd.ExecuteScalar()!;
            }
         }
      }
   }
}
