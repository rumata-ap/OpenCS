using System;
using System.Linq;
using Microsoft.Data.Sqlite;

var conn = new SqliteConnection($"Data Source={args[0]}");
conn.Open();

using var cmd = conn.CreateCommand();
cmd.CommandText = @"
   SELECT id, task_kind, status, data_json
   FROM calc_results
   WHERE task_kind IN ('two_stage_strain','two_stage_strain_batch')
   ORDER BY id DESC LIMIT 3";
using var r = cmd.ExecuteReader();
while (r.Read())
{
   Console.WriteLine($"=== id={r.GetInt32(0)} kind={r.GetString(1)} status={r.GetString(2)} ===");
   var json = r.GetString(3);
   Console.WriteLine(json.Length > 1200 ? json.Substring(0, 1200) + "...[trunc]" : json);
   Console.WriteLine();
}
conn.Close();
