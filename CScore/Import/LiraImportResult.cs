namespace CScore.Import
{
   /// <summary>Результат импорта HTML LIRA SAPR.</summary>
   public class LiraImportResult
   {
      public List<ForceSet> ForceSets { get; } = [];
      public List<string> Warnings { get; } = [];
      public string? Error { get; set; }

      public bool Success => Error == null;
   }
}
