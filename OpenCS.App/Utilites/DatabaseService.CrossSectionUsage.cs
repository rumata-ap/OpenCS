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
         // Задачи пластин (shell_*) ссылаются на сечение пластины с тем же числовым Id — не на стержневое.
         var tasks = UsageStrings("SELECT tag FROM calc_tasks WHERE section_id=@id AND kind NOT LIKE 'shell\\_%' ESCAPE '\\' ORDER BY num", sectionId);
         var fire = UsageStrings("SELECT tag FROM fire_sections WHERE section_id=@id ORDER BY num", sectionId);
         int members = UsageCount("SELECT COUNT(*) FROM fem_members WHERE cross_section_id=@id", sectionId);
         int elements = UsageCount("SELECT COUNT(*) FROM fem_elements WHERE cross_section_id=@id", sectionId);
         return (tasks, fire, members, elements);
      }

      /// <summary>
      /// Ссылки на сечение пластины: задачи пластин (shell_*), КонЭ и группы КЭ схем МКЭ, эквивалентные сечения полос.
      /// </summary>
      public (List<string> Tasks, int FemMembers, List<string> FemGroups, List<string> Equivalents) PlateSectionReferences(int plateSectionId)
      {
         var tasks = UsageStrings("SELECT tag FROM calc_tasks WHERE section_id=@id AND kind LIKE 'shell\\_%' ESCAPE '\\' ORDER BY num", plateSectionId);
         int members = UsageCount("SELECT COUNT(*) FROM fem_members WHERE plate_section_id=@id", plateSectionId);
         var groups = UsageStrings("SELECT tag FROM fem_member_groups WHERE plate_section_id=@id ORDER BY tag", plateSectionId);
         var equivalents = UsageStrings("SELECT tag FROM equivalent_sections WHERE source_plate_section_id=@id ORDER BY num", plateSectionId);
         return (tasks, members, groups, equivalents);
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
