using Newtonsoft.Json;

namespace OpenCS.Utilites
{
   public class CsvExportSettings
   {
      [JsonProperty("delim")]
      public string Delimiter { get; set; } = ";";

      [JsonProperty("enc")]
      public string Encoding { get; set; } = "windows-1251";

      public static CsvExportSettings Default => new();

      public CsvExportSettings Clone() => new()
      {
         Delimiter = Delimiter, Encoding = Encoding
      };
   }
}
