using CSmath;

using System.Text.Json.Serialization;

using System;
using System.Collections.Generic;
using System.Linq;

namespace CScore
{
   [Serializable]
   public class MaterialChars
   {
      [JsonIgnore]
      public int Id { get; set; }
      [JsonIgnore]
      public int MaterialId { get; set; }
      [JsonIgnore]
      public Material? Material { get; set; }

      public string Tag { get; set; } = "";

      /// <summary>
      /// Класс матриала.
      /// </summary>
      public double Class { get; set; }

      /// <summary>
      /// Прочность на сжатие.
      /// </summary>
      public double Fc { get; set; }

      /// <summary>
      /// Прочность на растяжение.
      /// </summary>
      public double Ft { get; set; }

      /// <summary>
      /// Предел текучести.
      /// </summary>
      public double Ry { get; set; }

      /// <summary>
      /// Предел прочности.
      /// </summary>
      public double Ru { get; set; }

      /// <summary>
      /// Модуль упругости.
      /// </summary>
      public double E { get; set; }

      /// <summary>
      /// Деформация при достижении fc.
      /// </summary>
      public double Ec0 { get; set; }

      /// <summary>
      /// Деформация при достижении 0.6 fc.
      /// </summary>
      public double Ec1 { get; set; }

      /// <summary>
      /// Максимальная деформация.
      /// </summary>
      public double Ec2 { get; set; }

      /// <summary>
      /// Деформация при достижении fc для двухлинейной диаграммы.
      /// </summary>
      public double Ec1Red { get; set; }

      /// <summary>
      /// Деформация при достижении ft для двухлинейной диаграммы.
      /// </summary>
      public double Et1Red { get; set; }

      /// <summary>
      /// Деформация при достижении ft.
      /// </summary>
      public double Et0 { get; set; }

      /// <summary>
      /// Деформация при достижении 0.6 ft.
      /// </summary>
      public double Et1 { get; set; }

      /// <summary>
      /// Максимальная деформация при растяжении.
      /// </summary>
      public double Et2 { get; set; }

      /// <summary>
      /// Тип материала.
      /// </summary>
      public MatType Type { get; set; } = MatType.None;

      /// <summary>
      /// Тип расчета.
      /// </summary>
      public CalcType TypeCalc { get; set; } = CalcType.N;

      /// <summary>
      /// Тип расчета.
      /// </summary>
      public Dampness Dampness { get; set; } = Dampness.any;

      public override string ToString()
      {
         return $"{Tag}";
      }

      public MaterialChars()
      {

      }

      public MaterialChars(CalcType calcType)
      {
         TypeCalc = calcType;
      }

      /// <summary>
      /// Создает копию данного объекта MaterialChars.
      /// </summary>
      public MaterialChars Clone()
      {
         return new MaterialChars
         {
            Tag = Tag,
            Class = Class,
            Fc = Fc,
            Ft = Ft,
            Ry = Ry,
            Ru = Ru,
            E = E,
            Ec0 = Ec0,
            Ec1 = Ec1,
            Ec2 = Ec2,
            Ec1Red = Ec1Red,
            Et1Red = Et1Red,
            Et0 = Et0,
            Et1 = Et1,
            Et2 = Et2,
            Type = Type,
            TypeCalc = TypeCalc,
            Dampness = Dampness
         };
      }

      /// <summary>
      /// Трёхлинейный сплайн ветви растяжения бетона (общий для L3, SP63, EKB, SP35):
      /// (0,0) → (Et1, 0.6·Ft) → (Et0, Ft) → (Et2, Ft).
      /// </summary>
      LSpline TensionTrilinear() => new(
         new[] { 0.0, Et1, Et0, Et2 },
         new[] { 0.0, 0.6 * Ft, Ft, Ft });

