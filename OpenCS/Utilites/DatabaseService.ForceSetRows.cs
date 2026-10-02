using CScore;
using Microsoft.Data.Sqlite;
using SQLitePCL;

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

      // Чтение строк — напрямую через SQLitePCL (дескриптор соединения Microsoft.Data.Sqlite): на РСУ в миллионы
      // строк обёртки ADO.NET (~150 нс на столбец) давали половину времени загрузки набора.

      /// <summary>Выполняет запрос строк набора и вызывает <paramref name="read"/> на каждую строку результата.</summary>
      static void ReadRows(SqliteConnection conn, string sql, int setId, int? fromElem, int? toElem, Action<sqlite3_stmt> read)
      {
         var db = conn.Handle!;
         // Фоновая запись результата проверки на время фиксации порции блокирует чтение — ждём, а не падаем.
         raw.sqlite3_busy_timeout(db, 30_000);
         int rc = raw.sqlite3_prepare_v2(db, sql, out sqlite3_stmt stmt);
         if (rc != raw.SQLITE_OK) throw new SqliteException(raw.sqlite3_errmsg(db).utf8_to_string(), rc);
         using (stmt)
         {
            raw.sqlite3_bind_int(stmt, raw.sqlite3_bind_parameter_index(stmt, "@sid"), setId);
            if (fromElem is int from && toElem is int to)
            {
               raw.sqlite3_bind_int(stmt, raw.sqlite3_bind_parameter_index(stmt, "@from"), from);
               raw.sqlite3_bind_int(stmt, raw.sqlite3_bind_parameter_index(stmt, "@to"), to);
            }
            while ((rc = raw.sqlite3_step(stmt)) == raw.SQLITE_ROW)
               read(stmt);
            if (rc != raw.SQLITE_DONE) throw new SqliteException(raw.sqlite3_errmsg(db).utf8_to_string(), rc);
         }
      }

      static bool IsNull(sqlite3_stmt s, int i) => raw.sqlite3_column_type(s, i) == raw.SQLITE_NULL;
      static string Text(sqlite3_stmt s, int i) => raw.sqlite3_column_text(s, i).utf8_to_string() ?? "";
      static int? NullableInt(sqlite3_stmt s, int i) => IsNull(s, i) ? null : raw.sqlite3_column_int(s, i);
      static double? NullableDouble(sqlite3_stmt s, int i) => IsNull(s, i) ? null : raw.sqlite3_column_double(s, i);

      static List<LoadItem> ReadBarRows(SqliteConnection conn, int setId, string filter, int? fromElem = null, int? toElem = null)
      {
         var items = new List<LoadItem>();
         ReadRows(conn,
            $"SELECT id, num, label, n, mx, my, vx, vy, t, source_elem_num, source_section_num FROM force_items WHERE set_id=@sid{filter} ORDER BY num",
            setId, fromElem, toElem, r => items.Add(new LoadItem
            {
               Id    = raw.sqlite3_column_int(r, 0),
               Num   = raw.sqlite3_column_int(r, 1),
               Label = Text(r, 2),
               N     = raw.sqlite3_column_double(r, 3),
               Mx    = raw.sqlite3_column_double(r, 4),
               My    = raw.sqlite3_column_double(r, 5),
               Vx    = raw.sqlite3_column_double(r, 6),
               Vy    = raw.sqlite3_column_double(r, 7),
               T     = raw.sqlite3_column_double(r, 8),
               SourceElementNum = NullableInt(r, 9),
               SourceSectionNum = NullableInt(r, 10),
            }));
         return items;
      }

      static List<ShellLoadItem> ReadShellRows(SqliteConnection conn, int setId, string filter, int? fromElem = null, int? toElem = null)
      {
         var shellItems = new List<ShellLoadItem>();
         ReadRows(conn,
            $"SELECT id, num, label, nx, ny, nxy, mx, my, mxy, qx, qy, sigma_x, sigma_y, tau_xy, source_elem_num, source_section_num FROM force_shell_items WHERE set_id=@sid{filter} ORDER BY num",
            setId, fromElem, toElem, r => shellItems.Add(new ShellLoadItem
            {
               Id      = raw.sqlite3_column_int(r, 0),
               Num     = raw.sqlite3_column_int(r, 1),
               Label   = Text(r, 2),
               Nx      = raw.sqlite3_column_double(r, 3),
               Ny      = raw.sqlite3_column_double(r, 4),
               Nxy     = raw.sqlite3_column_double(r, 5),
               Mx      = raw.sqlite3_column_double(r, 6),
               My      = raw.sqlite3_column_double(r, 7),
               Mxy     = raw.sqlite3_column_double(r, 8),
               Qx      = raw.sqlite3_column_double(r, 9),
               Qy      = raw.sqlite3_column_double(r, 10),
               SigmaX  = NullableDouble(r, 11),
               SigmaY  = NullableDouble(r, 12),
               TauXY   = NullableDouble(r, 13),
               SourceElementNum = NullableInt(r, 14),
               SourceSectionNum = NullableInt(r, 15),
            }));
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
