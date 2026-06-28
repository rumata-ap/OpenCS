using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using CSTriangulation;

namespace CScore
{
   /// <summary>Категория материальной области: полигональная, группа стержней или одиночный стержень.</summary>
   public enum AreaCategory { Region, RebarGroup }

   /// <summary>Метод генерации сетки фибр.</summary>
   public enum MeshMethod { Grid, Ruppert, AdvancingFront }

   /// <summary>
   /// Материальная область поперечного сечения — единая замена для FiberRegion,
   /// RCFiberRegion и ReBarGroup. Содержит геометрию контуров, коллекцию волокон
   /// (полигональных или точечных) и словарь диаграмм работы материала.
   /// Для арматурных областей Diagramms содержит разностные диаграммы
   /// (σ_сталь − σ_бетон_носителя).
   /// </summary>
   [Serializable]
   public class MaterialArea
   {
      public int Id { get; set; }
      public int Num { get; set; }
      public string Tag { get; set; } = "";
      public string? Description { get; set; }

      /// <summary>Список контуров: внешний (Hull) и отверстия.</summary>
      public List<Contour> Contours { get; set; } = [];

      /// <summary>WKT-представление полигона (null для чисто арматурных областей).</summary>
      public string? WKT { get; set; }

      /// <summary>Высота сечения (размер по оси Y) [м].</summary>
      public double H { get; set; }

      /// <summary>Количество участков деления по оси X.</summary>
      public int NX { get; set; } = 21;

      /// <summary>Количество участков деления по оси Y.</summary>
      public int NY { get; set; } = 21;

      /// <summary>
      /// Волокна области: FiberType.poly/tri — из нарезки/триангуляции,
      /// FiberType.point — арматурные стержни.
      /// </summary>
      public List<Fiber> Fibers { get; set; } = [];

      /// <summary>
      /// Диаграммы работы материала по видам расчёта.
      /// Для арматурных областей с HostArea — разностные (σ_steel − σ_concrete).
      /// Не сохраняется в БД — пересчитывается при загрузке через ResolveAndBuildDiagramms().
      /// </summary>
      [JsonIgnore]
      public Dictionary<CalcType, Diagramm> Diagramms { get; set; } = [];

      /// <summary>Ссылка на материал (для построения диаграмм и UI). Не сериализуется.</summary>
      [JsonIgnore] public Material? Material { get; set; }

      /// <summary>Id материала в БД.</summary>
      public int MaterialId { get; set; }

      /// <summary>
      /// Ссылка на бетонную область-носитель (только для арматурных областей).
      /// null → используется чистая диаграмма материала (арматура вне бетона).
      /// </summary>
      [JsonIgnore] public MaterialArea? HostArea { get; set; }

      /// <summary>Id бетонной области-носителя в БД (null если нет).</summary>
      public int? HostAreaId { get; set; }

      /// <summary>Id контура из пула проекта, назначенного как внешний контур.</summary>
      public int? PoolContourId { get; set; }

      /// <summary>Ссылка на контур из пула. Не сериализуется — разрешается при загрузке.</summary>
      [JsonIgnore] public Contour? PoolContour { get; set; }

      /// <summary>Тип диаграммы работы материала.</summary>
      public DiagrammType DiagrammType { get; set; } = DiagrammType.L2;

      /// <summary>Категория области: полигон, группа стержней или одиночный стержень.</summary>
      public AreaCategory Category { get; set; } = AreaCategory.Region;

      /// <summary>Метод генерации сетки фибр.</summary>
      public MeshMethod MeshMethod { get; set; } = MeshMethod.Grid;

      /// <summary>Предварительное напряжение арматуры после потерь [МПа]. 0 = не преднапряжена.</summary>
      public double SigSp { get; set; } = 0.0;

      /// <summary>Коэффициент точности преднапряжения γ_sp (п. 9.2.6 СП 63). 1.0 = не учитывать.</summary>
      public double GammaSp { get; set; } = 1.0;

      /// <summary>Максимальная площадь треугольника (доля от площади области).</summary>
      public double MeshMaxArea { get; set; } = 0.01;

      /// <summary>Минимальный угол треугольника для метода Рупперта, градусы.</summary>
      public double MeshMinAngle { get; set; } = 30.0;

