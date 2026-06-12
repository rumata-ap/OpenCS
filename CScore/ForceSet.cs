using System.Collections.Generic;

namespace CScore
{
   /// <summary>
   /// Одна строка в наборе усилий — сочетание N, My, Mz для заданного типа расчёта.
   /// </summary>
   public class LoadItem
   {
      public int Id { get; set; }
      public int Num { get; set; }
      public string Tag { get; set; } = "";
      public double N { get; set; }
      public double My { get; set; }
      public double Mz { get; set; }
      public CalcType CalcType { get; set; } = CalcType.C;

      /// <summary>Преобразует строку в структуру Load для расчёта.</summary>
      public Load ToLoad() => new Load { N = N, My = My, Mz = Mz, Calc = CalcType };

      public override string ToString() => $"{Num:D3}: N={N:G4}  My={My:G4}  Mz={Mz:G4}  [{CalcType}]";
   }

   /// <summary>
   /// Именованный набор расчётных усилий (N, My, Mz) для поперечного сечения.
   /// </summary>
   public class ForceSet
   {
      public int Id { get; set; }
      public int Num { get; set; }
      public string Tag { get; set; } = "";
      public string? Description { get; set; }

      public List<LoadItem> Items { get; set; } = [];

      public override string ToString() => $"{Num:D3}#ForceSet : {Tag}";
   }
}