      /// <summary>
      /// Создает двухлинейную диаграмму работы материала.
      /// </summary>
      /// <returns>Объект типа <see cref="Diagramm"/>, представляющий двухлинейную диаграмму работы материала.</returns>
      /// <remarks>
      /// Метод рассчитывает значения деформаций и напряжений для двухлинейной диаграммы работы материала
      /// на основе характеристик материала. Возвращаемый объект <see cref="Diagramm"/> содержит двухлинейную диаграмму
      /// для различных типов материалов, таких как бетон, арматура с физическим пределом текучести и конструкционная сталь.
      /// </remarks>
      /// <exception cref="ArgumentException">Выбрасывается, если тип материала не совместим с диаграммой.</exception>
      public Diagramm D2L()
      {
         double[] xc = new double[3];
         double[] yc = new double[3];
         double[] xt = new double[3];
         double[] yt = new double[3];
         string tag = "";
         switch (Type)
         {
            case MatType.Concrete:
               xc[0] = Ec2; yc[0] = Fc;
               xc[1] = Ec1Red; yc[1] = Fc;
               xc[2] = 0; yc[2] = 0;
               xt[0] = 0; yt[0] = 0;
               xt[1] = Et1Red; yt[1] = Ft;
               xt[2] = Et2; yt[2] = Ft;
               tag = "Двухлинейная по СП63.13330 (бетон)";
               break;
            case MatType.ReSteelF:
               xc[0] = Ec2; yc[0] = Fc;
               xc[1] = Fc / E; yc[1] = Fc;
               xc[2] = 0; yc[2] = 0;
               xt[0] = 0; yt[0] = 0;
               xt[1] = Ft / E; yt[1] = Ft;
               xt[2] = Et2; yt[2] = Ft;
               tag = "Двухлинейная по СП63.13330 (арматура с физическим пределом текучести)";
               break;
            case MatType.ReSteelU:
               throw new ArgumentException("Диаграмма и материал не совместимы");
            case MatType.Steel:
               xc[0] = Ec2; yc[0] = -Ru;
               xc[1] = -Ry / E; yc[1] = -Ry;
               xc[2] = 0; yc[2] = 0;
               xt[0] = 0; yt[0] = 0;
               xt[1] = Ry / E; yt[1] = Ry;
               xt[2] = Et2; yt[2] = Ru;
               tag = "Двухлинейная (сталь конструкционная)";
               break;
            default:
               throw new ArgumentException("Неизвестный материал");
         }
         return new Diagramm(new LSpline(xc, yc), new LSpline(xt, yt), DiagrammType.L2, Type, tag);
      }

      /// <summary>
      /// Многосегментная диаграмма конструкционной стали по СП 16.13330 прил.В (табл.В.9, рис.В.1).
      /// Симметрична: растяжение и сжатие имеют одинаковые характеристики.
      /// </summary>
      /// <param name="hasYieldPlateau">True — с площадкой текучести (OACDEF), False — без (OACEF).</param>
      public Diagramm DSP16(bool hasYieldPlateau = true)
      {
         double Ry_kpa = Ry;
         double eps_y = Ry_kpa / E;

         int group = Ry_kpa switch
         {
            <= 290 => 1,
            <= 390 => 2,
            <= 440 => 3,
            <= 500 => 4,
            _      => 5
         };

         // Таблица В.9 — безразмерные параметры ε̅ = ε/ε_y, σ̅ = σ/Ry
         var (eps_pl, sig_pl, eps_yld, eps_st, eps_u, sig_u, eps_t, sig_t) = group switch
         {
            1 => (0.8, 0.8,   1.7, 14.0, 141.6, 1.653, 251.0, 1.35),
            2 => (0.8, 0.8,   1.7, 16.0,  88.3, 1.415, 153.0, 1.26),
            3 => (0.9, 0.9,   1.7, 17.0,  67.1, 1.345, 115.0, 1.23),
            4 => (0.9, 0.9,   1.7, 17.0,  49.6, 1.33,   87.2, 1.20),
            _ => (0.9, 0.9,   1.7, 18.0,  26.2, 1.16,   51.1, 1.10)
         };

         var pos = new List<double[]>  // ε, σ (МПа)
         {
            new[] { 0.0,                      0.0 },
            new[] { eps_pl * eps_y,           sig_pl * Ry_kpa },
            new[] { eps_yld * eps_y,          Ry_kpa }
         };
         if (hasYieldPlateau)
            pos.Add(new[] { eps_st * eps_y,   Ry_kpa });
         pos.Add(new[] { eps_u * eps_y,       sig_u * Ry_kpa });
         pos.Add(new[] { eps_t * eps_y,       sig_t * Ry_kpa });

         int n = pos.Count;
         var xc = new double[n];
         var yc = new double[n];
         var xt = new double[n];
         var yt = new double[n];

         // Сжатие (ε < 0) — обратный порядок, знак минус
         for (int i = 0; i < n; i++)
         {
            xc[i] = -pos[n - 1 - i][0];
            yc[i] = -pos[n - 1 - i][1];
         }

         // Растяжение (ε > 0) — прямой порядок
         for (int i = 0; i < n; i++)
         {
            xt[i] = pos[i][0];
            yt[i] = pos[i][1];
         }

         return new Diagramm(
            new LSpline(xc, yc),
            new LSpline(xt, yt),
            DiagrammType.SP16, Type,
            hasYieldPlateau
               ? "Многосегментная по СП 16.13330 (сталь конструкционная)"
               : "Многосегментная по СП 16.13330 без площадки (сталь конструкционная)");
      }