      /// <summary>Целевая длина ребра треугольника для метода продвижения фронта (0 = авто по MeshMaxArea).</summary>
      public double MeshMaxEdgeLen { get; set; } = 0.0;

      /// <summary>Число итераций сглаживания Лапласа после триангуляции (применяется к обоим методам).</summary>
      public int MeshSmoothIter { get; set; } = 5;

      /// <summary>Id родительского CrossSection в БД.</summary>
      [JsonIgnore] public int SectionId { get; set; }

      public MaterialArea() { }

      public override string ToString() =>
         Material == null
            ? $"{Num:D3}#MaterialArea : {Tag} | <No Material>"
            : $"{Num:D3}#MaterialArea : {Tag} | <{Material.Tag}>";

      /// <summary>Внешний контур области.</summary>
      [JsonIgnore]
      public Contour? Hull
      {
         get => Contours.FirstOrDefault(c => c.Type == ContourType.Hull);
         set
         {
            if (value == null) return;
            value.Type = ContourType.Hull;
            int idx = Contours.FindIndex(c => c.Type == ContourType.Hull);
            if (idx >= 0) Contours[idx] = value;
            else Contours.Insert(0, value);
         }
      }

      /// <summary>Отверстия области.</summary>
      [JsonIgnore]
      public IList<Contour> Holes =>
         Contours.Where(c => c.Type == ContourType.Hole).ToList();

      /// <summary>Обновляет WKT и H по текущему Hull.</summary>
      public void SetWKT()
      {
         if (Hull == null) return;
         List<List<(double X, double Y)>>? holeRings = null;
         if (Holes.Count > 0)
         {
            holeRings = [];
            foreach (var h in Holes)
               holeRings.Add(h.X.Zip(h.Y, (x, y) => (x, y)).ToList());
         }
         WKT = WktHelper.PolygonToWKT(Hull.X, Hull.Y, holeRings);
         if (Hull.X.Count > 0)
            H = Hull.Y.Max() - Hull.Y.Min();
      }

      /// <summary>Назначает материал и строит диаграммы.</summary>
      /// <param name="sp63EtaMin">Нижняя граница нисходящей ветви SP63 (по умолчанию 0.85).</param>
      public void SetMaterial(Material material, DiagrammType diagrammType, double sp63EtaMin = 0.85)
      {
         Material = material;
         MaterialId = material.Id;
         DiagrammType = diagrammType;
         Diagramms = material.GetDiagramms(diagrammType, sp63EtaMin)!;
      }

      /// <summary>
      /// Вычисляет ε_sp = SigSp · GammaSp / E_s и записывает в <see cref="Fiber.Eps_p"/>
      /// для всех точечных фибр. Вызывать после разрешения <see cref="Material"/>.
      /// </summary>
      public void PropagateEps_p()
      {
         if (SigSp == 0.0 || Material == null) return;
         double eps_p = SigSp * 1000.0 * GammaSp / Material.E;  // SigSp [МПа] → [кПа] ×1000; E в кПа
         foreach (var f in Fibers.Where(f => f.TypeFiber == FiberType.point))
            f.Eps_p = eps_p;
      }

      /// <summary>
      /// Пересчитывает диаграммы после загрузки из БД.
      /// Для арматурной области с HostArea строит разностные диаграммы.
      /// </summary>
      /// <param name="sp63EtaMin">Нижняя граница нисходящей ветви SP63 (по умолчанию 0.85).</param>
      /// <param name="pool">Пул диаграмм проекта — нужен для Custom-материала. null = старый путь.</param>
      public void ResolveAndBuildDiagramms(double sp63EtaMin = 0.85,
                                            IReadOnlyList<Diagramm>? pool = null)
      {
         if (Material == null) return;

         Dictionary<CalcType, Diagramm> own;
         if (Material.Type == MatType.Custom && pool != null)
            own = Material.ResolveCustomDiagramms(pool) ?? [];
         else
            own = Material.GetDiagramms(DiagrammType, sp63EtaMin) ?? [];

         if (HostArea != null && HostArea.Diagramms.Count > 0)
         {
            Diagramms = [];
            foreach (var ct in own.Keys)
               Diagramms[ct] = Diagramm.Differential(own[ct], HostArea.Diagramms[ct]);
         }
         else
         {
            Diagramms = own;
         }
         PropagateEps_p();
      }

