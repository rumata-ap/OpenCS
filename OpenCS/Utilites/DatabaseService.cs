using CScore;
using CSmath;

using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

using System.Collections.ObjectModel;
using System.IO;

namespace OpenCS.Utilites
{
   /// <summary>
   /// Сервис работы с базой данных SQLite. Заменяет EF Core ApplicationContext.
   /// Управляет подключением к БД, создаёт таблицы и выполняет CRUD-операции
   /// через параметризованный SQL. Вложенные коллекции сериализуются в JSON-колонки.
   /// </summary>
   public class DatabaseService : IDisposable
   {
      private SqliteConnection _connection;
      private string _dataSource;
      private static readonly JsonSerializerSettings _jsonSettings = new()
      {
         NullValueHandling = NullValueHandling.Ignore,
         DefaultValueHandling = DefaultValueHandling.Ignore,
         ContractResolver = new DatabaseContractResolver()
      };

      public string DataSource => _dataSource;

      public ObservableCollection<Material> Materials { get; } = [];
      public ObservableCollection<MaterialChars> MaterialChars { get; } = [];
      public ObservableCollection<Contour> Contours { get; } = [];
      public ObservableCollection<StressPoint> Points { get; } = [];
      public ObservableCollection<CircleP> Circles { get; } = [];
      public ObservableCollection<Region> Regions { get; } = [];
      public ObservableCollection<FiberRegion> FiberRegions { get; } = [];
      public ObservableCollection<RCFiberRegion> RcFiberRegions { get; } = [];
      public ObservableCollection<Fiber> Fibers { get; } = [];
      public ObservableCollection<ReBar> Rebars { get; } = [];
      public ObservableCollection<ReBarLayer> RebarLayers { get; } = [];
      public ObservableCollection<ReBarGroup> RebarGroups { get; } = [];
      public ObservableCollection<Diagramm> Diagrams { get; } = [];

      public DatabaseService() : this("dbapp.db") { }

      public DatabaseService(string dataSource)
      {
         _dataSource = dataSource;
         _connection = new SqliteConnection($"Data Source={dataSource}");
         _connection.Open();
         EnsureCreated();
      }

