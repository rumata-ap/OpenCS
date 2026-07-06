using CScore;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace OpenCS.Services
{
   /// <summary>Прямой импорт геометрии из запущенного AutoCAD через COM (позднее связывание).</summary>
   public sealed class AcadImporter : IDisposable
   {
      const string ProgId = "AutoCAD.Application";
      const double Eps = 1e-8;

      readonly double _scale;
      readonly bool _arcChordMode;
      readonly double _arcChordLength;
      readonly int _arcSegments;
      dynamic? _acadApp;
      bool _disposed;

      public AcadImporter(double scaleFactor = 0.001,
                          bool arcChordMode = true,
                          double arcChordLength = 0.01,
                          int arcSegments = 16)
      {
         _scale = scaleFactor;
         _arcChordMode = arcChordMode;
         _arcChordLength = arcChordLength;
         _arcSegments = Math.Max(3, arcSegments);
      }

      // ── ROT (Running Object Table) ──────────────────────────────────────────

      [DllImport("ole32.dll", PreserveSig = true, CharSet = CharSet.Unicode)]
      static extern int CLSIDFromProgID(string lpszProgID, out Guid pclsid);

      [DllImport("ole32.dll", PreserveSig = true)]
      static extern int CreateBindCtx(uint reserved, out IBindCtx ppbc);

      static object GetActiveObjectFromRot(string progId)
      {
         int hr = CLSIDFromProgID(progId, out Guid clsid);
         if (hr != 0)
            throw new InvalidOperationException($"CLSID для {progId} не найден (HRESULT: 0x{hr:X8})");

         IBindCtx? ctx = null;
         IRunningObjectTable? rot = null;
         IEnumMoniker? monikers = null;

         try
         {
            hr = CreateBindCtx(0, out ctx);
            if (hr != 0 || ctx == null)
               throw new InvalidOperationException($"CreateBindCtx не удался (HRESULT: 0x{hr:X8})");

            ctx.GetRunningObjectTable(out rot);
            if (rot == null)
               throw new InvalidOperationException("GetRunningObjectTable вернул null");

            rot.EnumRunning(out monikers);
            if (monikers == null)
               throw new InvalidOperationException("EnumRunning вернул null");

            var moniker = new IMoniker[1];

            while (monikers.Next(1, moniker, IntPtr.Zero) == 0)
            {
                moniker[0].GetDisplayName(ctx, null, out string displayName);
                if (displayName.Contains(progId, StringComparison.OrdinalIgnoreCase) ||
                    displayName.Contains(clsid.ToString("B"), StringComparison.OrdinalIgnoreCase))
                {
                    rot.GetObject(moniker[0], out object obj);
                    return obj;
                }
            }

            throw new InvalidOperationException(
               "AutoCAD не запущен. Запустите AutoCAD и повторите попытку.");
         }
         finally
         {
            if (monikers != null) Marshal.ReleaseComObject(monikers);
            if (rot != null) Marshal.ReleaseComObject(rot);
            if (ctx != null) Marshal.ReleaseComObject(ctx);
         }
      }

      public void Connect()
      {
         _acadApp = GetActiveObjectFromRot(ProgId);
      }

      /// <summary>
      /// Импортирует замкнутые полилинии как MaterialArea с авто-распознаванием
      /// внешних контуров (Hull) и отверстий (Hole).
      /// </summary>
      /// <returns>Кортеж: (области, все созданные контуры).</returns>
      public (List<MaterialArea> regions, List<Contour> contours) ImportRegions(string? layerFilter = null)
      {
         EnsureConnected();
         var doc = _acadApp!.ActiveDocument;
         var ms = doc.ModelSpace;

         string docName = doc.Name;
         string geoSet = System.IO.Path.GetFileNameWithoutExtension((string)docName);

          var polylines = CollectPolylines(ms, layerFilter);
          if (polylines.Count == 0)
             return ([], []);

          // Собираем замкнутые полилинии с декомпозицией дуг
          var polyData = new List<(double[] coords, string layer, double area)>();
          foreach (dynamic pl in polylines)
          {
             bool closed = (bool)(pl.Closed ?? false);
             var exploded = ExplodePolyline(pl);
             int n = exploded.Count / 2;
             if (n < 3) continue;

             // У ExplodePolyline последняя точка для closed ≈ первой (с точностью аппроксимации)
             bool firstEqualsLast =
                Math.Abs(exploded[0] - exploded[2 * (n - 1)]) < Eps &&
                Math.Abs(exploded[1] - exploded[2 * (n - 1) + 1]) < Eps;

             if (!closed && !firstEqualsLast) continue;

             double[] coords = exploded.ToArray();
             double area = Math.Abs((double)(pl.Area ?? 0.0));
             polyData.Add((coords, (string)pl.Layer, area));
          }

         if (polyData.Count == 0)
            return ([], []);

         // Сортируем по убыванию площади → большой контур = Hull
         polyData.Sort((a, b) => b.area.CompareTo(a.area));

         // Вычисляем центроид для каждой полилинии для проверки вхождения
         static (double cx, double cy) Centroid(double[] coords, double scale)
         {
            int n = coords.Length / 2;
            double sumX = 0, sumY = 0;
            for (int i = 0; i < n; i++)
            {
               sumX += coords[2 * i];
               sumY += coords[2 * i + 1];
            }
            return (sumX / n * scale, sumY / n * scale);
         }

         var hulls = new List<MaterialArea>();
         var holes = new List<(int hullIdx, double[] coords, string layer)>();

         foreach (var (coords, layer, _) in polyData)
         {
            var (cx, cy) = Centroid(coords, _scale);

            int parentIdx = -1;
            for (int h = 0; h < hulls.Count; h++)
            {
               string? wkt = hulls[h].WKT;
               if (wkt != null && WktHelper.PointInPolygon(wkt, cx, cy))
               {
                  parentIdx = h;
                  break;
               }
            }

            if (parentIdx >= 0)
               holes.Add((parentIdx, coords, layer));
            else
               hulls.Add(CreateRegionFromCoords(coords, layer, geoSet));
         }

         var allContours = new List<Contour>();

         foreach (var (hIdx, coords, layer) in holes)
         {
            var hole = CreateContourFromCoords(coords, layer, _scale, geoSet);
            hole.Type = ContourType.Hole;
            hulls[hIdx].Contours.Add(hole);
            allContours.Add(hole);
         }

         foreach (var ha in hulls)
         {
            ha.SetWKT();
            var hullContour = ha.Contours.FirstOrDefault(c => c.Type == ContourType.Hull);
            if (hullContour != null)
               allContours.Add(hullContour);
         }

         return (hulls, allContours);
      }

      /// <summary>
      /// Импортирует окружности, сгруппированные по слоям.
      /// Каждая группа → MaterialArea с категорией RebarGroup.
      /// </summary>
      /// <returns>Кортеж: (группы фибр по слоям, все окружности как CircleP).</returns>
      public (Dictionary<string, List<Fiber>> circleGroups, List<CircleP> circles) ImportCirclesByLayer(string? layerFilter = null)
      {
         EnsureConnected();
         var doc = _acadApp!.ActiveDocument;
         var ms = doc.ModelSpace;

         string docName = doc.Name;
         string geoSet = System.IO.Path.GetFileNameWithoutExtension((string)docName);

         int count = ms.Count;

         var groups = new Dictionary<string, List<Fiber>>();
         var allCircles = new List<CircleP>();

         for (int i = 0; i < count; i++)
         {
            dynamic obj = ms.Item(i);
            string objName = obj.ObjectName;
            if (objName != "AcDbCircle") continue;
            if (!PassesLayerFilter((string)obj.Layer, layerFilter)) continue;

            double[] center = (double[])obj.Center;
            double r = (double)obj.Radius;
            string layer = (string)obj.Layer;

            double d = 2.0 * r * _scale;
            if (d < 1e-10) continue;
            double x = center[0] * _scale;
            double y = center[1] * _scale;
            var fiber = Fiber.CreatePoint(d, x, y);
            if (!groups.TryGetValue(layer, out var list))
               groups[layer] = list = [];
            list.Add(fiber);

            var cp = new CircleP(x, y, r * _scale)
            {
               Tag = layer,
               GeometrySet = geoSet,
            };
            allCircles.Add(cp);
         }

         return (groups, allCircles);
      }

      static List<dynamic> CollectPolylines(dynamic modelSpace, string? layerFilter)
      {
         var result = new List<dynamic>();
         int count = modelSpace.Count;
         for (int i = 0; i < count; i++)
         {
            dynamic obj = modelSpace.Item(i);
            string objName = obj.ObjectName;
            if (objName != "AcDbPolyline") continue;

            if (!PassesLayerFilter((string)obj.Layer, layerFilter)) continue;
            result.Add(obj);
         }
         return result;
      }

      static bool PassesLayerFilter(string layer, string? filter)
      {
         if (string.IsNullOrEmpty(filter) || filter == "#") return true;
         return string.Equals(layer, filter, StringComparison.OrdinalIgnoreCase);
      }

      // ── Arc approximation ──────────────────────────────────────────────────

      /// <summary>Декомпозиция полилинии: прямые сегменты + аппроксимация дуг (bulge).
      /// Для замкнутой полилинии обрабатывается и замыкающий сегмент.</summary>
      List<double> ExplodePolyline(dynamic pl)
      {
         var result = new List<double>();
         double[] coords = (double[])pl.Coordinates;
         int n = coords.Length / 2;
         if (n < 2) return result;

         bool closed = (bool)(pl.Closed ?? false);
         int segCount = closed ? n : n - 1;

         result.Add(coords[0]);
         result.Add(coords[1]);

         for (int i = 0; i < segCount; i++)
         {
            int i1 = i;
            int i2 = i + 1 < n ? i + 1 : 0;
            double x1 = coords[2 * i1], y1 = coords[2 * i1 + 1];
            double x2 = coords[2 * i2], y2 = coords[2 * i2 + 1];
            double bulge = (double)pl.GetBulge(i);

            if (Math.Abs(bulge) < Eps)
            {
               result.Add(x2);
               result.Add(y2);
            }
            else
            {
               var pts = ArcPoints(x1, y1, x2, y2, bulge);
               for (int j = 1; j < pts.Count; j++)
               {
                  result.Add(pts[j].x);
                  result.Add(pts[j].y);
               }
            }
         }

         return result;
      }

      /// <summary>Аппроксимирует дугу отрезками по настройкам дискретизации.</summary>
      List<(double x, double y)> ArcPoints(double x1, double y1, double x2, double y2, double bulge)
      {
         double chord = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
         if (chord < Eps) return [(x1, y1), (x2, y2)];

         double theta = 4.0 * Math.Atan(bulge);
         double radius = chord / (2.0 * Math.Sin(theta / 2.0));
         double midX = (x1 + x2) / 2.0, midY = (y1 + y2) / 2.0;
         double chordLen = chord;
         double perpX = -(y2 - y1) / chordLen, perpY = (x2 - x1) / chordLen;
         double d = radius * Math.Cos(theta / 2.0);
         double cx = midX + d * perpX, cy = midY + d * perpY;

         double a1 = Math.Atan2(y1 - cy, x1 - cx);
         double a2 = Math.Atan2(y2 - cy, x2 - cx);

         if (bulge > 0)
         {
            while (a2 < a1) a2 += 2.0 * Math.PI;
         }
         else
         {
            while (a1 < a2) a1 += 2.0 * Math.PI;
         }

         int segs;
         if (_arcChordMode)
         {
            double maxAngle = 2.0 * Math.Asin(Math.Min(1.0, _arcChordLength / (2.0 * Math.Abs(radius))));
            if (maxAngle < 1e-10) maxAngle = Math.PI / 64;
            segs = Math.Max(3, (int)Math.Ceiling(Math.Abs(theta) / maxAngle));
         }
         else
         {
            segs = _arcSegments;
         }

         var pts = new List<(double x, double y)>(segs + 1);
         for (int j = 0; j <= segs; j++)
         {
            double t = (double)j / segs;
            double a = a1 + t * (a2 - a1);
            pts.Add((cx + radius * Math.Cos(a), cy + radius * Math.Sin(a)));
         }
         return pts;
      }

      // ── Helpers ─────────────────────────────────────────────────────────────

      MaterialArea CreateRegionFromCoords(double[] coords, string layer, string geoSet)
      {
         int n = coords.Length / 2;
         var hullContour = new Contour(
            Enumerable.Range(0, n).Select(i => coords[2 * i] * _scale),
            Enumerable.Range(0, n).Select(i => coords[2 * i + 1] * _scale),
            layer)
         {
            Type = ContourType.Hull,
            GeometrySet = geoSet,
         };

         var ma = new MaterialArea
         {
            Tag = layer,
            Category = AreaCategory.Region,
         };
         ma.Contours.Add(hullContour);
         ma.SetWKT();
         return ma;
      }

      static Contour CreateContourFromCoords(double[] coords, string layer, double scale, string geoSet)
      {
         int n = coords.Length / 2;
         string tag = "Отв.: " + layer;
         return new Contour(
            Enumerable.Range(0, n).Select(i => coords[2 * i] * scale),
            Enumerable.Range(0, n).Select(i => coords[2 * i + 1] * scale),
            tag)
         {
            GeometrySet = geoSet,
         };
      }

      void EnsureConnected()
      {
         if (_acadApp == null)
            throw new InvalidOperationException("Не выполнено подключение к AutoCAD. Вызовите Connect().");
      }

      public void Dispose()
      {
         if (!_disposed && _acadApp != null)
         {
            try { Marshal.ReleaseComObject(_acadApp); }
            catch { }
            _acadApp = null;
            _disposed = true;
         }
      }
   }
}
