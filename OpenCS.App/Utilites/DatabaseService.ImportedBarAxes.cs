using System.Globalization;
using System.Text.Json;
using CScore.Fem;
using CScore.Import;

namespace OpenCS.Utilites
{
   /// <summary>
   /// Миграция v102: оси импортных стержней ЛИРЫ/SCAD как у родных (<see cref="ImportedBarAxes"/>, 18.11.2026). До неё
   /// местная Y стержня сетки импорта была Y1 программы, теперь — Z1 (поворот на 90° больше).
   /// </summary>
   public partial class DatabaseService
   {
      /// <summary>
      /// У стержней с поворотом (<c>beam_rotation_deg</c>) импортных схем ЛИРЫ/SCAD и их фрагментов: угол +90°, биты
      /// шарниров Qy ↔ Qz, My ↔ Mz; местные нагрузки на них по y/z — в новые оси (запись на стержни вперемешку с
      /// другими КЭ делится: у пластин и прочих КЭ оси прежние). Результаты постановок CSfea этих схем сбрасываются, как
      /// после смены сетки (<see cref="FemAnalysis.InvalidateResult"/>): усилия стержней в них записаны в прежних осях
      /// (наборы усилий из них по базе не отличить от наборов из таблиц ЛИРЫ/SCAD — в журнале общее напоминание). Снимки ЛИРЫ/SCAD (оси программы) и сечения
      /// не меняются.
      /// </summary>
      void MigrateV102()
      {
         var schemas = new List<(int Id, string Tag)>();
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = """
               SELECT id, tag FROM fem_schemas
               WHERE source_type IN ('lira', 'scad') OR parent_source_type IN ('lira', 'scad')
               """;
            using var r = cmd.ExecuteReader();
            while (r.Read()) schemas.Add((r.GetInt32(0), r.IsDBNull(1) ? "" : r.GetString(1)));
         }

         int bars = 0, loads = 0, split = 0, results = 0;
         var touched = new List<string>();
         foreach (var (schemaId, tag) in schemas)
         {
            var b = MigrateImportedBarAxes(schemaId);
            if (b.Count == 0) continue;
            bars += b.Count;
            var (l, s) = MigrateImportedBarLoads(schemaId, b);
            loads += l;
            split += s;
            results += ResetFemAnalysisResults(schemaId);
            touched.Add($"«{tag}»");
         }
         if (bars == 0) return;
         _migrationNotices.Add(string.Format(CultureInfo.CurrentCulture, Loc.S("ImportedBarAxesMigratedNotice",
               "Оси импортных стержней переведены на новое правило (местная Y = Z1 программы, как у родных балок) в схемах {0}: стержней {1}, местных нагрузок {2} (разделено записей на стержни и пластины: {3})"),
            string.Join(", ", touched), bars, loads, split));
         if (results > 0)
            _migrationNotices.Add(string.Format(CultureInfo.CurrentCulture, Loc.S("ImportedBarAxesResultsResetNotice",
               "Сброшены результаты постановок CSfea импортных схем (усилия стержней были в прежних осях): {0}. Пересчитайте их и пересоздайте наборы усилий стержней, составленные из этих результатов (наборы из таблиц ЛИРЫ/SCAD верны)"), results));
      }

      /// <summary>Угол и шарниры стержней схемы; результат — теги переведённых стержней.</summary>
      HashSet<string> MigrateImportedBarAxes(int schemaId)
      {
         var rows = new List<(long Id, string Tag, double Deg, long? Ri, long? Rj)>();
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = """
               SELECT id, elem_tag, beam_rotation_deg, release_i, release_j FROM fem_elements
               WHERE schema_id = @s AND elem_type = 'beam' AND beam_rotation_deg IS NOT NULL
               """;
            cmd.Parameters.AddWithValue("@s", schemaId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
               rows.Add((r.GetInt64(0), r.GetString(1), r.GetDouble(2),
                  r.IsDBNull(3) ? null : r.GetInt64(3), r.IsDBNull(4) ? null : r.GetInt64(4)));
         }
         using var upd = _connection.CreateCommand();
         upd.CommandText = "UPDATE fem_elements SET beam_rotation_deg = @d, release_i = @ri, release_j = @rj WHERE id = @id";
         var pd = upd.Parameters.Add("@d", Microsoft.Data.Sqlite.SqliteType.Real);
         var pri = upd.Parameters.Add("@ri", Microsoft.Data.Sqlite.SqliteType.Integer);
         var prj = upd.Parameters.Add("@rj", Microsoft.Data.Sqlite.SqliteType.Integer);
         var pid = upd.Parameters.Add("@id", Microsoft.Data.Sqlite.SqliteType.Integer);
         foreach (var (id, _, deg, ri, rj) in rows)
         {
            pd.Value = ImportedBarAxes.MigrateRotationDeg(deg);
            pri.Value = ri is { } a ? ImportedBarAxes.ReleaseToMesh((int)a) : DBNull.Value;
            prj.Value = rj is { } c ? ImportedBarAxes.ReleaseToMesh((int)c) : DBNull.Value;
            pid.Value = id;
            upd.ExecuteNonQuery();
         }
         return rows.Select(x => x.Tag).ToHashSet(StringComparer.Ordinal);
      }

