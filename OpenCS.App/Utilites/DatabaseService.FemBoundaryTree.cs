namespace OpenCS.Utilites
{
   /// <summary>
   /// Сводки ГУ, принадлежащих КЭ сетки (шарниры стержней, упругое основание), для дерева схемы: группировка и
   /// номера КЭ — запросами, без чтения всей сетки (импортированные схемы — сотни тысяч КЭ).
   /// </summary>
   public partial class DatabaseService
   {
      /// <summary>Сочетания освобождений концов I/J стержней схемы (0 — жёсткое примыкание) и число КЭ с каждым.</summary>
      public List<(int ReleaseI, int ReleaseJ, int Count)> GetFemElementReleaseGroups(int schemaId)
      {
         var result = new List<(int, int, int)>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT COALESCE(release_i, 0) AS ri, COALESCE(release_j, 0) AS rj, COUNT(*)
            FROM fem_elements WHERE schema_id=@sid AND (COALESCE(release_i, 0) <> 0 OR COALESCE(release_j, 0) <> 0)
            GROUP BY ri, rj ORDER BY ri, rj
            """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var r = cmd.ExecuteReader();
         while (r.Read()) result.Add((r.GetInt32(0), r.GetInt32(1), r.GetInt32(2)));
         return result;
      }

      /// <summary>Номера КЭ схемы с освобождениями концов <paramref name="releaseI"/> / <paramref name="releaseJ"/>.</summary>
      public List<string> GetFemElementTagsByReleases(int schemaId, int releaseI, int releaseJ)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT elem_tag FROM fem_elements
            WHERE schema_id=@sid AND COALESCE(release_i, 0)=@ri AND COALESCE(release_j, 0)=@rj ORDER BY id
            """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         cmd.Parameters.AddWithValue("@ri", releaseI);
         cmd.Parameters.AddWithValue("@rj", releaseJ);
         return ReadStrings(cmd);
      }

      /// <summary>Коэффициенты постели C1 пластин схемы, Н/м³, и число КЭ с каждым.</summary>
      public List<(double C1, int Count)> GetFemFoundationGroups(int schemaId)
      {
         var result = new List<(double, int)>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT foundation_c1, COUNT(*) FROM fem_elements
            WHERE schema_id=@sid AND foundation_c1 IS NOT NULL AND foundation_c1 <> 0
            GROUP BY foundation_c1 ORDER BY foundation_c1
            """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var r = cmd.ExecuteReader();
         while (r.Read()) result.Add((r.GetDouble(0), r.GetInt32(1)));
         return result;
      }

      /// <summary>Номера КЭ схемы с коэффициентом постели <paramref name="c1"/> (значение из <see cref="GetFemFoundationGroups"/>).</summary>
      public List<string> GetFemElementTagsByFoundation(int schemaId, double c1)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT elem_tag FROM fem_elements WHERE schema_id=@sid AND foundation_c1=@c1 ORDER BY id";
         cmd.Parameters.AddWithValue("@sid", schemaId);
         cmd.Parameters.AddWithValue("@c1", c1);
         return ReadStrings(cmd);
      }

      static List<string> ReadStrings(Microsoft.Data.Sqlite.SqliteCommand cmd)
      {
         var result = new List<string>();
         using var r = cmd.ExecuteReader();
         while (r.Read()) result.Add(r.GetString(0));
         return result;
      }
   }
}
