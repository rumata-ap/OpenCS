using CScore.Fem;

namespace OpenCS.Utilites
{
   /// <summary>Нагрузки загружения по видам — счётчики для дерева схемы.</summary>
   public readonly record struct FemLoadCaseCounts(int Node, int Member, int Element, int Kinematic, int MeshNode)
   {
      public int Total => Node + Member + Element + Kinematic + MeshNode;
   }

   /// <summary>Загружения и комбинации в дереве схемы: счётчики нагрузок без чтения самих нагрузок, активное загружение.</summary>
   public partial class DatabaseService
   {
      /// <summary>Число нагрузок каждого вида по загружениям схемы (одним запросом).</summary>
      public Dictionary<int, FemLoadCaseCounts> GetFemLoadCaseCounts(int schemaId)
      {
         var result = new Dictionary<int, FemLoadCaseCounts>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT load_case_id, 0, COUNT(*) FROM fem_node_loads      WHERE schema_id=@sid GROUP BY load_case_id
            UNION ALL SELECT load_case_id, 1, COUNT(*) FROM fem_member_loads    WHERE schema_id=@sid GROUP BY load_case_id
            UNION ALL SELECT load_case_id, 2, COUNT(*) FROM fem_element_loads   WHERE schema_id=@sid GROUP BY load_case_id
            UNION ALL SELECT load_case_id, 3, COUNT(*) FROM fem_kinematic_loads WHERE schema_id=@sid GROUP BY load_case_id
            UNION ALL SELECT load_case_id, 4, COUNT(*) FROM fem_mesh_node_loads WHERE schema_id=@sid GROUP BY load_case_id
            """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var r = cmd.ExecuteReader();
         while (r.Read())
         {
            int id = r.GetInt32(0), kind = r.GetInt32(1), n = r.GetInt32(2);
            var c = result.GetValueOrDefault(id);
            result[id] = kind switch
            {
               0 => c with { Node = n },
               1 => c with { Member = n },
               2 => c with { Element = n },
               3 => c with { Kinematic = n },
               _ => c with { MeshNode = n },
            };
         }
         return result;
      }

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