      /// <summary>
      /// Вычисляет деформации и напряжения во всех волокнах по кривизне плоскости деформаций.
      /// </summary>
      public void SetEps(Kurvature k, CalcType calc, bool ten = true, bool ca = true)
      {
         if (!Diagramms.TryGetValue(calc, out var dgr)) return;

         foreach (var f in Fibers)
            f.Eps = k.e0 + k.ky * f.Y + k.kz * f.X;

         for (int i = 0; i < Fibers.Count; i++)
            dgr.Sig(Fibers[i], ten, ca);

         if (Hull != null)
            foreach (var pt in Hull.Points)
            {
               pt.Eps = k.e0 + k.ky * pt.Y + k.kz * pt.X;
               dgr.Sig(pt, ten, ca);
            }
      }

      /// <summary>
      /// Фабричный метод: создаёт арматурную область с дифференциальными диаграммами.
      /// hostConcreteArea == null → используется чистая стальная диаграмма.
      /// </summary>
      public static MaterialArea CreateRebarArea(
         IEnumerable<Fiber> bars,
         Material steelMaterial,
         DiagrammType steelDiagrammType,
         MaterialArea? hostConcreteArea)
      {
         var area = new MaterialArea
         {
            Material = steelMaterial,
            MaterialId = steelMaterial.Id,
            DiagrammType = steelDiagrammType,
            HostArea = hostConcreteArea,
            HostAreaId = hostConcreteArea?.Id,
            Fibers = bars.ToList()
         };
         area.ResolveAndBuildDiagramms();
         return area;
      }

      /// <summary>
      /// Автоматически определяет HostArea для арматурных областей по вхождению
      /// точечных волокон в контуры бетонных областей (ray casting).
      /// Пропускает области с уже назначенным HostAreaId.
      /// </summary>
      public static void AutoResolveHostAreas(IEnumerable<MaterialArea> allAreas)
      {
         var all = allAreas.ToList();
         var concreteAreas = all
            .Where(a => a.Material?.Type == MatType.Concrete && a.WKT != null)
            .ToList();

         var rebarAreas = all
            .Where(a => a.Fibers.Any(f => f.TypeFiber == FiberType.point))
            .ToList();

         foreach (var rebar in rebarAreas)
         {
            if (rebar.HostAreaId != null) continue;
            var pointFibers = rebar.Fibers.Where(f => f.TypeFiber == FiberType.point).ToList();
            foreach (var conc in concreteAreas)
            {
               bool allInside = pointFibers
                  .All(f => WktHelper.PointInPolygon(conc.WKT!, f.X, f.Y));
               if (allInside)
               {
                  rebar.HostArea = conc;
                  rebar.HostAreaId = conc.Id;
                  break;
               }
            }
            rebar.ResolveAndBuildDiagramms();
         }
      }

      /// <summary>Разбивает область на волокна методом триангуляции, сохраняя точечные волокна.</summary>
      public void Triangulate(double maxTrgArea = 0.01, double maxAngl = 30,
         MeshMethod method = MeshMethod.Ruppert, double maxEdgeLen = 0, int smoothIter = 5)
      {
         var triMethod = method == MeshMethod.AdvancingFront
            ? TriangulationMethod.AdvancingFront
            : TriangulationMethod.Ruppert;
         Fiber[] res = Geo.Triangulation(this, maxTrgArea, maxAngl, maxEdgeLen: maxEdgeLen, smoothIter: smoothIter, method: triMethod);
         var points = Fibers.Where(f => f.TypeFiber == FiberType.point).ToList();
         Fibers = [.. res, .. points];
      }

      /// <summary>Разбивает область на волокна прямоугольной сеткой, сохраняя точечные волокна.</summary>
      public void SliceXY(int nx = 0, int ny = 0)
      {
         int usedNx = nx > 0 ? nx : NX;
         int usedNy = ny > 0 ? ny : NY;
         Fiber[] res = GridSplit.SliceXY(this, usedNx, usedNy);
         var points = Fibers.Where(f => f.TypeFiber == FiberType.point).ToList();
         Fibers = [.. res, .. points];
      }

