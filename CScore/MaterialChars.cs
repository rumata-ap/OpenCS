using CSmath;

using Newtonsoft.Json;

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
               xc[3] = 0; yc[3] = 0;
               xt[0] = 0; yt[0] = 0;
               xt[1] = Et1; yt[1] = 0.6 * Ft;
               xt[2] = Et0; yt[2] = Ft;
               xt[3] = Et2; yt[3] = Ft;
               tag = "Трехлинейная по СП63.13330 (бетон)";
               break;
            case MatType.ReSteelF:
               throw new ArgumentException("Диаграмма и материал не совместимы");
            case MatType.ReSteelU:
               double e0 = Ft / E + 0.002;
               double e1 = 0.9 * Ft / E;
               double e2 = e0 + (e0 - e1);
               xc[0] = Ec2; yc[0] = 1.1 * Fc;
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
      public Diagramm DCL()
      {
         List<double> xc = null;
         List<double> yc = null;
         List<double> dyc = null;
         double[] xt = new double[4];
         double[] yt = new double[4];
         string tag = "";
         switch (Type)
         {
            case MatType.Concrete:
               var dgr = SP63.DownBranch(this);
               xc = dgr[0]; yc = dgr[1]; dyc = dgr[2];
               dgr = SP63.UpBranch(this);
               xc.AddRange(dgr[0]); yc.AddRange(dgr[1]); dyc.AddRange(dgr[2]);
               xt[1] = Et1; yt[1] = 0.6 * Ft;
               xt[2] = Et0; yt[2] = Ft;
               xt[3] = Et2; yt[3] = Ft;
               tag = "Криволинейная по прил.Г СП63.13330 (бетон)";
               break;
            case MatType.ReSteelF:
               throw new ArgumentException("Диаграмма и материал не совместимы");
            case MatType.ReSteelU:
               throw new ArgumentException("Диаграмма и материал не совместимы");
            case MatType.Steel:
               throw new ArgumentException("Диаграмма и материал не совместимы");
            default:
               throw new ArgumentException("Неизвестный материал");
         }
           var combined = xc.Select((x, i) => (x, y: yc[i]))
                             .OrderBy(p => p.x)
                             .ToList();
           var dedup = new List<(double x, double y)>();
           foreach (var p in combined)
              if (dedup.Count == 0 || Math.Abs(p.x - dedup[^1].x) > 1e-14)
                 dedup.Add(p);
           return new Diagramm(
              new LSpline(dedup.Select(p => p.x).ToArray(), dedup.Select(p => p.y).ToArray()),
              new LSpline(xt, yt), DiagrammType.SP63, Type, tag);
      }
   }
}