      private void EnsureCreated()
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS materials (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                type INTEGER NOT NULL DEFAULT 0,
                tag TEXT NOT NULL DEFAULT '',
                description TEXT NOT NULL DEFAULT '',
                e REAL NOT NULL DEFAULT 0,
                chars_json TEXT NOT NULL DEFAULT '[]'
            );
            CREATE TABLE IF NOT EXISTS contours (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                tag TEXT NOT NULL DEFAULT '',
                wkt TEXT NOT NULL DEFAULT '',
                type INTEGER NOT NULL DEFAULT 0,
                geometry_set TEXT NULL,
                points_json TEXT NOT NULL DEFAULT '[]',
                regions_json TEXT NOT NULL DEFAULT '[]'
            );
            CREATE TABLE IF NOT EXISTS circles (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                tag TEXT NOT NULL DEFAULT '',
                x REAL NOT NULL DEFAULT 0,
                y REAL NOT NULL DEFAULT 0,
                diameter REAL NOT NULL DEFAULT 0,
                radius REAL NOT NULL DEFAULT 0,
                area REAL NOT NULL DEFAULT 0,
                type INTEGER NOT NULL DEFAULT 0,
                num INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS rc_fiber_regions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                tag TEXT NOT NULL DEFAULT '',
                data_json TEXT NOT NULL DEFAULT '{}'
            );
            CREATE TABLE IF NOT EXISTS diagrams (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                tag TEXT NOT NULL DEFAULT '',
                type INTEGER NOT NULL DEFAULT 0,
                material_type INTEGER NOT NULL DEFAULT 0,
                calc_type INTEGER NOT NULL DEFAULT 0,
                material_id INTEGER NOT NULL DEFAULT 0,
                spline_data_json TEXT NOT NULL DEFAULT '{}'
            );
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value_json TEXT NOT NULL DEFAULT '{}'
            );";
         cmd.ExecuteNonQuery();
      }

      public void ChangeDatabase(string dataSource)
      {
         _connection.Close();
         _connection.Dispose();
         _dataSource = dataSource;
         _connection = new SqliteConnection($"Data Source={dataSource}");
         _connection.Open();
         EnsureCreated();
      }

      public void SaveAs(string newPath)
      {
         SaveAll();
         _connection.Close();
         _connection.Dispose();
         File.Copy(_dataSource, newPath, overwrite: true);
         _dataSource = newPath;
         _connection = new SqliteConnection($"Data Source={newPath}");
         _connection.Open();
      }

      public void SaveAll()
      {
         foreach (var m in Materials) SaveMaterial(m);
         foreach (var c in Contours) SaveContour(c);
         foreach (var c in Circles) SaveCircle(c);
         foreach (var r in RcFiberRegions) SaveRCFiberRegion(r);
         foreach (var d in Diagrams) SaveDiagram(d);
      }

      internal void ClearCollections()
      {
         Materials.Clear();
         MaterialChars.Clear();
         Contours.Clear();
         Points.Clear();
         Circles.Clear();
         Regions.Clear();
         FiberRegions.Clear();
         RcFiberRegions.Clear();
         Fibers.Clear();
         Rebars.Clear();
         RebarLayers.Clear();
         RebarGroups.Clear();
         Diagrams.Clear();
      }

      #region Load

      /// <summary>
      /// Загружает все данные из базы данных в ObservableCollection-коллекции.
      /// Вызывать после создания сервиса, перед использованием данных.
      /// </summary>
      public void LoadAll()
      {
         LoadMaterials();
         LoadCircles();
         LoadContours();
         LoadRCFiberRegions();
         LoadDiagrams();
         ResolveReferences();
      }

      void LoadMaterials()
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT id, type, tag, description, e, chars_json FROM materials ORDER BY id";
         using var reader = cmd.ExecuteReader();
         while (reader.Read())
         {
            var m = new Material
            {
               Id = reader.GetInt32(0),
               Type = (MatType)reader.GetInt32(1),
               Tag = reader.GetString(2),
               Description = reader.GetString(3),
               E = reader.GetDouble(4)
            };
            var charsJson = reader.GetString(5);
            var chars = JsonConvert.DeserializeObject<List<MaterialChars>>(charsJson, _jsonSettings);
            if (chars != null && chars.Count == 4)
               m.MaterialChars = chars;
            Materials.Add(m);
            foreach (var c in m.MaterialChars)
               MaterialChars.Add(c);
         }
      }

      void LoadCircles()
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT id, tag, x, y, diameter, radius, area, type, num FROM circles ORDER BY id";
         using var reader = cmd.ExecuteReader();
         while (reader.Read())
         {
            Circles.Add(new CircleP
            {
               Id = reader.GetInt32(0),
               Tag = reader.GetString(1),
               X = reader.GetDouble(2),
               Y = reader.GetDouble(3),
               Diameter = reader.GetDouble(4),
               Radius = reader.GetDouble(5),
               Area = reader.GetDouble(6),
               Type = (PointType)reader.GetInt32(7),
               Num = reader.GetInt32(8)
            });
         }
      }

      void LoadContours()
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT id, tag, wkt, type, geometry_set, points_json FROM contours ORDER BY id";
         using var reader = cmd.ExecuteReader();
         while (reader.Read())
         {
            var c = new Contour
            {
               Id = reader.GetInt32(0),
               Tag = reader.GetString(1)
            };
            c.WKT = reader.GetString(2);
            c.Type = (ContourType)reader.GetInt32(3);
            if (!reader.IsDBNull(4)) c.GeometrySet = reader.GetString(4);
            var pointsJson = reader.GetString(5);
            var points = JsonConvert.DeserializeObject<List<StressPoint>>(pointsJson, _jsonSettings);
            if (points != null)
               foreach (var p in points) { p.Contour = c; c.Points.Add(p); }
            Contours.Add(c);
            foreach (var p in c.Points) Points.Add(p);
         }
      }

      void LoadRCFiberRegions()
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT id, tag, data_json FROM rc_fiber_regions ORDER BY id";
         using var reader = cmd.ExecuteReader();
         while (reader.Read())
         {
            var id = reader.GetInt32(0);
            var tag = reader.GetString(1);
            var dataJson = reader.GetString(2);
            var data = JsonConvert.DeserializeObject<RcfrData>(dataJson, _jsonSettings);
            if (data == null) continue;

            var r = new RCFiberRegion
            {
               Id = id,
               Tag = tag,
               WKT = data.Wkt ?? "",
               H = data.H,
               NX = data.Nx,
               NY = data.Ny,
               Atr = data.Atr,
               Antr = data.Antr
            };

            // Загрузка арматурных групп
            if (data.RebarGroups != null)
            {
               foreach (var rgData in data.RebarGroups)
               {
                  var rg = new ReBarGroup
                  {
                     Tag = rgData.Tag ?? "",
                     Type = rgData.Type
                  };
                  if (rgData.Rebars != null)
                  {
                     foreach (var rbData in rgData.Rebars)
                     {
                        if (rbData.Discriminator == "ReBarLayer")
                        {
                           var rb = new ReBarLayer
                           {
                              X = rbData.X, Y = rbData.Y,
                              E = rbData.E, E2 = rbData.E2,
                              Sig = rbData.Sig, Eps = rbData.Eps, Eps_p = rbData.Eps_p,
                              Nu1 = rbData.Nu1, Nu2 = rbData.Nu2,
                              N = rbData.N, Area = rbData.Area,
                              My = rbData.My, Mz = rbData.Mz,
                              Tag = rbData.Tag,
                              Diameter = rbData.Diameter,
                              Nd = rbData.Nd,
                              Pos = rbData.Pos,
                              As = rbData.As,
                              Num = rbData.Num
                           };
                           rg.ReBars.Add(rb);
                           RebarLayers.Add(rb);
                           Rebars.Add(rb);
                        }
                        else
                        {
                           var rb = new ReBar
                           {
                              X = rbData.X, Y = rbData.Y,
                              E = rbData.E, E2 = rbData.E2,
                              Sig = rbData.Sig, Eps = rbData.Eps, Eps_p = rbData.Eps_p,
                              Nu1 = rbData.Nu1, Nu2 = rbData.Nu2,
                              N = rbData.N, Area = rbData.Area,
                              My = rbData.My, Mz = rbData.Mz,
                              Tag = rbData.Tag,
                              Diameter = rbData.Diameter,
                              Num = rbData.Num
                           };
                           rg.ReBars.Add(rb);
                           Rebars.Add(rb);
                        }
                     }
                  }
                  r.ReBarGroups.Add(rg);
                  RebarGroups.Add(rg);
               }
            }

            RcFiberRegions.Add(r);
            Regions.Add(r);
            FiberRegions.Add(r);
         }
      }

      /// <summary>
      /// Разрешает навигационные ссылки между объектами после загрузки.
      /// Связывает Material с MaterialChars, Region с Contour и Material и т.д.
      /// </summary>
      void ResolveReferences()
      {
         // Material ← MaterialChars уже связаны через Material.MaterialChars setter

         // Разрешаем Material для ReBarGroup
         // (загрузка material_id из JSON данных будет в следующей итерации)

         // Разрешаем Contour для Region (связь M:N через regions_json)
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT id, regions_json FROM contours WHERE regions_json != '[]'";
         using var reader = cmd.ExecuteReader();
         while (reader.Read())
         {
            var contourId = reader.GetInt32(0);
            var regionsJson = reader.GetString(1);
            var regionIds = JsonConvert.DeserializeObject<List<int>>(regionsJson);
            var contour = Contours.FirstOrDefault(c => c.Id == contourId);
            if (contour == null || regionIds == null) continue;

            foreach (var rid in regionIds)
            {
               var region = Regions.FirstOrDefault(r => r.Id == rid);
               if (region != null)
               {
                  contour.Regions.Add(region);
                  region.Contours.Add(contour);
               }
            }
         }
      }

      void LoadDiagrams()
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT id, tag, type, material_type, calc_type, material_id, spline_data_json FROM diagrams ORDER BY id";
         using var reader = cmd.ExecuteReader();
         while (reader.Read())
         {
            var id = reader.GetInt32(0);
            var tag = reader.GetString(1);
            var type = (DiagrammType)reader.GetInt32(2);
            var matType = (MatType)reader.GetInt32(3);
            var calcType = (CalcType)reader.GetInt32(4);
            var materialId = reader.GetInt32(5);
            var splineJson = reader.GetString(6);
            var sd = JsonConvert.DeserializeObject<SplineDataJson>(splineJson);
            var d = new Diagramm
            {
               Id = id,
               Tag = tag,
               Type = type,
               MaterialType = matType,
               CalcType = calcType,
               MaterialId = materialId,
               Ic = RebuildSpline(sd?.Compression),
               It = RebuildSpline(sd?.Tension)
            };
            Diagrams.Add(d);
         }
      }

      static ISpline? RebuildSpline(SplineBranchJson? branch)
      {
         if (branch?.X == null || branch.Y == null) return null;
         return branch.SplineType == "HSpline" && branch.DY != null
            ? new HSpline(branch.X, branch.Y, branch.DY)
            : new LSpline(branch.X, branch.Y) as ISpline;
      }

      #endregion

      #region Save

      public void SaveDiagram(Diagramm d)
      {
         var sd = new SplineDataJson
         {
            Compression = ExtractSpline(d.Ic),
            Tension = ExtractSpline(d.It)
         };
         var splineJson = JsonConvert.SerializeObject(sd, _jsonSettings);
         var cmd = _connection.CreateCommand();
         if (d.Id == 0)
         {
            cmd.CommandText = @"INSERT INTO diagrams (tag, type, material_type, calc_type, material_id, spline_data_json)
                                VALUES ($tag, $type, $mt, $ct, $mid, $spl);
                                SELECT last_insert_rowid();";
         }
         else
         {
            cmd.CommandText = @"UPDATE diagrams SET tag=$tag, type=$type, material_type=$mt,
                                calc_type=$ct, material_id=$mid, spline_data_json=$spl WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", d.Id);
         }
         cmd.Parameters.AddWithValue("$tag", d.Tag ?? "");
         cmd.Parameters.AddWithValue("$type", (int)d.Type);
         cmd.Parameters.AddWithValue("$mt", (int)d.MaterialType);
         cmd.Parameters.AddWithValue("$ct", (int)d.CalcType);
         cmd.Parameters.AddWithValue("$mid", d.MaterialId);
         cmd.Parameters.AddWithValue("$spl", splineJson);
         if (d.Id == 0)
            d.Id = Convert.ToInt32(cmd.ExecuteScalar());
         else
            cmd.ExecuteNonQuery();
      }

      public void DeleteDiagram(Diagramm d)
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "DELETE FROM diagrams WHERE id=$id";
         cmd.Parameters.AddWithValue("$id", d.Id);
         cmd.ExecuteNonQuery();
      }

      static SplineBranchJson? ExtractSpline(ISpline? spline)
      {
         if (spline == null) return null;
         return new SplineBranchJson
         {
            SplineType = spline is HSpline ? "HSpline" : "LSpline",
            X = spline.X,
            Y = spline.Y,
            DY = spline.DY
         };
      }

      public void SaveMaterial(Material m)
      {
         var cmd = _connection.CreateCommand();
         if (m.Id == 0)
         {
            cmd.CommandText = @"INSERT INTO materials (type, tag, description, e, chars_json)
                               VALUES ($type, $tag, $desc, $e, $chars);
                               SELECT last_insert_rowid();";
         }
         else
         {
            cmd.CommandText = @"UPDATE materials SET type=$type, tag=$tag, description=$desc, e=$e, chars_json=$chars
                               WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", m.Id);
         }
         cmd.Parameters.AddWithValue("$type", (int)m.Type);
         cmd.Parameters.AddWithValue("$tag", m.Tag ?? "");
         cmd.Parameters.AddWithValue("$desc", m.Description ?? "");
         cmd.Parameters.AddWithValue("$e", m.E);
         cmd.Parameters.AddWithValue("$chars", JsonConvert.SerializeObject(m.MaterialChars, _jsonSettings));
         if (m.Id == 0)
            m.Id = Convert.ToInt32(cmd.ExecuteScalar());
         else
            cmd.ExecuteNonQuery();
      }

      public void DeleteMaterial(Material m)
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "DELETE FROM materials WHERE id=$id";
         cmd.Parameters.AddWithValue("$id", m.Id);
         cmd.ExecuteNonQuery();
      }

      public void SaveContour(Contour c)
      {
         var cmd = _connection.CreateCommand();
         var pointsJson = JsonConvert.SerializeObject(c.Points.ToList(), _jsonSettings);
         var regionIds = c.Regions.Select(r => r.Id).ToList();
         var regionsJson = JsonConvert.SerializeObject(regionIds);

         if (c.Id == 0)
         {
            cmd.CommandText = @"INSERT INTO contours (tag, wkt, type, geometry_set, points_json, regions_json)
                               VALUES ($tag, $wkt, $type, $gset, $pjson, $rjson);
                               SELECT last_insert_rowid();";
         }
         else
         {
            cmd.CommandText = @"UPDATE contours SET tag=$tag, wkt=$wkt, type=$type, geometry_set=$gset,
                               points_json=$pjson, regions_json=$rjson WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", c.Id);
         }
         cmd.Parameters.AddWithValue("$tag", c.Tag ?? "");
         cmd.Parameters.AddWithValue("$wkt", c.WKT ?? "");
         cmd.Parameters.AddWithValue("$type", (int)c.Type);
         cmd.Parameters.AddWithValue("$gset", (object?)c.GeometrySet ?? DBNull.Value);
         cmd.Parameters.AddWithValue("$pjson", pointsJson);
         cmd.Parameters.AddWithValue("$rjson", regionsJson);
         if (c.Id == 0)
            c.Id = Convert.ToInt32(cmd.ExecuteScalar());
         else
            cmd.ExecuteNonQuery();
      }

      public void DeleteContour(Contour c)
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "DELETE FROM contours WHERE id=$id";
         cmd.Parameters.AddWithValue("$id", c.Id);
         cmd.ExecuteNonQuery();
      }

      public void SaveCircle(CircleP c)
      {
         var cmd = _connection.CreateCommand();
         if (c.Id == 0)
         {
            cmd.CommandText = @"INSERT INTO circles (tag, x, y, diameter, radius, area, type, num)
                               VALUES ($tag, $x, $y, $dia, $rad, $area, $type, $num);
                               SELECT last_insert_rowid();";
         }
         else
         {
            cmd.CommandText = @"UPDATE circles SET tag=$tag, x=$x, y=$y, diameter=$dia, radius=$rad,
                               area=$area, type=$type, num=$num WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", c.Id);
         }
         cmd.Parameters.AddWithValue("$tag", c.Tag ?? "");
         cmd.Parameters.AddWithValue("$x", c.X);
         cmd.Parameters.AddWithValue("$y", c.Y);
         cmd.Parameters.AddWithValue("$dia", c.Diameter);
         cmd.Parameters.AddWithValue("$rad", c.Radius);
         cmd.Parameters.AddWithValue("$area", c.Area);
         cmd.Parameters.AddWithValue("$type", (int)c.Type);
         cmd.Parameters.AddWithValue("$num", c.Num);
         if (c.Id == 0)
            c.Id = Convert.ToInt32(cmd.ExecuteScalar());
         else
            cmd.ExecuteNonQuery();
      }

      public void DeleteCircle(CircleP c)
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "DELETE FROM circles WHERE id=$id";
         cmd.Parameters.AddWithValue("$id", c.Id);
         cmd.ExecuteNonQuery();
      }

      public void SaveRCFiberRegion(RCFiberRegion r)
      {
         var data = new RcfrData
         {
            Wkt = r.WKT,
            H = r.H,
            Nx = r.NX,
            Ny = r.NY,
            Atr = r.Atr,
            Antr = r.Antr,
            MaterialId = r.Material?.Id,
            RebarGroups = r.ReBarGroups.Select(rg => new RebarGroupData
            {
               Tag = rg.Tag,
               Type = rg.Type,
               MaterialId = rg.Material?.Id,
               Rebars = rg.ReBars.Select(rb => new RebarData
               {
                  Discriminator = rb is ReBarLayer ? "ReBarLayer" : "ReBar",
                  X = rb.X, Y = rb.Y,
                  E = rb.E, E2 = rb.E2,
                  Sig = rb.Sig, Eps = rb.Eps, Eps_p = rb.Eps_p,
                  Nu1 = rb.Nu1, Nu2 = rb.Nu2,
                  N = rb.N, Area = rb.Area,
                  My = rb.My, Mz = rb.Mz,
                  Tag = rb.Tag,
                  Diameter = rb.Diameter,
                  Num = rb.Num,
                  Nd = rb is ReBarLayer rl ? rl.Nd : 0,
                  Pos = rb is ReBarLayer rl2 ? rl2.Pos : ReBarLayerPos.Left,
                  As = rb is ReBarLayer rl3 ? rl3.As : 0
               }).ToList()
            }).ToList(),
            Fibers = r.Fibers?.Select(f => new FiberData
            {
               X = f.X, Y = f.Y,
               E = f.E, E2 = f.E2,
               Sig = f.Sig, Eps = f.Eps, Eps_p = f.Eps_p,
               Nu1 = f.Nu1, Nu2 = f.Nu2,
               Tag = f.Tag,
               N = f.N, Area = f.Area,
               My = f.My, Mz = f.Mz,
               Wkt = f.WKT,
               TypeFiber = f.TypeFiber,
               Num = f.Num
            }).ToList()
         };

         var dataJson = JsonConvert.SerializeObject(data, _jsonSettings);
         var cmd = _connection.CreateCommand();
         if (r.Id == 0)
         {
            cmd.CommandText = @"INSERT INTO rc_fiber_regions (tag, data_json)
                               VALUES ($tag, $data);
                               SELECT last_insert_rowid();";
         }
         else
         {
            cmd.CommandText = @"UPDATE rc_fiber_regions SET tag=$tag, data_json=$data WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", r.Id);
         }
         cmd.Parameters.AddWithValue("$tag", r.Tag ?? "");
         cmd.Parameters.AddWithValue("$data", dataJson);
         if (r.Id == 0)
            r.Id = Convert.ToInt32(cmd.ExecuteScalar());
         else
            cmd.ExecuteNonQuery();
      }

      public void DeleteRCFiberRegion(RCFiberRegion r)
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "DELETE FROM rc_fiber_regions WHERE id=$id";
         cmd.Parameters.AddWithValue("$id", r.Id);
         cmd.ExecuteNonQuery();
      }

      #endregion

      #region Convenience methods

      public void AddMaterial(Material m) { Materials.Add(m); MaterialChars.Clear(); SaveMaterial(m); foreach (var c in m.MaterialChars) MaterialChars.Add(c); }
      public void AddContour(Contour c) { Contours.Add(c); foreach (var p in c.Points) Points.Add(p); SaveContour(c); }
      public void AddCircle(CircleP c) { Circles.Add(c); SaveCircle(c); }
      public void AddRange(IEnumerable<CircleP> circles) { foreach (var c in circles) AddCircle(c); }
      public void AddRange(IEnumerable<Contour> contours) { foreach (var c in contours) AddContour(c); }

      /// <summary>
      /// Добавляет арматурный стержень (или слой) и сохраняет родительский RCFiberRegion.
      /// </summary>
      public void AddRebar(ReBar rb, RCFiberRegion parent)
      {
         Rebars.Add(rb);
         if (rb is ReBarLayer rl) RebarLayers.Add(rl);
         SaveRCFiberRegion(parent);
      }

      /// <summary>
      /// Добавляет список арматурных стержней и сохраняет родительский RCFiberRegion.
      /// </summary>
      public void AddRebars(IEnumerable<ReBar> rebars, RCFiberRegion parent)
      {
         foreach (var rb in rebars)
         {
            Rebars.Add(rb);
            if (rb is ReBarLayer rl) RebarLayers.Add(rl);
         }
         SaveRCFiberRegion(parent);
      }

      /// <summary>
      /// Удаляет волокна из региона и сохраняет родительский RCFiberRegion.
      /// </summary>
      public void RemoveFibers(IEnumerable<Fiber> fibers, RCFiberRegion parent)
      {
         foreach (var f in fibers)
            Fibers.Remove(f);
         SaveRCFiberRegion(parent);
      }

      #endregion

      #region DTO classes for JSON serialization

      class RcfrData
      {
         public string? Wkt { get; set; }
         public double H { get; set; }
         public int Nx { get; set; } = 21;
         public int Ny { get; set; } = 21;
         public double Atr { get; set; } = 0.25;
         public double Antr { get; set; } = 25;
         public int? MaterialId { get; set; }
         public List<RebarGroupData>? RebarGroups { get; set; }
         public List<FiberData>? Fibers { get; set; }
      }

      class RebarGroupData
      {
         public string? Tag { get; set; }
         public RegionType Type { get; set; }
         public int? MaterialId { get; set; }
         public List<RebarData>? Rebars { get; set; }
      }

      class RebarData
      {
         public string Discriminator { get; set; } = "ReBar";
         public double X { get; set; }
         public double Y { get; set; }
         public double E { get; set; }
         public double E2 { get; set; }
         public double Sig { get; set; }
         public double Eps { get; set; }
         public double Eps_p { get; set; }
         public double Nu1 { get; set; } = 1;
         public double Nu2 { get; set; } = 1;
         public string? Tag { get; set; }
         public double N { get; set; }
         public double Area { get; set; }
         public double My { get; set; }
         public double Mz { get; set; }
         public double Diameter { get; set; }
         public int Num { get; set; }
         public double Nd { get; set; }
         public ReBarLayerPos Pos { get; set; }
         public double As { get; set; }
      }

      class FiberData
      {
         public double X { get; set; }
         public double Y { get; set; }
         public double E { get; set; }
         public double E2 { get; set; }
         public double Sig { get; set; }
         public double Eps { get; set; }
         public double Eps_p { get; set; }
         public double Nu1 { get; set; } = 1;
         public double Nu2 { get; set; } = 1;
         public string? Tag { get; set; }
         public double N { get; set; }
         public double Area { get; set; }
         public double My { get; set; }
         public double Mz { get; set; }
         public string? Wkt { get; set; }
         public FiberType TypeFiber { get; set; }
         public int Num { get; set; }
      }

      class SplineBranchJson
      {
         public string SplineType { get; set; } = "LSpline";
         public double[]? X { get; set; }
         public double[]? Y { get; set; }
         public double[]? DY { get; set; }
      }

      class SplineDataJson
      {
         public SplineBranchJson? Compression { get; set; }
         public SplineBranchJson? Tension { get; set; }
      }

      /// <summary>
      /// Контракт сериализации: включает Id даже для [JsonIgnore] свойств.
      /// </summary>
      class DatabaseContractResolver : DefaultContractResolver
      {
         protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
         {
            var props = base.CreateProperties(type, memberSerialization);
            foreach (var prop in props)
            {
               if (prop.PropertyName == "Id")
                  prop.Ignored = false;
            }
            return props;
         }
      }

      #endregion

      #region Settings

      public CsvExportSettings LoadCsvSettings()
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT value_json FROM settings WHERE key='csv'";
         var json = cmd.ExecuteScalar() as string;
         if (json == null) return CsvExportSettings.Default;
         return JsonConvert.DeserializeObject<CsvExportSettings>(json) ?? CsvExportSettings.Default;
      }

      public void SaveCsvSettings(CsvExportSettings s)
      {
         var json = JsonConvert.SerializeObject(s);
         var cmd = _connection.CreateCommand();
         cmd.CommandText = @"INSERT OR REPLACE INTO settings (key, value_json)
                             VALUES ('csv', $json)";
         cmd.Parameters.AddWithValue("$json", json);
         cmd.ExecuteNonQuery();
      }

      public PlotSettings LoadPlotSettings()
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT value_json FROM settings WHERE key='plot'";
         var json = cmd.ExecuteScalar() as string;
         if (json == null) return PlotSettings.Default;
         return JsonConvert.DeserializeObject<PlotSettings>(json) ?? PlotSettings.Default;
      }

      public void SavePlotSettings(PlotSettings s)
      {
         var json = JsonConvert.SerializeObject(s);
         var cmd = _connection.CreateCommand();
         cmd.CommandText = @"INSERT OR REPLACE INTO settings (key, value_json)
                             VALUES ('plot', $json)";
         cmd.Parameters.AddWithValue("$json", json);
         cmd.ExecuteNonQuery();
      }

      #endregion

      public void Dispose()
      {
         _connection.Close();
         _connection.Dispose();
      }
   }
}