      /// <summary>Начальное приближение кривизны (упругая стадия).</summary>
      public Kurvature Guess(Load load)
      {
         var pr = new GeoProps(this);
         double ea  = pr.EA  > 1e-10 ? pr.EA  : 1.0;
         double eix = pr.EIx > 1e-10 ? pr.EIx : 1.0;
         double eiy = pr.EIy > 1e-10 ? pr.EIy : 1.0;
         return new Kurvature
         {
            e0 = load.N  / ea,
            ky = load.Mx / eix,
            kz = load.My / eiy
         };
      }

      /// <summary>Создаёт глубокую копию области.</summary>
      public MaterialArea Clone()
      {
         return new MaterialArea
         {
            Tag = Tag,
            Description = Description,
            NX = NX, NY = NY,
            WKT = WKT, H = H,
            Material = Material,
            MaterialId = MaterialId,
            DiagrammType = DiagrammType,
            HostArea = HostArea,
            HostAreaId = HostAreaId,
            Diagramms = new Dictionary<CalcType, Diagramm>(Diagramms),
            Contours = [.. Contours],
            Fibers = Fibers.Select(f => f.Clone()).ToList()
         };
      }

      /// <summary>
      /// Создаёт копию области для параллельного расчёта: клонирует мутабельные Contours и Fibers,
      /// разделяет read-only ссылки (Material, HostArea, PoolContour, Diagramms).
      /// </summary>
      public MaterialArea CloneForCalc() => new()
      {
         Id             = Id,
         Num            = Num,
         Tag            = Tag,
         Description    = Description,
         NX             = NX,
         NY             = NY,
         WKT            = WKT,
         H              = H,
         MaterialId     = MaterialId,
         HostAreaId     = HostAreaId,
         PoolContourId  = PoolContourId,
         DiagrammType   = DiagrammType,
         Category       = Category,
         MeshMethod     = MeshMethod,
         MeshMaxArea    = MeshMaxArea,
         MeshMinAngle   = MeshMinAngle,
         MeshMaxEdgeLen = MeshMaxEdgeLen,
         MeshSmoothIter = MeshSmoothIter,
         SectionId      = SectionId,
         Material       = Material,
         HostArea       = HostArea,
         PoolContour    = PoolContour,
         Diagramms      = Diagramms,
         Contours       = Contours.Select(c => c.CloneForCalc()).ToList(),
         Fibers         = Fibers.Select(f => f.CloneForCalc()).ToList()
      };

      /// <summary>
      /// Вычисляет (N, Mx, My) методом теоремы Грина по контуру области.
      /// Mx = ∬σ·y dA, My = ∬σ·x dA.
      /// Точечные фибры (арматура) не учитываются — только полигональная часть.
      /// Требует: Hull != null, Diagramms содержит ключ calc.
      /// </summary>
      public (double N, double Mx, double My) ContourIntegral(
         Kurvature k, CalcType calc, bool ten = true, bool ca = true)
      {
         var dgr = Diagramms[calc];

         Func<double, double, double> epsFunc = (x, y) => k.e0 + k.ky * y + k.kz * x;
         Func<double, double, double> sigma   = (x, y) => dgr.SigValue(epsFunc(x, y), ten, ca);
         double[] critEps = dgr.GetCriticalStrains();

         // Внешний контур без замыкающей точки (Contour.X/Y замкнуты: X[0]==X[last])
         var outer = Hull!.X.Take(Hull.X.Count - 1)
                           .Zip(Hull.Y.Take(Hull.Y.Count - 1), (x, y) => (X: x, Y: y))
                           .ToList();

         // Отверстия без замыкающей точки
         IReadOnlyList<IReadOnlyList<(double X, double Y)>> holes =
            Holes.Select(h => (IReadOnlyList<(double X, double Y)>)
                    h.X.Take(h.X.Count - 1)
                       .Zip(h.Y.Take(h.Y.Count - 1), (x, y) => (X: x, Y: y))
                       .ToList())
                 .ToList();

         var gi = new GreenIntegrator(outer, holes);
         return gi.IntegrateN_Mx_My(sigma, critEps, epsFunc);
      }
   }
}
