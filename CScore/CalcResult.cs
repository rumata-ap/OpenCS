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

      /// <summary>
      /// Строки результата проверки по КЭ — только между расчётом и сохранением: в БД пишутся отдельной
      /// таблицей (их бывают миллионы), в <see cref="DataJson"/> не входят. После загрузки из БД — null.
      /// </summary>
      public IReadOnlyList<Fem.FemCheckRow>? FemCheckRows { get; set; }

      public override string ToString() => $"{Id}#{TaskKind} [{Status}] {Created}";
   }
}
