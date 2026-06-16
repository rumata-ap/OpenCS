namespace CScore
{
   /// <summary>
   /// Расчётная задача проекта. Ссылается на сечение, набор усилий и конкретную строку.
   /// </summary>
   public class CalcTask
   {
      public int      Id          { get; set; }
      public int      Num         { get; set; }
      public string   Tag         { get; set; } = "";

      /// <summary>Тип задачи: "strain_state" и др.</summary>
      public string   Kind        { get; set; } = "strain_state";

      public int      SectionId   { get; set; }
      public int      ForceSetId  { get; set; }
      public int      ForceItemId { get; set; }

      /// <summary>Тип расчёта (C/CL/N/NL).</summary>
      public CalcType CalcType    { get; set; } = CalcType.C;

      public override string ToString() => $"{Num:D3}#CalcTask : {Tag}";
   }
}
