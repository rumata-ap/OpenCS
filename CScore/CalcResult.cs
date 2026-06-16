namespace CScore
{
   /// <summary>
   /// Результат выполнения расчётной задачи.
   /// </summary>
   public class CalcResult
   {
      public int    Id       { get; set; }
      public int    TaskId   { get; set; }
      public string TaskKind { get; set; } = "";
      public string TaskTag  { get; set; } = "";
      public string Created  { get; set; } = "";

      /// <summary>Статус: "ok", "error", "not_converged".</summary>
      public string Status   { get; set; } = "ok";

      /// <summary>JSON-словарь с результатами конкретного вида задачи.</summary>
      public string DataJson { get; set; } = "{}";

      public override string ToString() => $"{Id}#{TaskKind} [{Status}] {Created}";
   }
}
