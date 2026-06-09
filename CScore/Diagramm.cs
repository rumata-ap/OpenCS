using CSmath;
using System.Text.Json.Serialization;

using System;
using System.Collections.Generic;

namespace CScore
{
   /// <summary>
   /// Диаграмма работы материала — определяет зависимость σ(ε) для сжатия
   /// и растяжения. Содержит два сплайна: Ic — для сжатия, It — для растяжения.
   /// Позволяет вычислять напряжение и касательный модуль упругости
   /// для заданной деформации.
   /// </summary>
   [Serializable]
   public class Diagramm
   {
      /// <summary>
      /// Внутренняя ссылка на материал (не сериализуется).
      /// </summary>
      [JsonIgnore]
      internal Material material;

      /// <summary>
      /// Первичный ключ.
      /// </summary>
      public int Id { get; set; }

      /// <summary>
      /// Идентификатор материала-источника.
      /// </summary>
      public int MaterialId { get; set; }

      /// <summary>
      /// Вид расчёта, для которого построена диаграмма.
      /// </summary>
      public CalcType CalcType { get; set; }

      /// <summary>
      /// Наименование диаграммы.
      /// </summary>
      public string Tag { get; set; }

      /// <summary>
      /// Тип диаграммы (L2, L3, SP63 и др.).
      /// </summary>
      public DiagrammType Type { get; set; }

      /// <summary>
      /// Тип материала (бетон, арматура и др.), для которого построена диаграмма.
      /// </summary>
      public MatType MaterialType { get; set; }

      /// <summary>
      /// Сплайн для сжатия: вычисляет σ(ε) и dσ/dε при ε &lt; 0.
      /// </summary>
      [JsonIgnore]
      public ISpline Ic { get; set; }

      /// <summary>
      /// Сплайн для растяжения: вычисляет σ(ε) и dσ/dε при ε &gt; 0.
      /// </summary>
      [JsonIgnore]
      public ISpline It { get; set; }

      /// <summary>
      /// Конструктор по умолчанию.
      /// </summary>
      public Diagramm() { }

      /// <summary>
      /// Создаёт диаграмму с заданными сплайнами сжатия и растяжения.
      /// </summary>
      /// <param name="interpolant_compression">Сплайн для ветви сжатия.</param>
      /// <param name="interpolant_tension">Сплайн для ветви растяжения.</param>
      /// <param name="diagrammType">Тип диаграммы.</param>
      /// <param name="matType">Тип материала.</param>
      /// <param name="tag">Наименование диаграммы.</param>
      public Diagramm(ISpline interpolant_compression, ISpline interpolant_tension, DiagrammType diagrammType, MatType matType, string tag = "")
      {
         Ic = interpolant_compression;
         It = interpolant_tension;
         Type = diagrammType;
         MaterialType = matType;
         Tag = tag;
      }

      /// <summary>
      /// Вычисляет напряжение и касательный модуль упругости по заданной деформации.
      /// Логика ветвления зависит от типа материала и флагов учёта растяжения/сжатия:
      /// - Бетон: сжатие всегда учитывается, растяжение — по флагу <paramref name="tenB"/>.
      /// - Арматура: растяжение всегда учитывается, сжатие — по флагу <paramref name="comprA"/>.
      /// </summary>
      /// <param name="eps">Деформация волокна (с учётом преддеформации).</param>
      /// <param name="E2">Выходной параметр: касательный модуль упругости dσ/dε.</param>
      /// <param name="tenB">Учитывать работу на растяжение (для бетона).</param>
      /// <param name="comprA">Учитывать работу на сжатие (для арматуры).</param>
      /// <returns>Напряжение σ при заданной деформации ε [МПа].</returns>
      public virtual double Sig(double eps, out double E2, bool tenB = true, bool comprA = true)
      {
         E2 = 0; double sig = 0;
         if (eps > 0 && tenB && MaterialType == MatType.Concrete) E2 = It.Derivative(eps, out sig);
         else if (eps < 0 && MaterialType == MatType.Concrete) E2 = Ic.Derivative(eps, out sig);
         else if (eps < 0 && comprA && (MaterialType == MatType.ReSteelF || MaterialType == MatType.ReSteelU)) E2 = Ic.Derivative(eps, out sig);
         else if (eps > 0 && (MaterialType == MatType.ReSteelF || MaterialType == MatType.ReSteelU)) E2 = It.Derivative(eps, out sig);
         else sig = 0;

         return sig;
      }