      /// <summary>
      /// Местные нагрузки по y/z на переведённые стержни: (переведено записей, разделено записей). Цель «КонЭ» и
      /// «группа» разворачивается в КЭ, если запись приходится делить.
      /// </summary>
      (int Migrated, int Split) MigrateImportedBarLoads(int schemaId, HashSet<string> bars)
      {
         var byMember = new Dictionary<string, List<string>>(StringComparer.Ordinal);
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = "SELECT elem_tag, source_member_tag FROM fem_elements WHERE schema_id = @s AND source_member_tag IS NOT NULL";
            cmd.Parameters.AddWithValue("@s", schemaId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
               string m = r.GetString(1);
               if (!byMember.TryGetValue(m, out var list)) byMember[m] = list = [];
               list.Add(r.GetString(0));
            }
         }
         var groups = new Dictionary<long, (string Kind, string Tags)>();
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = "SELECT id, kind, member_tags_json FROM fem_member_groups WHERE schema_id = @s";
            cmd.Parameters.AddWithValue("@s", schemaId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) groups[r.GetInt64(0)] = (r.GetString(1), r.GetString(2));
         }

         var loads = new List<(long Id, long Case, string Origin, string TargetKind, string Tags, long? Group, string Kind, string Axis, string Values)>();
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = """
               SELECT id, load_case_id, origin, target_kind, target_tags_json, group_id, load_kind, axis, values_json
               FROM fem_element_loads WHERE schema_id = @s AND coordinate_system = 'local' AND axis IN ('y', 'z')
               """;
            cmd.Parameters.AddWithValue("@s", schemaId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
               loads.Add((r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetString(4),
                  r.IsDBNull(5) ? null : r.GetInt64(5), r.GetString(6), r.GetString(7), r.GetString(8)));
         }

         int migrated = 0, split = 0;
         foreach (var l in loads)
         {
            var values = JsonSerializer.Deserialize<double[]>(l.Values) ?? [];
            if (ImportedBarAxes.MigrateLocalBarLoad(l.Kind, l.Axis, values) is not var (axis, newValues)) continue;
            var elems = TargetElements(l.TargetKind, l.Tags, l.Group);
            var onBars = elems.Where(bars.Contains).ToList();
            if (onBars.Count == 0) continue;
            migrated++;
            if (onBars.Count == elems.Count)
            {
               using var upd = _connection.CreateCommand();
               upd.CommandText = "UPDATE fem_element_loads SET axis = @a, values_json = @v WHERE id = @id";
               upd.Parameters.AddWithValue("@a", axis);
               upd.Parameters.AddWithValue("@v", JsonSerializer.Serialize(newValues));
               upd.Parameters.AddWithValue("@id", l.Id);
               upd.ExecuteNonQuery();
               continue;
            }
            split++;
            var rest = elems.Where(t => !bars.Contains(t)).ToList();
            using (var upd = _connection.CreateCommand())
            {
               upd.CommandText = """
                  UPDATE fem_element_loads SET target_kind = 'elements', target_tags_json = @t, group_id = NULL
                  WHERE id = @id
                  """;
               upd.Parameters.AddWithValue("@t", JsonSerializer.Serialize(rest));
               upd.Parameters.AddWithValue("@id", l.Id);
               upd.ExecuteNonQuery();
            }
            using var ins = _connection.CreateCommand();
            ins.CommandText = """
               INSERT INTO fem_element_loads (schema_id, load_case_id, origin, target_kind, target_tags_json, group_id,
                                              load_kind, coordinate_system, axis, values_json)
               VALUES (@s, @c, @o, 'elements', @t, NULL, @k, 'local', @a, @v)
               """;
            ins.Parameters.AddWithValue("@s", schemaId);
            ins.Parameters.AddWithValue("@c", l.Case);
            ins.Parameters.AddWithValue("@o", l.Origin);
            ins.Parameters.AddWithValue("@t", JsonSerializer.Serialize(onBars));
            ins.Parameters.AddWithValue("@k", l.Kind);
            ins.Parameters.AddWithValue("@a", axis);
            ins.Parameters.AddWithValue("@v", JsonSerializer.Serialize(newValues));
            ins.ExecuteNonQuery();
         }
         return (migrated, split);

         List<string> TargetElements(string kind, string tagsJson, long? group)
         {
            List<string> Members(IEnumerable<string> tags) =>
               tags.SelectMany(t => byMember.TryGetValue(t, out var e) ? e : []).Distinct(StringComparer.Ordinal).ToList();
            static List<string> Tags(string json)
            {
               // Теги — строки или числа (как в FemMemberGroup.MemberTags).
               using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
               if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
               return doc.RootElement.EnumerateArray()
                  .Select(x => x.ValueKind switch
                  {
                     JsonValueKind.String => x.GetString(),
                     JsonValueKind.Number => x.GetRawText(),
                     _ => null,
                  })
                  .OfType<string>().ToList();
            }
            return kind switch
            {
               FemLoadTargetKinds.Elements => Tags(tagsJson),
               FemLoadTargetKinds.Members => Members(Tags(tagsJson)),
               FemLoadTargetKinds.Group when group is { } g && groups.TryGetValue(g, out var grp) =>
                  grp.Kind == FemMemberGroup.KindMesh ? Tags(grp.Tags) : Members(Tags(grp.Tags)),
               _ => [],
            };
         }
      }

      /// <summary>Сбрасывает результаты постановок CSfea схемы (как после смены сетки); число сброшенных.</summary>
      int ResetFemAnalysisResults(int schemaId)
      {
         using var upd = _connection.CreateCommand();
         upd.CommandText = "UPDATE fem_analyses SET result_id = NULL, status = 'created' WHERE schema_id = @s AND result_id IS NOT NULL";
         upd.Parameters.AddWithValue("@s", schemaId);
         return upd.ExecuteNonQuery();
      }
   }
}
