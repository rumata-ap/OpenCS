using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace CScore
{
    /// <summary>
    /// Поперечное сечение — контейнер материальных областей с единым интегралом.
    /// Заменяет RCFiberRegion как контейнер. Минимум одна MaterialArea.
    /// </summary>
    [Serializable]
    public class CrossSection
    {
       public int Id { get; set; }
       public int Num { get; set; }
       public string Tag { get; set; } = "";
       public string? Description { get; set; }

       public List<MaterialArea> Areas { get; set; } = [];

       public CrossSection() { }

       public override string ToString() => $"{Num:D3}#CrossSection : {Tag}";

       /// <summary>
       /// Перечисляет пары (область, эффективная плоскость деформаций) при базовой кривизне
       /// <paramref name="baseK"/>. Базовая реализация возвращает (area, baseK) для каждой
       /// области из <see cref="Areas"/>. Производные классы (например, <see cref="TwoStageSection"/>)
       /// могут переопределять этот метод, чтобы вернуть разные эффективные плоскости
       /// для разных групп областей (замороженная κ1 для этапа 1 + κ2 для этапа 2).
       /// Этот метод — единая точка для потребителей (сводка, графики σ/ε, жёсткости),
       /// чтобы корректно работать с любым типом сечения.
       /// </summary>
       public virtual IEnumerable<(MaterialArea area, Kurvature k)> EnumerateAreas(Kurvature baseK)
       {
          foreach (var area in Areas)
             yield return (area, baseK);
       }

      /// <summary>
      /// Вычисляет деформации и напряжения во всех областях по кривизне.
      /// </summary>
      public virtual void SetEps(Kurvature k, CalcType calc,
                                  bool ten = true, bool ca = true)
      {
         foreach (var (area, ka) in EnumerateAreas(k))
            area.SetEps(ka, calc, ten, ca);
      }

      /// <summary>
      /// Единый интеграл по всем областям (бетон + арматура с разностными диаграммами).
      /// Если у области нет сеточных фибр и задан Hull — используется контурный путь (теорема Грина).
      /// Иначе — фибровое суммирование.
      /// </summary>
      public virtual Load Integral(Kurvature k, CalcType calc = CalcType.C,
                                    bool ten = true, bool ca = true)
      {
         double N = 0, Mx = 0, My = 0;
         // EnumerateAreas даёт пары (область, эффективная плоскость деформаций ka).
         // Для базового сечения ka == k; для составного (TwoStageSection) области этапа 1
         // получают k + κ1, области этапа 2 — k. Так контурный и фибровый пути работают
         // одинаково для любого производного сечения.
         foreach (var (area, ka) in EnumerateAreas(k))
         {
            bool hasMeshFibers = area.Fibers.Any(f => f.TypeFiber != FiberType.point);

            if (!hasMeshFibers && area.Hull != null && area.Diagramms.ContainsKey(calc))
            {
               // Контурный путь для полигонной части
               var (n, mx, my) = area.ContourIntegral(ka, calc, ten, ca);
               N += n; Mx += mx; My += my;

               // Точечные фибры в этой области (если есть) — через SetEps
               if (area.Fibers.Count > 0)
               {
                  area.SetEps(ka, calc, ten, ca);
                  foreach (var f in area.Fibers)
                  { N += f.N; Mx += f.Mx; My += f.My; }
               }
            }
            else
            {
               // Фибровый путь — SetEps обрабатывает все фибры (mesh + point)
               area.SetEps(ka, calc, ten, ca);
               foreach (var f in area.Fibers)
               { N += f.N; Mx += f.Mx; My += f.My; }
            }
         }
         return new Load { Calc = calc, N = N, Mx = Mx, My = My };
      }

      /// <summary>Интеграл + геометрические характеристики.</summary>
      public virtual Load Integral(Kurvature k, out GeoProps props,
                                    CalcType calc = CalcType.C,
                                    bool ten = true, bool ca = true)
      {
         var load = Integral(k, calc, ten, ca);
         props = new GeoProps(this);
         return load;
      }

      /// <summary>
      /// Начальное приближение кривизны (упругая стадия).
      /// Для областей без сеточных фибр использует геометрию контура Hull,
      /// чтобы не занижать жёсткость сечения.
      /// </summary>
      public virtual Kurvature Guess(Load load)
      {
         return GuessFromProps(ElasticProps(Areas), load);
      }

      /// <summary>
      /// Накапливает упругие геометрические характеристики (EA, EIx, EIy) по набору областей.
      /// Для областей без сеточных фибр использует геометрию контура Hull, чтобы не занижать
      /// жёсткость сечения. Используется при построении начального приближения кривизны.
      /// </summary>
      protected static GeoProps ElasticProps(IEnumerable<MaterialArea> areas)
      {
         var pr = new GeoProps();
         foreach (var area in areas)
         {
            bool hasMeshFibers = area.Fibers.Any(f => f.TypeFiber != FiberType.point);
            if (!hasMeshFibers && area.Hull != null && area.Material != null)
            {
               double E = area.Material.E;
               // Внешний контур
               var ap = new GeoProps(area.Hull, E);
               pr = pr + ap;
               // Вычесть отверстия
               foreach (var hole in area.Holes)
                  pr = pr - new GeoProps(hole, E);
            }
            // Фибровый вклад (арматурные точки, сгенерированная сетка)
            pr = pr + new GeoProps(area);
         }
         return pr;
      }

      /// <summary>Приведённые (E-взвешенные) моменты инерции бетона и арматуры порознь.</summary>
      public readonly record struct StiffnessSplit(
         double EIxConcrete, double EIxRebar,
         double EIyConcrete, double EIyRebar);

      /// <summary>
      /// Разделяет приведённую жёсткость сечения на вклад бетона и арматуры
      /// относительно общего приведённого центра тяжести сечения. Нужно для
      /// формулы D = kb·Eb·I + ks·Es·Is (п. 8.1.15 СП63.13330). Использует уже
      /// существующую агрегацию <see cref="ElasticProps"/>, разбитую по признаку
      /// <see cref="MaterialArea.HostAreaId"/> (null — бетон, иначе — арматура),
      /// затем переносит оба слагаемых к общему центру тяжести по формуле
      /// параллельного переноса I_C = I_O - 2·c·S_O + c²·A_O.
      /// </summary>
      public StiffnessSplit SplitStiffnessByMaterial()
      {
         var concreteAreas = Areas.Where(a => a.HostAreaId == null);
         var rebarAreas    = Areas.Where(a => a.HostAreaId != null);

         var prConcrete = ElasticProps(concreteAreas);
         var prRebar    = ElasticProps(rebarAreas);
         var prAll      = prConcrete + prRebar;

         double xc = prAll.EA > 1e-10 ? prAll.Centroid.X : 0.0;
         double yc = prAll.EA > 1e-10 ? prAll.Centroid.Y : 0.0;

         double eixConcrete = prConcrete.EIx - 2 * yc * prConcrete.ESx + yc * yc * prConcrete.EA;
         double eixRebar    = prRebar.EIx    - 2 * yc * prRebar.ESx    + yc * yc * prRebar.EA;
         double eiyConcrete = prConcrete.EIy - 2 * xc * prConcrete.ESy + xc * xc * prConcrete.EA;
         double eiyRebar    = prRebar.EIy    - 2 * xc * prRebar.ESy    + xc * xc * prRebar.EA;

         return new StiffnessSplit(eixConcrete, eixRebar, eiyConcrete, eiyRebar);
      }

      /// <summary>
      /// Ограничивающий прямоугольник сечения (по контурам областей и точечным
      /// фибрам арматуры). Нужен для автоматического определения высоты сечения
      /// h в плоскости изгиба (п. 8.1.15: δe = e0/h, гейт гибкости l0/h).
      /// </summary>
      public (double minX, double maxX, double minY, double maxY) SectionBoundingBox()
      {
         double minX = double.MaxValue, maxX = double.MinValue;
         double minY = double.MaxValue, maxY = double.MinValue;
         bool any = false;

         void Consider(double x, double y)
         {
            any = true;
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
         }

         foreach (var area in Areas)
         {
            if (area.Hull != null)
            {
               area.Hull.PointsToXYs();
               for (int i = 0; i < area.Hull.X.Count; i++)
                  Consider(area.Hull.X[i], area.Hull.Y[i]);
            }
            foreach (var f in area.Fibers)
               Consider(f.X, f.Y);
         }

         if (!any)
            throw new InvalidOperationException("Сечение не содержит геометрии для ограничивающего прямоугольника");

         return (minX, maxX, minY, maxY);
      }

      /// <summary>Упругое приближение кривизны из геометрических характеристик и нагрузки.</summary>
      protected static Kurvature GuessFromProps(GeoProps pr, Load load)
      {
         // Load.Mx = ∫σ·y·dA → ky (dε/dy), жёсткость EIx = E·∫y²dA
         // Load.My = ∫σ·x·dA → kz (dε/dx), жёсткость EIy = E·∫x²dA
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

      /// <summary>
      /// Строит диаграммы для всех областей после загрузки из БД.
      /// Сначала — области без HostArea (бетонные), затем — с HostArea (арматурные).
      /// </summary>
      /// <param name="sp63EtaMin">Нижняя граница нисходящей ветви SP63 (η_min, по умолчанию 0.85).</param>
      /// <param name="pool">Пул диаграмм проекта — пробрасывается в MaterialArea для Custom-материалов.</param>
      /// <param name="rebarDifferentialDiagram">Разностная диаграмма σ_st − σ_bc для арматуры в бетоне.</param>
      public void ResolveAndBuildDiagramms(double sp63EtaMin = 0.85,
                                            IReadOnlyList<Diagramm>? pool = null,
                                            bool rebarDifferentialDiagram = true)
      {
         foreach (var area in Areas)
            if (area.HostAreaId == null)
               area.ResolveAndBuildDiagramms(sp63EtaMin, pool, rebarDifferentialDiagram);

         foreach (var area in Areas)
            if (area.HostAreaId != null)
            {
               area.HostArea = Areas.Find(a => a.Id == area.HostAreaId);
               area.ResolveAndBuildDiagramms(sp63EtaMin, pool, rebarDifferentialDiagram);
            }
      }

      /// <summary>
      /// Создаёт копию сечения для параллельного расчёта: клонирует мутабельные Areas.
      /// </summary>
      public virtual CrossSection CloneForCalc() => new()
      {
         Id          = Id,
         Num         = Num,
         Tag         = Tag,
         Description = Description,
         Areas       = Areas.Select(a => a.CloneForCalc()).ToList()
      };

      /// <summary>
      /// Прямой расчёт усилий и (опционально) касательной 3×3 по плоскости деформаций.
      /// Касательная — forward finite differences (4 вызова <see cref="Integral"/>).
      /// </summary>
      public SectionResult Compute(Kurvature k, CalcType calc = CalcType.C,
                                   bool ten = true, bool ca = true,
                                   bool computeStiffness = true, double fdStep = 1e-7)
      {
         var f0 = Integral(k, calc, ten, ca);
         if (!computeStiffness)
            return new SectionResult { N = f0.N, Mx = f0.Mx, My = f0.My };

         const int n = 3;
         var j = new double[n, n];
         var fBase = new[] { f0.N, f0.Mx, f0.My };
         double h = fdStep;

         for (int col = 0; col < n; col++)
         {
            var kp = k;
            if (col == 0) kp.e0 += h;
            else if (col == 1) kp.ky += h;
            else kp.kz += h;

            var fp = Integral(kp, calc, ten, ca);
            var fPert = new[] { fp.N, fp.Mx, fp.My };
            for (int row = 0; row < n; row++)
               j[row, col] = (fPert[row] - fBase[row]) / h;
         }

         return new SectionResult { N = f0.N, Mx = f0.Mx, My = f0.My, Tangent = j };
      }
   }
}