      /// <summary>
      /// Вычисляет напряжение и усилия для отдельного волокна (конечного элемента).
      /// Задаёт поля Sig, N, My, Mz, E, E2 объекта <see cref="Fiber"/>.
      /// </summary>
      /// <param name="finiteArea">Волокно, для которого вычисляются напряжения.</param>
      /// <param name="tenB">Учитывать работу на растяжение.</param>
      /// <param name="comprA">Учитывать работу на сжатие.</param>
      public virtual void Sig(Fiber finiteArea, bool tenB = true, bool comprA = true)
      {
         double eps = finiteArea.Eps + finiteArea.Eps_p;
         finiteArea.Sig = Sig(eps, out double e2, tenB, comprA);
         finiteArea.N = finiteArea.Sig * finiteArea.Area;
         finiteArea.My = finiteArea.Sig * finiteArea.Area * finiteArea.Y;
         finiteArea.Mz = finiteArea.Sig * finiteArea.Area * finiteArea.X;
         finiteArea.E = (Math.Abs(eps) > 1e-20) ? finiteArea.Sig / eps : 0;
         finiteArea.E2 = e2;
      }

      /// <summary>
      /// Вычисляет напряжение и секущий модуль упругости для точки контура
      /// (<see cref="StressPoint"/>).
      /// </summary>
      /// <param name="stressPoint">Точка контура сечения.</param>
      /// <param name="tenB">Учитывать работу на растяжение.</param>
      /// <param name="comprA">Учитывать работу на сжатие.</param>
      public virtual void Sig(StressPoint stressPoint, bool tenB = true, bool comprA = true)
      {
         double sig = Sig(stressPoint.Eps + stressPoint.Eps_p, out double e2, tenB, comprA);
         stressPoint.Sig = sig;
         stressPoint.E = (Math.Abs(stressPoint.Eps + stressPoint.Eps_p) > 1e-20) ? sig / (stressPoint.Eps + stressPoint.Eps_p) : 0;
         stressPoint.E2 = e2;
      }

      /// <summary>
      /// Вычисляет напряжения для всех волокон материальной области.
      /// Вызывает <see cref="Sig(Fiber, bool, bool)"/> для каждого волокна.
      /// </summary>
      /// <param name="area">Материальная область.</param>
      /// <param name="tenB">Учитывать работу на растяжение.</param>
      /// <param name="comprA">Учитывать работу на сжатие.</param>
      public virtual void Sig(MaterialArea area, bool tenB = true, bool comprA = true)
      {
         for (int i = 0; i < area.Fibers.Count; i++)
            Sig(area.Fibers[i], tenB, comprA);
      }

      /// <summary>
      /// Создаёт разностную диаграмму: σ_eff(ε) = σ_steel(ε) − σ_concrete(ε).
      /// Используется для арматурных областей, вложенных в бетонную (брутто-сечение).
      /// </summary>
      public static Diagramm Differential(Diagramm steel, Diagramm concrete)
      {
         return new Diagramm(
            new CSmath.DifferentialSpline(steel.Ic, concrete.Ic),
            new CSmath.DifferentialSpline(steel.It, concrete.It),
            steel.Type,
            steel.MaterialType,
            $"diff({steel.Tag}−{concrete.Tag})"
         );
      }
   }

   /// <summary>
   /// Тип диаграммы работы материала:
   /// L2 — двухлинейная, L3 — трёхлинейная, SP63 — криволинейная по СП 63.13330,
   /// EKB — по Единым каталогам, SP35 — по СП 35.
   /// </summary>
   public enum DiagrammType { L2, L3, EKB, SP63, SP35 }
}