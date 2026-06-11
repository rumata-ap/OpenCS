using CScore;

using OpenCS.Utilites;

using netDxf;
using netDxf.Entities;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace OpenCS.ViewModels
{
   /// <summary>
   /// ViewModel импорта геометрии из DXF. Парсит Polyline2D, Circle,
   /// Arc (аппроксимация 32-точечной ломаной) и Line (сшивка в цепи).
   /// Взаимодействие с <see cref="Views.DxfInteractiveView"/> — через колбэки
   /// <see cref="CanvasLoader"/> и <see cref="HandleSelectionChanged"/>.
   /// </summary>
   public class FromDxfVM : ViewModelBase
   {
      private static readonly string[] Palette =
         ["#318CE7", "#9457EB", "#CC397B", "#F07427", "#F4CA16", "#20B2AA"];

      public AppViewModel mvm = null!;

      private double _scale = 0.001;
      private int _unitIdx;
      private DxfRole _selectMode = DxfRole.Hull;
      private CircleDiscretizeMethod _discretizeMethod = CircleDiscretizeMethod.ChordLength;
      private double _discretizeValue = 0.020;
      private string _tag = "area";
      private List<DxfPrimitive> _primitives = [];

      public List<string> Units { get; } = ["мм", "см", "м"];

      /// <summary>Слои DXF — источник для легенды в левой панели.</summary>
      public ObservableCollection<LayerInfo> Layers { get; } = [];

      /// <summary>
      /// Устанавливается code-behind страницы. Вызывается после успешного
      /// разбора DXF для передачи примитивов в <see cref="Views.DxfInteractiveView"/>.
      /// </summary>
      public Action<IReadOnlyList<DxfPrimitive>, IReadOnlyList<LayerInfo>>? CanvasLoader { get; set; }

      public int UnitIdx
      {
         get => _unitIdx;
         set
         {
            _unitIdx = value;
            _scale = value == 0 ? 0.001 : value == 1 ? 0.01 : 1.0;
            OnPropertyChanged();
         }
      }

      public DxfRole SelectMode
      {
         get => _selectMode;
         set { _selectMode = value; OnPropertyChanged(); }
      }

      public CircleDiscretizeMethod DiscretizeMethod
      {
         get => _discretizeMethod;
         set { _discretizeMethod = value; OnPropertyChanged(); }
      }

      public List<string> DiscretizeMethodDisplayNames { get; } =
         ["Длина хорды", "Число сегментов"];

      public int DiscretizeMethodIdx
      {
         get => (int)_discretizeMethod;
         set { _discretizeMethod = (CircleDiscretizeMethod)value; OnPropertyChanged(); }
      }

      public double DiscretizeValue
      {
         get => _discretizeValue;
         set { _discretizeValue = value; OnPropertyChanged(); }
      }

      public string Tag
      {
         get => _tag;
         set { _tag = value; OnPropertyChanged(); }
      }

      // ── Вычисляемые коллекции по ролям ───────────────────────────────────

      public DxfPrimitive? HullPrimitive =>
         _primitives.FirstOrDefault(p => p.Role == DxfRole.Hull);

      public IReadOnlyList<DxfPrimitive> HolePrimitives =>
         _primitives.Where(p => p.Role == DxfRole.Hole).ToList();

      public IReadOnlyList<DxfPrimitive> GroupBarPrimitives =>
         _primitives.Where(p => p.Role == DxfRole.RebarGroup).ToList();

      public IReadOnlyList<DxfPrimitive> SingleBarPrimitives =>
         _primitives.Where(p => p.Role == DxfRole.SingleBar).ToList();

      // ── Команды ──────────────────────────────────────────────────────────

      public ICommand OpenDXFCommand            { get; }
      public ICommand CreateMaterialAreaCommand { get; }
      public ICommand ClearRoleCommand          { get; }

      public FromDxfVM()
      {
         OpenDXFCommand            = new RelayCommand(OpenDxf);
         CreateMaterialAreaCommand = new RelayCommand(CreateMaterialArea);
         ClearRoleCommand          = new RelayCommand(p => ClearRole((DxfPrimitive)p!));
      }

      /// <summary>
      /// Вызывается канвасом при клике. Назначает текущий режим как роль примитива.
      /// Hull: допускает только один объект — предыдущий Hull сбрасывается.
      /// Повторный клик на тот же режим — сброс в None.
      /// </summary>
      public void HandlePrimitiveClicked(DxfPrimitive p)
      {
         if (p.Role == _selectMode)
         {
            p.Role = DxfRole.None;
         }
         else
         {
            if (_selectMode == DxfRole.Hull)
            {
               foreach (var prev in _primitives.Where(x => x.Role == DxfRole.Hull))
                  prev.Role = DxfRole.None;
            }
            p.Role = _selectMode;
         }
         OnPropertyChanged(nameof(HullPrimitive));
         OnPropertyChanged(nameof(HolePrimitives));
         OnPropertyChanged(nameof(GroupBarPrimitives));
         OnPropertyChanged(nameof(SingleBarPrimitives));
      }

      /// <summary>Сбрасывает роль примитива в None. Вызывается кнопкой [×] в правой панели.</summary>
      public void ClearRole(DxfPrimitive p)
      {
         p.Role = DxfRole.None;
         OnPropertyChanged(nameof(HullPrimitive));
         OnPropertyChanged(nameof(HolePrimitives));
         OnPropertyChanged(nameof(GroupBarPrimitives));
         OnPropertyChanged(nameof(SingleBarPrimitives));
      }

      private void CreateMaterialArea(object? _ = null)
      {
         if (HullPrimitive == null && !GroupBarPrimitives.Any() && !SingleBarPrimitives.Any())
         {
            mvm.LogService.Info("Нет назначенных объектов для создания области");
            return;
         }

         // ── Region (если назначен Hull) ───────────────────────────────────
         MaterialArea? region = null;
         if (HullPrimitive != null)
         {
            region = new MaterialArea { Tag = _tag, Category = AreaCategory.Region };
            region.Hull = ToHullContour(HullPrimitive, _discretizeMethod, _discretizeValue);
            foreach (var hp in HolePrimitives)
               region.Contours.Add(ToHoleContour(hp, _discretizeMethod, _discretizeValue));
            region.SetWKT();
            mvm.db.SaveMaterialArea(region);
         }

         // ── RebarGroup (все GroupBar) ─────────────────────────────────────
         if (GroupBarPrimitives.Any())
         {
            var group = new MaterialArea
            {
               Tag        = _tag + "_г",
               Category   = AreaCategory.RebarGroup,
               HostAreaId = region?.Id
            };
            foreach (var bar in GroupBarPrimitives)
               group.Fibers.Add(Fiber.CreatePoint(bar.Radius * 2, bar.CenterX, bar.CenterY));
            mvm.db.SaveMaterialArea(group);
         }

         // ── SingleBar (каждая → отдельная MaterialArea) ───────────────────
         foreach (var bar in SingleBarPrimitives)
         {
            var single = new MaterialArea
            {
               Tag        = _tag + "_с",
               Category   = AreaCategory.SingleBar,
               HostAreaId = region?.Id
            };
            single.Fibers.Add(Fiber.CreatePoint(bar.Radius * 2, bar.CenterX, bar.CenterY));
            mvm.db.SaveMaterialArea(single);
         }

         // ── Сброс ролей после сохранения ─────────────────────────────────
         foreach (var p in _primitives) p.Role = DxfRole.None;
         OnPropertyChanged(nameof(HullPrimitive));
         OnPropertyChanged(nameof(HolePrimitives));
         OnPropertyChanged(nameof(GroupBarPrimitives));
         OnPropertyChanged(nameof(SingleBarPrimitives));

         mvm.RefreshMaterialAreaLiveCollections();
         mvm.LogService.Info($"Создана MaterialArea «{_tag}»");
      }

      private void OpenDxf(object? _ = null)
      {
         string fileName = mvm.FileDialogService.OpenFile(
            filter: "Файл обмена чертежами (*.dxf)|*.dxf",
            title: "Импорт данных из файла DXF");
         if (string.IsNullOrEmpty(fileName)) return;

         foreach (var p in _primitives) p.Role = DxfRole.None;

         var dxf = DxfDocument.Load(fileName);
         Tag = dxf.Name;

         _primitives = ParseDxf(dxf);

         Layers.Clear();
         var names = _primitives.Select(p => p.LayerName).Distinct().ToList();
         for (int i = 0; i < names.Count; i++)
            Layers.Add(new LayerInfo(names[i], Palette[i % Palette.Length]));

         CanvasLoader?.Invoke(_primitives, Layers);
      }

      // ── Парсинг ────────────────────────────────────────────────────────────

      private List<DxfPrimitive> ParseDxf(DxfDocument dxf)
      {
         var result = new List<DxfPrimitive>();
         int num = 1;

         foreach (var p in dxf.Entities.Polylines2D)
            result.Add(PolylineToPrimitive(p, num++));

         foreach (var c in dxf.Entities.Circles)
            result.Add(CircleToPrimitive(c, num++));

         foreach (var a in dxf.Entities.Arcs)
            result.Add(ArcToPrimitive(a, num++));

         foreach (var group in dxf.Entities.Lines.GroupBy(l => l.Layer.Name))
         {
            var stitched = StitchLines(group, group.Key, num);
            result.AddRange(stitched);
            num += stitched.Count;
         }

         return result;
      }

      private DxfPrimitive PolylineToPrimitive(Polyline2D pline, int num)
      {
         var verts = pline.Vertexes;
         bool needClose = pline.IsClosed &&
            !verts.First().Position.Equals(verts.Last().Position, 1e-4);
         int total = verts.Count + (needClose ? 1 : 0);

         var xs = new double[total];
         var ys = new double[total];
         var pts = new List<StressPoint>(total);

         int j = 0;
         foreach (var v in verts)
         {
            xs[j] = v.Position.X * _scale;
            ys[j] = v.Position.Y * _scale;
            pts.Add(new StressPoint(xs[j], ys[j]) { Num = j + 1 });
            j++;
         }
         if (needClose)
         {
            xs[j] = verts.First().Position.X * _scale;
            ys[j] = verts.First().Position.Y * _scale;
            pts.Add(new StressPoint(xs[j], ys[j]) { Num = j + 1 });
         }

         var contour = new Contour(pts, pline.Layer.Name) { Num = num, GeometrySet = _tag };
         contour.SetWKT();

         return new DxfPrimitive
         {
            Kind = DxfPrimitiveKind.Contour,
            LayerName = pline.Layer.Name,
            Xs = xs, Ys = ys,
            Contour = contour
         };
      }

      private DxfPrimitive CircleToPrimitive(Circle circle, int num)
      {
         var cp = new CircleP(circle.Center.X * _scale, circle.Center.Y * _scale, circle.Radius * _scale)
         {
            Num = num,
            Tag = circle.Layer.Name,
            GeometrySet = _tag
         };
         return new DxfPrimitive
         {
            Kind = DxfPrimitiveKind.Circle,
            LayerName = circle.Layer.Name,
            CenterX = cp.X, CenterY = cp.Y, Radius = cp.Radius,
            Circle = cp
         };
      }

      private DxfPrimitive ArcToPrimitive(Arc arc, int num)
      {
         // netDxf: углы в градусах; Math.Cos/Sin — в радианах
         double startRad = arc.StartAngle * Math.PI / 180;
         double endRad   = arc.EndAngle   * Math.PI / 180;
         if (endRad < startRad) endRad += 2 * Math.PI;

         const int N = 32;
         var xs = new double[N + 1];
         var ys = new double[N + 1];
         var pts = new List<StressPoint>(N + 1);

         for (int i = 0; i <= N; i++)
         {
            double angle = startRad + i * (endRad - startRad) / N;
            xs[i] = (arc.Center.X + arc.Radius * Math.Cos(angle)) * _scale;
            ys[i] = (arc.Center.Y + arc.Radius * Math.Sin(angle)) * _scale;
            pts.Add(new StressPoint(xs[i], ys[i]) { Num = i + 1 });
         }

         var contour = new Contour(pts, arc.Layer.Name) { Num = num, GeometrySet = _tag };
         contour.SetWKT();

         return new DxfPrimitive
         {
            Kind = DxfPrimitiveKind.Contour,
            LayerName = arc.Layer.Name,
            Xs = xs, Ys = ys,
            Contour = contour
         };
      }

      /// <summary>
      /// Сшивает набор отрезков одного слоя в замкнутые/незамкнутые цепи.
      /// Алгоритм: граф смежности + жадный DFS.
      /// </summary>
      private List<DxfPrimitive> StitchLines(IEnumerable<Line> lines, string layerName, int startNum)
      {
         const double tol = 1e-6;
         (double x, double y) Snap(double x, double y) =>
            (Math.Round(x / tol) * tol, Math.Round(y / tol) * tol);

         var segs = lines.Select(l => (
            A: Snap(l.StartPoint.X * _scale, l.StartPoint.Y * _scale),
            B: Snap(l.EndPoint.X * _scale, l.EndPoint.Y * _scale)
         )).ToList();

         if (segs.Count == 0) return [];

         var adj = new Dictionary<(double, double), List<int>>();
         for (int i = 0; i < segs.Count; i++)
         {
            if (!adj.TryGetValue(segs[i].A, out var la)) adj[segs[i].A] = la = [];
            la.Add(i);
            if (!adj.TryGetValue(segs[i].B, out var lb)) adj[segs[i].B] = lb = [];
            lb.Add(i);
         }

         var used = new bool[segs.Count];
         var result = new List<DxfPrimitive>();
         int num = startNum;

         for (int start = 0; start < segs.Count; start++)
         {
            if (used[start]) continue;
            used[start] = true;

            var chain = new List<(double x, double y)> { segs[start].A, segs[start].B };
            var startPt = segs[start].A;
            var curPt = segs[start].B;

            while (true)
            {
               int next = adj[curPt].FirstOrDefault(i => !used[i], -1);
               if (next == -1) break;
               used[next] = true;
               curPt = segs[next].A == curPt ? segs[next].B : segs[next].A;
               chain.Add(curPt);
               if (curPt == startPt) break;
            }

            if (chain.Count < 2) continue;

            var xs = chain.Select(p => p.x).ToArray();
            var ys = chain.Select(p => p.y).ToArray();
            var pts = chain.Select((p, i) => new StressPoint(p.x, p.y) { Num = i + 1 }).ToList();

            var contour = new Contour(pts, layerName) { Num = num, GeometrySet = _tag };
            contour.SetWKT();

            result.Add(new DxfPrimitive
            {
               Kind = DxfPrimitiveKind.Contour,
               LayerName = layerName,
               Xs = xs, Ys = ys,
               Contour = contour
            });
            num++;
         }

         return result;
      }

      // ── Дискретизация и ориентация контуров ──────────────────────────────

      /// <summary>Метод дискретизации окружности в полигон.</summary>
      public enum CircleDiscretizeMethod { ChordLength, SegmentCount }

      /// <summary>
      /// Вычисляет площадь полигона со знаком по формуле Гаусса.
      /// Положительная → CCW, отрицательная → CW.
      /// </summary>
      internal static double SignedArea(IList<double> x, IList<double> y)
      {
         double s = 0;
         int n = x.Count - 1; // последняя точка = первая (замкнутый контур)
         for (int i = 0; i < n; i++)
            s += x[i] * y[i + 1] - x[i + 1] * y[i];
         return s / 2.0;
      }

      /// <summary>
      /// Дискретизирует окружность в замкнутый контур.
      /// ccw=true → обход против часовой (Hull); ccw=false → по часовой (Hole).
      /// </summary>
      internal static CScore.Contour DiscretizeCircle(
         double cx, double cy, double r,
         CircleDiscretizeMethod method, double value, bool ccw)
      {
         int n = method == CircleDiscretizeMethod.ChordLength
            ? Math.Max(3, (int)Math.Ceiling(2 * Math.PI * r / Math.Max(value, 1e-9)))
            : Math.Max(3, (int)value);

         double step = 2 * Math.PI / n;
         double dir  = ccw ? 1.0 : -1.0;

         var xs = new List<double>(n + 1);
         var ys = new List<double>(n + 1);
         for (int i = 0; i <= n; i++) // n+1 точек — последняя = первой
         {
            xs.Add(cx + r * Math.Cos(dir * i * step));
            ys.Add(cy + r * Math.Sin(dir * i * step));
         }
         return new CScore.Contour(xs, ys, "circle");
      }

      /// <summary>
      /// Возвращает контур с типом Hull (CCW). Если исходный CW — разворачивает.
      /// </summary>
      internal static CScore.Contour ToHullContour(DxfPrimitive p,
         CircleDiscretizeMethod method, double value)
      {
         CScore.Contour c;
         if (p.Kind == DxfPrimitiveKind.Circle)
         {
            c = DiscretizeCircle(p.CenterX, p.CenterY, p.Radius, method, value, ccw: true);
         }
         else
         {
            c = p.Contour!;
            if (SignedArea(c.X, c.Y) < 0) // CW → reverse
               c = new CScore.Contour(c.X.Reverse().ToList(), c.Y.Reverse().ToList(), c.Tag);
         }
         c.Type = ContourType.Hull;
         return c;
      }

      /// <summary>
      /// Возвращает контур с типом Hole (CW). Если исходный CCW — разворачивает.
      /// </summary>
      internal static CScore.Contour ToHoleContour(DxfPrimitive p,
         CircleDiscretizeMethod method, double value)
      {
         CScore.Contour c;
         if (p.Kind == DxfPrimitiveKind.Circle)
         {
            c = DiscretizeCircle(p.CenterX, p.CenterY, p.Radius, method, value, ccw: false);
         }
         else
         {
            c = p.Contour!;
            if (SignedArea(c.X, c.Y) > 0) // CCW → reverse to CW
               c = new CScore.Contour(c.X.Reverse().ToList(), c.Y.Reverse().ToList(), c.Tag);
         }
         c.Type = ContourType.Hole;
         return c;
      }
   }
}
