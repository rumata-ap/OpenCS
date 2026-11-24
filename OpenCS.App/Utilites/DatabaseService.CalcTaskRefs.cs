using CScore;

namespace OpenCS.Utilites
{
   /// <summary>Строки наборов для задач без загрузки всего набора (наборы РСУ бывают в миллионы строк).</summary>
   public partial class DatabaseService
   {
      /// <summary>
      /// Строка набора по Id: набор, номер, метка; null — строки нет. Строки стержней и пластин — в разных таблицах,
      /// их Id могут совпадать, поэтому вид указывается явно.
      /// </summary>
      public (int SetId, int Num, string Label)? ForceItemRef(int itemId, bool shell = false)
      {
         if (itemId == 0) return null;
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = $"SELECT set_id, num, label FROM {(shell ? "force_shell_items" : "force_items")} WHERE id=@id";
         cmd.Parameters.AddWithValue("@id", itemId);
         using var r = cmd.ExecuteReader();
         return r.Read() ? (r.GetInt32(0), r.GetInt32(1), r.IsDBNull(2) ? "" : r.GetString(2)) : null;
      }

      /// <summary>Id строк набора с номером <paramref name="num"/> (обычно одна; больше — номер неоднозначен).</summary>
      public List<int> ForceItemIdsByNum(int setId, int num, bool shell = false)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = $"SELECT id FROM {(shell ? "force_shell_items" : "force_items")} WHERE set_id=@sid AND num=@num ORDER BY id";
         cmd.Parameters.AddWithValue("@sid", setId);
         cmd.Parameters.AddWithValue("@num", num);
         var ids = new List<int>();
         using var r = cmd.ExecuteReader();
         while (r.Read()) ids.Add(r.GetInt32(0));
         return ids;
      }
   }
}