      /// <summary>
      /// Создает трехлинейную диаграмму работы материала.
      /// </summary>
      /// <returns>Объект типа <see cref="Diagramm"/>, представляющий трехлинейную диаграмму работы материала.</returns>
      /// <remarks>
      /// Метод рассчитывает значения деформаций и напряжений для трехлинейной диаграммы работы материала
      /// на основе характеристик материала. Возвращаемый объект <see cref="Diagramm"/> содержит трехлинейную диаграмму
      /// для различных типов материалов, таких как бетон и арматура с условным пределом текучести.
      /// </remarks>
      /// <exception cref="ArgumentException">Выбрасывается, если тип материала не совместим с диаграммой.</exception>
      public Diagramm D3L()
      {
         double[] xc = new double[4];
         double[] yc = new double[4];
         double[] xt = new double[4];
         double[] yt = new double[4];
         string tag = "";
         switch (Type)
         {
            case MatType.Concrete:
               xc[0] = Ec2; yc[0] = Fc;
               xc[1] = Ec0; yc[1] = Fc;
               xc[2] = Ec1; yc[2] = 0.6 * Fc;
               xc[3] = 0;   yc[3] = 0;
               tag = "Трехлинейная по СП63.13330 (бетон)";
               return new Diagramm(new LSpline(xc, yc), TensionTrilinear(), DiagrammType.L3, Type, tag);
            case MatType.ReSteelF:
               throw new ArgumentException("Диаграмма и материал не совместимы");
            case MatType.ReSteelU:
               double e0 = Ft / E + 0.002;
               double e1 = 0.9 * Ft / E;
               double e2 = e0 + (e0 - e1);
               xc[0] = -Et2; yc[0] = 1.1 * Fc;
               xc[1] = -e2; yc[1] = 1.1 * Fc;
               xc[2] = -e1; yc[2] = 0.9 * Fc;
               xc[3] = 0; yc[3] = 0;
               xt[0] = 0; yt[0] = 0;
               xt[1] = e1; yt[1] = 0.9 * Ft;
               xt[2] = e2; yt[2] = 1.1 * Ft;
               xt[3] = Et2; yt[3] = 1.1 * Ft;
               tag = "Трехлинейная по СП63.13330 (арматура с условным пределом текучести)";
               break;
            case MatType.Steel:
               throw new ArgumentException("Диаграмма и материал не совместимы");
            default:
               throw new ArgumentException("Неизвестный материал");
         }
         return new Diagramm(new LSpline(xc, yc), new LSpline(xt, yt), DiagrammType.L3, Type, tag);
      }

      /// <summary>
      /// Создает криволинейную диаграмму работы материала.
      /// </summary>
      /// <returns>Объект типа <see cref="Diagramm"/>, представляющий криволинейную диаграмму работы материала.</returns>
      /// <remarks>
      /// Метод рассчитывает значения деформаций, напряжений и жесткостей для криволинейной диаграммы работы материала
      /// на основе характеристик материала. Возвращаемый объект <see cref="Diagramm"/> содержит криволинейную диаграмму
      /// для бетона, построенную по приложению Г СП63.13330.
      /// </remarks>
      /// <exception cref="ArgumentException">Выбрасывается, если тип материала не совместим с диаграммой.</exception>
      /// <param name="sp63EtaMin">Нижняя граница нисходящей ветви (η), по умолчанию 0.85 (СП 63 п. Г.1).</param>
      public Diagramm DCL(double sp63EtaMin = 0.85)
      {
         if (Type != MatType.Concrete)
            throw new ArgumentException("Диаграмма СП63 только для бетона");

         var dgr = SP63.DownBranch(this, 1, sp63EtaMin);
         var xc  = dgr[0]; var yc = dgr[1];
         dgr = SP63.UpBranch(this);
         xc.AddRange(dgr[0]); yc.AddRange(dgr[1]);

         var combined = xc.Select((x, i) => (x, y: yc[i])).OrderBy(p => p.x).ToList();
         var dedup    = new List<(double x, double y)>();
         foreach (var p in combined)
            if (dedup.Count == 0 || Math.Abs(p.x - dedup[^1].x) > 1e-14)
               dedup.Add(p);

         return new Diagramm(
            new CSmath.CSpline(dedup.Select(p => p.x), dedup.Select(p => p.y)),
            TensionTrilinear(), DiagrammType.SP63, Type,
            "Криволинейная по прил.Г СП63.13330 (бетон)");
      }

