using CScore;
using Microsoft.Data.Sqlite;

namespace OpenCS.Utilites
{
   /// <summary>
   /// Строки наборов усилий по требованию (<see cref="IForceSetRowSource"/>). Наборы РСУ из МКЭ-программ
   /// бывают в миллионы строк: при открытии проекта читаются только заголовки, строки — при первом
   /// обращении к набору. Чтение идёт через своё соединение: его вызывают и фоновые расчёты, а общее
   /// соединение <c>_connection</c> принадлежит потоку интерфейса.
   /// </summary>
   public partial class DatabaseService : IForceSetRowSource
   {
      SqliteConnection OpenReadConnection()
      {
         var conn = new SqliteConnection($"Data Source={_dataSource}");
         conn.Open();
         return conn;
      }

      /// <inheritdoc/>
      public (List<LoadItem> Items, List<ShellLoadItem> ShellItems) LoadRows(int setId)
      {
         using var conn = OpenReadConnection();
         return (ReadBarRows(conn, setId, ""), ReadShellRows(conn, setId, ""));
      }

      /// <inheritdoc/>
      public List<LoadItem> LoadBarRows(int setId, int? fromElem, int? toElem)
      {
         using var conn = OpenReadConnection();
         return ReadBarRows(conn, setId, ElementRangeFilter(fromElem, toElem), fromElem, toElem);
      }

      /// <inheritdoc/>
      public List<ShellLoadItem> LoadShellRows(int setId, int? fromElem, int? toElem)
      {
         using var conn = OpenReadConnection();
         return ReadShellRows(conn, setId, ElementRangeFilter(fromElem, toElem), fromElem, toElem);
      }

      /// <summary>Условие на номер КЭ строки: диапазон или «без номера» (индекс (set_id, source_elem_num), v75).</summary>
      static string ElementRangeFilter(int? fromElem, int? toElem) =>
         fromElem != null && toElem != null
            ? " AND source_elem_num BETWEEN @from AND @to"
            : " AND source_elem_num IS NULL";

      static void AddRangeParameters(SqliteCommand cmd, int? fromElem, int? toElem)
      {
         if (fromElem == null || toElem == null) return;
         cmd.Parameters.AddWithValue("@from", fromElem.Value);
         cmd.Parameters.AddWithValue("@to", toElem.Value);
      }

      static List<LoadItem> ReadBarRows(SqliteConnection conn, int setId, string filter, int? fromElem = null, int? toElem = null)
      {
         var items = new List<LoadItem>();
         using var cmd = conn.CreateCommand();
         cmd.CommandText = $"SELECT id, num, label, n, mx, my, vx, vy, t, source_elem_num, source_section_num FROM force_items WHERE set_id=@sid{filter} ORDER BY num";
         cmd.Parameters.AddWithValue("@sid", setId);
         AddRangeParameters(cmd, fromElem, toElem);
         using var r = cmd.ExecuteReader();
         while (r.Read())
            items.Add(new LoadItem
            {
               Id    = r.GetInt32(0),
               Num   = r.GetInt32(1),
               Label = r.GetString(2),
               N     = r.GetDouble(3),
               Mx    = r.GetDouble(4),
               My    = r.GetDouble(5),
               Vx    = r.GetDouble(6),
               Vy    = r.GetDouble(7),
               T     = r.GetDouble(8),
               SourceElementNum = r.IsDBNull(9) ? null : r.GetInt32(9),
               SourceSectionNum = r.IsDBNull(10) ? null : r.GetInt32(10),
            });
         return items;
      }

      static List<ShellLoadItem> ReadShellRows(SqliteConnection conn, int setId, string filter, int? fromElem = null, int? toElem = null)
      {
         var shellItems = new List<ShellLoadItem>();
         using var cmd = conn.CreateCommand();
         cmd.CommandText = $"SELECT id, num, label, nx, ny, nxy, mx, my, mxy, qx, qy, sigma_x, sigma_y, tau_xy, source_elem_num, source_section_num FROM force_shell_items WHERE set_id=@sid{filter} ORDER BY num";
         cmd.Parameters.AddWithValue("@sid", setId);
         AddRangeParameters(cmd, fromElem, toElem);
         using var r = cmd.ExecuteReader();
         while (r.Read())
            shellItems.Add(new ShellLoadItem
            {
               Id      = r.GetInt32(0),
               Num     = r.GetInt32(1),
               Label   = r.GetString(2),
               Nx      = r.GetDouble(3),
               Ny      = r.GetDouble(4),
               Nxy     = r.GetDouble(5),
               Mx      = r.GetDouble(6),
               My      = r.GetDouble(7),
               Mxy     = r.GetDouble(8),
               Qx      = r.GetDouble(9),
               Qy      = r.GetDouble(10),
               SigmaX  = r.IsDBNull(11) ? null : r.GetDouble(11),
               SigmaY  = r.IsDBNull(12) ? null : r.GetDouble(12),
               TauXY   = r.IsDBNull(13) ? null : r.GetDouble(13),
               SourceElementNum = r.IsDBNull(14) ? null : r.GetInt32(14),
               SourceSectionNum = r.IsDBNull(15) ? null : r.GetInt32(15),
            });
         return shellItems;
      }

      /// <inheritdoc/>
      public ForceSetElementStats ElementStats(int setId, bool shell)
      {
         using var conn = OpenReadConnection();
         using var cmd = conn.CreateCommand();
         cmd.CommandText = $"SELECT source_elem_num, COUNT(*) FROM {(shell ? "force_shell_items" : "force_items")} WHERE set_id=@sid GROUP BY source_elem_num";
         cmd.Parameters.AddWithValue("@sid", setId);
         var byElement = new Dictionary<int, int>();
         int without = 0;
         using var r = cmd.ExecuteReader();
         while (r.Read())
         {
            if (r.IsDBNull(0)) without += r.GetInt32(1);
            else byElement[r.GetInt32(0)] = r.GetInt32(1);
         }
         return new ForceSetElementStats(byElement, without);
      }
   }
}
