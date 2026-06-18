using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CScore
{
   /// <summary>
   /// Двухэтапное поперечное сечение — для сборно-монолитных конструкций
   /// и усиления ЖБ сечений.
   /// Этап 1: замороженная кривизна плоскости деформаций Stage1Kurvature.
   /// Деформации волокон этапа 1: ε_total = ε_current + ε_stage1.
   /// Этап 2 (Areas из базового CrossSection): ε_total = ε_current.
   /// </summary>
   [Serializable]
   public class TwoStageSection : CrossSection
   {
      /// <summary>Сечение первого этапа (до усиления / омоноличивания).</summary>
      public CrossSection Stage1 { get; set; } = new();

      /// <summary>Id сечения первого этапа в БД.</summary>
      public int Stage1SectionId { get; set; }

      /// <summary>
      /// Замороженная кривизна плоскости деформаций от нагрузки первого этапа.
      /// Транзитное поле: вычисляется обработчиком расчётной задачи (решением сечения
      /// этапа 1 под усилием этапа 1) перед расчётом этапа 2. В БД не сохраняется.
      /// </summary>
      public Kurvature Stage1Kurvature { get; set; }

       /// <inheritdoc/>
       /// <remarks>
       /// Для составного сечения под инкрементом <paramref name="baseK"/> (= κ2) возвращает:
       /// области этапа 1 с эффективной плоскостью <c>baseK + Stage1Kurvature</c>
       /// (суммарная деформация ε(κ1) + ε(κ2)) и области этапа 2 с плоскостью <c>baseK</c>.
       /// </remarks>
       public override IEnumerable<(MaterialArea area, Kurvature k)> EnumerateAreas(Kurvature baseK)
       {
          Kurvature k1Total = baseK + Stage1Kurvature;
          foreach (var a in Stage1.Areas) yield return (a, k1Total);
          foreach (var a in Areas)        yield return (a, baseK);
       }

       public TwoStageSection() { }

       /// <summary>
       /// Начальное приближение приращения кривизны κ2 для этапа 2.
       /// Жёсткость берётся по <b>составному</b> сечению (области этапа 1 + этапа 2),
       /// а не только по усиливающей части. Иначе деление полного усилия на малую
       /// жёсткость одной усиливающей области даёт завышенное в разы κ2, и Ньютон
       /// стартует в нелинейной/разрушенной зоне → расходимость.
       /// </summary>
       public override Kurvature Guess(Load load)
       {
          var pr = ElasticProps(Stage1.Areas.Concat(Areas));
          return GuessFromProps(pr, load);
       }

      // SetEps и Integral НЕ переопределяются: базовая реализация CrossSection
      // итерирует EnumerateAreas(k) и поддерживает контурный путь (теорема Грина)
      // для плоских областей без сетки фибр. Наивное суммирование area.Fibers
      // теряло бетон контурных областей обеих стадий — несходимость этапа 2.

      /// <inheritdoc/>
      public override CrossSection CloneForCalc() => new TwoStageSection
      {
         Id               = Id,
         Num              = Num,
         Tag              = Tag,
         Description      = Description,
         Areas            = Areas.Select(a => a.CloneForCalc()).ToList(),
         Stage1SectionId  = Stage1SectionId,
         Stage1Kurvature  = Stage1Kurvature,
         Stage1           = new CrossSection
         {
            Id    = Stage1.Id,
            Tag   = Stage1.Tag,
            Areas = Stage1.Areas.Select(a => a.CloneForCalc()).ToList()
         }
      };
   }
}
