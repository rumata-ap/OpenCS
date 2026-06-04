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
      private string _geometrySet = "dxf";
      private ObservableCollection<Contour> _contoursPrj = [];
      private ObservableCollection<CircleP> _circlesPrj = [];
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

      public string GeometrySet
      {
         get => _geometrySet;
         set { _geometrySet = value; OnPropertyChanged(); }
      }

      public ObservableCollection<Contour> ContoursPrj
      {
         get => _contoursPrj;
         set { _contoursPrj = value; OnPropertyChanged(); }
      }

      public ObservableCollection<CircleP> CirclesPrj
      {
         get => _circlesPrj;
         set { _circlesPrj = value; OnPropertyChanged(); }
      }

      public ICommand OpenDXFCommand      { get; }
      public ICommand SaveContoursCommand { get; }
      public ICommand SaveCirclesCommand  { get; }

      public FromDxfVM()
      {
         OpenDXFCommand      = new RelayCommand(OpenDxf);
         SaveContoursCommand = new RelayCommand(SaveContours);
         SaveCirclesCommand  = new RelayCommand(SaveCircles);
      }

      /// <summary>
      /// Обновляет <see cref="ContoursPrj"/> и <see cref="CirclesPrj"/> по текущему
      /// выделению канваса. Подключается через <see cref="Views.DxfInteractiveView.SelectionChanged"/>.
      /// </summary>
      public void HandleSelectionChanged(IReadOnlyList<DxfPrimitive> selected)
      {
         ContoursPrj = new ObservableCollection<Contour>(
            selected.Where(p => p.Kind == DxfPrimitiveKind.Contour && p.Contour != null)
                    .Select(p => p.Contour!));
         CirclesPrj = new ObservableCollection<CircleP>(
            selected.Where(p => p.Kind == DxfPrimitiveKind.Circle && p.Circle != null)
                    .Select(p => p.Circle!));
      }

      private void SaveContours(object? _ = null)
      {
         if (_contoursPrj.Count == 0) return;
         mvm.db.AddRange(_contoursPrj);
         mvm.LogService.Info($"В проект добавлено {_contoursPrj.Count} контуров");
         ContoursPrj.Clear();
         mvm.ContoursRenumber();
      }

      private void SaveCircles(object? _ = null)
      {
         if (_circlesPrj.Count == 0) return;
         mvm.db.AddRange(_circlesPrj);
         mvm.LogService.Info($"В проект добавлено {_circlesPrj.Count} окружностей");
         CirclesPrj.Clear();
         mvm.CirclesRenumber();
      }

      private void OpenDxf(object? _ = null)
      {
         string fileName = mvm.FileDialogService.OpenFile(
            filter: "Файл обмена чертежами (*.dxf)|*.dxf",
            title: "Импорт данных из файла DXF");
         if (string.IsNullOrEmpty(fileName)) return;

         ContoursPrj.Clear();
         CirclesPrj.Clear();

         var dxf = DxfDocument.Load(fileName);
         GeometrySet = dxf.Name;

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

         var contour = new Contour(pts, pline.Layer.Name) { Num = num, GeometrySet = _geometrySet };
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
            GeometrySet = _geometrySet
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

         var contour = new Contour(pts, arc.Layer.Name) { Num = num, GeometrySet = _geometrySet };
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

            var contour = new Contour(pts, layerName) { Num = num, GeometrySet = _geometrySet };
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
   }
}