      /// <summary>
      /// Нелинейная диаграмма бетона по EN 1992-1-1 §3.1.5 (формула Сарджина, ЕКБ).
      /// Ветвь растяжения — трёхлинейная, как в DCL.
      /// </summary>
      public Diagramm DEKB()
      {
         if (Type != MatType.Concrete)
            throw new ArgumentException("Диаграмма ЕКБ только для бетона");

         double Rb  = Math.Abs(Fc);
         double ec1 = Math.Abs(Ec0);   // деформация в вершине кривой (εc1)
         double ecu = Math.Abs(Ec2);   // предельная деформация (εcu1)
         double E   = this.E;

         double k     = 1.05 * E * ec1 / Rb;
         double etaU  = ecu / ec1;     // η на предельной деформации

         // Восходящая ветвь (0..1) + нисходящая (1..etaU) — одним проходом
         const int N = 60;
         var xs = new List<double>(N + 2);
         var ys = new List<double>(N + 2);

         for (int i = 0; i <= N; i++)
         {
            double eta  = etaU * i / N;
            double denom = 1.0 + (k - 2.0) * eta;
            double sig  = denom < 1e-12 ? Rb : Rb * (k * eta - eta * eta) / denom;
            sig = Math.Max(0.0, sig);   // не уходить в отрицательные за пределами кривой
            xs.Add(-eta * ec1);
            ys.Add(-sig);
         }
         // Сентинель слева: продление последнего значения без экстраполяции
         xs.Insert(0, xs[0] - 0.01);
         ys.Insert(0, ys[0]);

         var sorted = xs.Zip(ys, (x, y) => (x, y)).OrderBy(p => p.x).ToList();
         var dedup  = new List<(double x, double y)>();
         foreach (var p in sorted)
            if (dedup.Count == 0 || Math.Abs(p.x - dedup[^1].x) > 1e-14)
               dedup.Add(p);

         return new Diagramm(
            new CSmath.CSpline(dedup.Select(p => p.x), dedup.Select(p => p.y)),
            TensionTrilinear(), DiagrammType.EKB, Type,
            "Нелинейная EN 1992-1-1 §3.1.5 / ЕКБ (бетон)");
      }

      /// <summary>
      /// Параболическо-прямоугольная диаграмма бетона по EN 1992-1-1 §3.1.7 / СП 35 (SP35).
      /// Ветвь растяжения — трёхлинейная, как в DCL.
      /// </summary>
      public Diagramm DSP35()
      {
         if (Type != MatType.Concrete)
            throw new ArgumentException("Диаграмма SP35 только для бетона");

         double Rb  = Math.Abs(Fc);
         double ec2 = Math.Abs(Ec0);   // деформация вершины параболы (εc2)
         double ecu = Math.Abs(Ec2);   // предельная деформация (εcu2)
         const int n = 2;              // показатель параболы

         // Параболический участок: 0 .. εc2  (50 точек)
         const int N = 50;
         var xs = new List<double>(N + 4);
         var ys = new List<double>(N + 4);

         for (int i = 0; i <= N; i++)
         {
            double e   = ec2 * i / N;                        // |ε| ∈ [0, εc2]
            double sig = Rb * (1.0 - Math.Pow(1.0 - e / ec2, n));
            xs.Add(-e);
            ys.Add(-sig);
         }
         // Прямоугольный участок: εc2 .. εcu2
         xs.Add(-ecu);
         ys.Add(-Rb);
         // Сентинель слева: продление площадки
         xs.Insert(0, xs[0] - 0.01);
         ys.Insert(0, -Rb);

         var sorted = xs.Zip(ys, (x, y) => (x, y)).OrderBy(p => p.x).ToList();
         var dedup  = new List<(double x, double y)>();
         foreach (var p in sorted)
            if (dedup.Count == 0 || Math.Abs(p.x - dedup[^1].x) > 1e-14)
               dedup.Add(p);

         // LSpline: кинк в εc2 (парабола → площадка) не размазывается кубическим сплайном
         return new Diagramm(
            new LSpline(dedup.Select(p => p.x).ToArray(), dedup.Select(p => p.y).ToArray()),
            TensionTrilinear(), DiagrammType.SP35, Type,
            "Параболическо-прямоугольная EN 1992-1-1 §3.1.7 / SP35 (бетон)");
      }
   }
}
