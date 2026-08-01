using System.Text.Json.Serialization;

namespace OpenCS.Utilites
{
   public class CsvExportSettings
   {
      [JsonPropertyName("delim")]
      public string Delimiter { get; set; } = ";";

      [JsonPropertyName("enc")]
      public string Encoding { get; set; } = "windows-1251";

      public static CsvExportSettings Default => new();

      public CsvExportSettings Clone() => new()
      {
         Delimiter = Delimiter, Encoding = Encoding
      };
   }
}
