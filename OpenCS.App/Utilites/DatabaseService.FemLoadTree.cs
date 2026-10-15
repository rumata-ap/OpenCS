using CScore.Fem;

namespace OpenCS.Utilites
{
   /// <summary>Загружения и комбинации в дереве схемы: активное загружение.</summary>
   public partial class DatabaseService
   {
      const string ActiveLoadCasesKey = "fem_active_load_cases";

      /// <summary>
      /// Активное загружение схемы (как в RFEM) — настройка вида, не данные схемы: хранится в таблице настроек проекта
      /// картой «схема → имя загружения» (Id загружений меняются при сохранении схемы); null — не выбрано.
      /// </summary>
      public string? GetFemActiveLoadCaseTag(int schemaId) =>
         ReadActiveLoadCases().TryGetValue(schemaId.ToString(System.Globalization.CultureInfo.InvariantCulture), out var tag) ? tag : null;

      /// <summary>Запоминает активное загружение схемы (null — забыть).</summary>
      public void SetFemActiveLoadCaseTag(int schemaId, string? tag)
      {
         var map = ReadActiveLoadCases();
         string key = schemaId.ToString(System.Globalization.CultureInfo.InvariantCulture);
         if (tag != null) map[key] = tag; else map.Remove(key);
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "INSERT OR REPLACE INTO settings (key, value_json) VALUES (@key, @json)";
         cmd.Parameters.AddWithValue("@key", ActiveLoadCasesKey);
         cmd.Parameters.AddWithValue("@json", System.Text.Json.JsonSerializer.Serialize(map));
         cmd.ExecuteNonQuery();
      }

      Dictionary<string, string> ReadActiveLoadCases()
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT value_json FROM settings WHERE key=@key";
         cmd.Parameters.AddWithValue("@key", ActiveLoadCasesKey);
         if (cmd.ExecuteScalar() is not string json) return [];
         try { return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? []; }
         catch (System.Text.Json.JsonException) { return []; }
      }
   }
}
