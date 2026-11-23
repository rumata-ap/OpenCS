using System.Text.Json.Nodes;
using CScore;

namespace OpenCS.Utilites
{
   /// <summary>Кто ссылается на набор усилий (для запрета удаления используемого) и запись нескольких наборов разом.</summary>
   public partial class DatabaseService
   {
      /// <summary>Ссылки на набор усилий из задач и схем МКЭ.</summary>
      /// <param name="Tasks">Задачи расчёта: <c>force_set_id</c> или этап <c>StageForce</c> в параметрах.</param>
      /// <param name="FemMembers">Число КонЭ схем с этим набором.</param>
      /// <param name="FemGroups">Группы КЭ схем с этим набором.</param>
      /// <param name="FemChecks">Проверки МКЭ: набор в списке наборов или NL-набор в параметрах.</param>
      /// <param name="ItemIds">Id строк набора, на которые ссылаются задачи (строка задачи, строка этапа).</param>
      public sealed record ForceSetUsage(List<string> Tasks, int FemMembers, List<string> FemGroups,
                                         List<string> FemChecks, HashSet<int> ItemIds)
      {
         /// <summary>Набор кем-то используется.</summary>
         public bool Any => Tasks.Count > 0 || FemMembers > 0 || FemGroups.Count > 0 || FemChecks.Count > 0;
      }

      /// <summary>Ссылки на набор усилий <paramref name="setId"/> в записанных таблицах проекта.</summary>
      public ForceSetUsage ForceSetReferences(int setId)
      {
         var tasks = new List<string>();
         var items = new HashSet<int>();
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = "SELECT tag, force_set_id, force_item_id, params_json FROM calc_tasks ORDER BY num";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
               string tag = r.IsDBNull(0) ? "" : r.GetString(0);
               bool uses = false;
               if (!r.IsDBNull(1) && r.GetInt32(1) == setId)
               {
                  uses = true;
                  if (!r.IsDBNull(2) && r.GetInt32(2) != 0) items.Add(r.GetInt32(2));
               }
               if (!r.IsDBNull(3) && ScanParams(r.GetString(3), setId, items)) uses = true;
               if (uses) tasks.Add(tag);
            }
         }
         int members = UsageCount("SELECT COUNT(*) FROM fem_members WHERE force_set_id=@id", setId);
         var groups = UsageStrings("SELECT tag FROM fem_member_groups WHERE force_set_id=@id ORDER BY tag", setId);
         var checks = new List<string>();
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = "SELECT tag, force_set_ids_json, params_json FROM fem_checks ORDER BY id";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
               bool uses = !r.IsDBNull(1) && IdsContain(r.GetString(1), setId);
               if (!r.IsDBNull(2) && ScanParams(r.GetString(2), setId, null)) uses = true;
               if (uses) checks.Add(r.IsDBNull(0) ? "" : r.GetString(0));
            }
         }
         return new ForceSetUsage(tasks, members, groups, checks, items);
      }

      static bool IdsContain(string json, int id)
      {
         try { return JsonNode.Parse(json) is JsonArray a && a.Any(n => n is JsonValue v && v.TryGetValue(out int x) && x == id); }
         catch (System.Text.Json.JsonException) { return false; }
      }

      /// <summary>
      /// Ищет в параметрах задачи или проверки ссылки на набор: объекты со свойством <c>ForceSetId</c> (этапы
      /// <c>StageForce</c>) или <c>NlForceSetId</c> (регистр не важен); строки этапов (<c>ForceItemId</c>) — в
      /// <paramref name="items"/>.
      /// </summary>
      static bool ScanParams(string json, int setId, HashSet<int>? items)
      {
         JsonNode? root;
         try { root = JsonNode.Parse(json); }
         catch (System.Text.Json.JsonException) { return false; }
         bool found = false;
         void Walk(JsonNode? node)
         {
            switch (node)
            {
               case JsonObject o:
                  foreach (var (key, value) in o)
                  {
                     bool isSetKey = key.Equals("ForceSetId", StringComparison.OrdinalIgnoreCase)
                                     || key.Equals("NlForceSetId", StringComparison.OrdinalIgnoreCase);
                     if (isSetKey && value is JsonValue v && v.TryGetValue(out int id) && id == setId)
                     {
                        found = true;
                        var item = o.FirstOrDefault(p => p.Key.Equals("ForceItemId", StringComparison.OrdinalIgnoreCase)).Value;
                        if (items != null && item is JsonValue iv && iv.TryGetValue(out int itemId) && itemId != 0)
                           items.Add(itemId);
                     }
                     else Walk(value);
                  }
                  break;
               case JsonArray a:
                  foreach (var n in a) Walk(n);
                  break;
            }
         }
         Walk(root);
         return found;
      }

      /// <summary>Записывает новые наборы одной транзакцией: ошибка любого — ни один не записан.</summary>
      public void SaveNewForceSets(IReadOnlyList<ForceSet> sets)
      {
         if (sets.Any(fs => fs.Id != 0)) throw new ArgumentException("only new sets", nameof(sets));
         using var tx = _connection.BeginTransaction();
         try
         {
            foreach (var fs in sets) SaveForceSetCore(fs);
            tx.Commit();
         }
         catch
         {
            tx.Rollback();
            // Строки базы откатились — выданные Id недействительны.
            foreach (var fs in sets)
            {
               fs.Id = 0;
               foreach (var i in fs.Items) i.Id = 0;
               foreach (var i in fs.ShellItems) i.Id = 0;
            }
            throw;
         }
      }
   }
}
