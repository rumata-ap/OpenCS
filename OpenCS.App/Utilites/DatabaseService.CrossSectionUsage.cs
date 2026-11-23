namespace OpenCS.Utilites
{
   /// <summary>Часть DatabaseService: кто ссылается на сечение (для запрета удаления используемого).</summary>
   public partial class DatabaseService
   {
      /// <summary>
      /// Ссылки на сечение из таблиц, не загруженных в пулы: задачи расчёта, сечения огнестойкости, КонЭ и КЭ схем МКЭ.
      /// </summary>
      public (List<string> Tasks, List<string> FireSections, int FemMembers, int FemElements) CrossSectionReferences(int sectionId)
      {
         var tasks = UsageStrings("SELECT tag FROM calc_tasks WHERE section_id=@id ORDER BY num", sectionId);
         var fire = UsageStrings("SELECT tag FROM fire_sections WHERE section_id=@id ORDER BY num", sectionId);
         int members = UsageCount("SELECT COUNT(*) FROM fem_members WHERE cross_section_id=@id", sectionId);
         int elements = UsageCount("SELECT COUNT(*) FROM fem_elements WHERE cross_section_id=@id", sectionId);
         return (tasks, fire, members, elements);
      }

      List<string> UsageStrings(string sql, int id)
      {
         var list = new List<string>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = sql;
         cmd.Parameters.AddWithValue("@id", id);
         using var r = cmd.ExecuteReader();
         while (r.Read()) list.Add(r.IsDBNull(0) ? "" : r.GetString(0));
         return list;
      }

      int UsageCount(string sql, int id)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = sql;
         cmd.Parameters.AddWithValue("@id", id);
         return Convert.ToInt32(cmd.ExecuteScalar());
      }
   }
}
