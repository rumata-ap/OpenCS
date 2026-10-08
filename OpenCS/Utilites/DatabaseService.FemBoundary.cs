using CScore.Fem;
using Microsoft.Data.Sqlite;

namespace OpenCS.Utilites
{
   /// <summary>Граничные условия сеточного уровня схемы: закрепления узлов сетки, пружины, жёсткие тела, ГУ КЭ.</summary>
   public partial class DatabaseService
   {
      /// <summary>Закрепления узлов сетки схемы.</summary>
      public List<FemMeshNodeSupport> GetFemMeshNodeSupports(int schemaId)
      {
         var result = new List<FemMeshNodeSupport>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT id, node_tag, mask, origin FROM fem_mesh_node_supports WHERE schema_id=@sid ORDER BY id";
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var r = cmd.ExecuteReader();
         while (r.Read())
            result.Add(new FemMeshNodeSupport
            {
               Id = r.GetInt32(0), SchemaId = schemaId, NodeTag = r.GetString(1), Mask = r.GetInt32(2), Origin = r.GetString(3),
            });
         return result;
      }

      /// <summary>У КЭ схемы есть освобождения концов (шарниры) — без чтения всей сетки.</summary>
      public bool HasFemElementReleases(int schemaId)
      {
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT EXISTS(SELECT 1 FROM fem_elements WHERE schema_id=@sid
               AND (COALESCE(release_i, 0) <> 0 OR COALESCE(release_j, 0) <> 0))
            """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         return Convert.ToInt64(cmd.ExecuteScalar()) != 0;
      }

      /// <summary>
      /// Ручная правка ГУ узла сетки одной транзакцией: закрепления и пружины узла любого происхождения заменяются
      /// записями <see cref="FemLoadOrigin.Manual"/> (закрепление — всегда, хотя бы с нулевой маской; пружина — при
      /// ненулевых жёсткостях). Узел становится ручным — повторный перенос из источника его уже не перезапишет.
      /// </summary>
      /// <param name="stiffnesses">Жёсткости X Y Z UX UY UZ, Н/м и Н·м/рад.</param>
      public void SetFemMeshNodeBoundary(int schemaId, string nodeTag, int mask, IReadOnlyList<double> stiffnesses)
      {
         using var tx = _connection.BeginTransaction();
         try
         {
            using (var cmd = _connection.CreateCommand())
            {
               cmd.CommandText = """
                  DELETE FROM fem_mesh_node_supports WHERE schema_id=@sid AND node_tag=@tag;
                  DELETE FROM fem_springs WHERE schema_id=@sid AND node_tag=@tag AND target_kind=@kind;
                  """;
               cmd.Parameters.AddWithValue("@sid", schemaId);
               cmd.Parameters.AddWithValue("@tag", nodeTag);
               cmd.Parameters.AddWithValue("@kind", FemSpringTargetKinds.MeshNode);
               cmd.ExecuteNonQuery();
            }
            // Запись закрепления пишется и с нулевой маской: она помечает узел ручным, чтобы снятые пользователем
            // импортные ГУ не вернулись при повторном переносе (резолвер и глифы нулевую маску пропускают).
            FemMeshNodeSupport[] supports =
               [new FemMeshNodeSupport { NodeTag = nodeTag, Mask = mask & FemBoundaryDofs.All, Origin = FemLoadOrigin.Manual }];
            var spring = new FemSpring { TargetKind = FemSpringTargetKinds.MeshNode, NodeTag = nodeTag, Origin = FemLoadOrigin.Manual };
            spring.SetStiffnesses(stiffnesses);
            InsertFemBoundaryCore(schemaId, supports, spring.ActiveMask != 0 ? [spring] : [], []);
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }

      /// <summary>Пружины схемы (на узлах обоих уровней).</summary>
      public List<FemSpring> GetFemSprings(int schemaId)
      {
         var result = new List<FemSpring>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT id, target_kind, node_tag, kx, ky, kz, kux, kuy, kuz, origin, source_elem_tag
            FROM fem_springs WHERE schema_id=@sid ORDER BY id
            """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var r = cmd.ExecuteReader();
         while (r.Read())
            result.Add(new FemSpring
            {
               Id = r.GetInt32(0), SchemaId = schemaId, TargetKind = r.GetString(1), NodeTag = r.GetString(2),
               Kx = r.GetDouble(3), Ky = r.GetDouble(4), Kz = r.GetDouble(5),
               Kux = r.GetDouble(6), Kuy = r.GetDouble(7), Kuz = r.GetDouble(8),
               Origin = r.GetString(9), SourceElemTag = r.IsDBNull(10) ? null : r.GetString(10),
            });
         return result;
      }

      /// <summary>Жёсткие тела схемы.</summary>
      public List<FemRigidBody> GetFemRigidBodies(int schemaId)
      {
         var result = new List<FemRigidBody>();
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT id, master_node_tag, slave_node_tags_json, mask, origin, source_elem_tag
            FROM fem_rigid_bodies WHERE schema_id=@sid ORDER BY id
            """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         using var r = cmd.ExecuteReader();
         while (r.Read())
            result.Add(new FemRigidBody
            {
               Id = r.GetInt32(0), SchemaId = schemaId, MasterNodeTag = r.GetString(1), SlaveNodeTagsJson = r.GetString(2),
               Mask = r.GetInt32(3), Origin = r.GetString(4), SourceElemTag = r.IsDBNull(5) ? null : r.GetString(5),
            });
         return result;
      }

      /// <summary>
      /// Записывает ГУ происхождения <paramref name="origin"/> одной транзакцией: закрепления, пружины и жёсткие тела
      /// этого происхождения заменяются переданными (им проставляются схема, происхождение и новые Id), записи другого
      /// происхождения (ручные) не трогаются. Узлы сетки с ручными ГУ (<see cref="SetFemMeshNodeBoundary"/>) остаются за
      /// пользователем: закрепления и пружины узлов сетки другого происхождения на них не пишутся.
      /// <paramref name="elementProps"/> (по тегу КЭ), если передан, заменяет освобождения и C1 у всех импортированных КЭ
      /// схемы: КЭ вне словаря их теряют.
      /// </summary>
      /// <returns>Число КЭ, получивших ГУ из <paramref name="elementProps"/>.</returns>
      public int SaveFemBoundary(int schemaId, string origin, IReadOnlyList<FemMeshNodeSupport> supports,
         IReadOnlyList<FemSpring> springs, IReadOnlyList<FemRigidBody> rigidBodies,
         IReadOnlyDictionary<string, FemElementBoundaryProps>? elementProps = null)
      {
         int updated = 0;
         using var tx = _connection.BeginTransaction();
         try
         {
            using (var cmd = _connection.CreateCommand())
            {
               cmd.CommandText = """
                  DELETE FROM fem_mesh_node_supports WHERE schema_id=@sid AND origin=@origin;
                  DELETE FROM fem_springs            WHERE schema_id=@sid AND origin=@origin;
                  DELETE FROM fem_rigid_bodies       WHERE schema_id=@sid AND origin=@origin;
                  """;
               cmd.Parameters.AddWithValue("@sid", schemaId);
               cmd.Parameters.AddWithValue("@origin", origin);
               cmd.ExecuteNonQuery();
            }
            if (origin != FemLoadOrigin.Manual && ManualMeshNodeTags(schemaId) is { Count: > 0 } manual)
            {
               supports = supports.Where(s => !manual.Contains(s.NodeTag)).ToList();
               springs = springs.Where(s => s.TargetKind != FemSpringTargetKinds.MeshNode || !manual.Contains(s.NodeTag)).ToList();
            }
            foreach (var s in supports) s.Origin = origin;
            foreach (var s in springs) s.Origin = origin;
            foreach (var b in rigidBodies) b.Origin = origin;
            InsertFemBoundaryCore(schemaId, supports, springs, rigidBodies);
            if (elementProps != null) updated = ReplaceFemElementBoundaryPropsCore(schemaId, elementProps);
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
         return updated;
      }

      /// <summary>Узлы сетки с ручными закреплениями или пружинами.</summary>
      public HashSet<string> ManualMeshNodeTags(int schemaId)
      {
         var result = new HashSet<string>(StringComparer.Ordinal);
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            SELECT node_tag FROM fem_mesh_node_supports WHERE schema_id=@sid AND origin=@manual
            UNION SELECT node_tag FROM fem_springs WHERE schema_id=@sid AND origin=@manual AND target_kind=@kind
            """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         cmd.Parameters.AddWithValue("@manual", FemLoadOrigin.Manual);
         cmd.Parameters.AddWithValue("@kind", FemSpringTargetKinds.MeshNode);
         using var r = cmd.ExecuteReader();
         while (r.Read()) result.Add(r.GetString(0));
         return result;
      }

      /// <summary>Вставляет ГУ в схему <paramref name="schemaId"/> (внутри транзакции вызывающего).</summary>
      void InsertFemBoundaryCore(int schemaId, IEnumerable<FemMeshNodeSupport> supports, IEnumerable<FemSpring> springs,
         IEnumerable<FemRigidBody> rigidBodies, bool updateObjects = true)
      {
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = """
               INSERT INTO fem_mesh_node_supports (schema_id, node_tag, mask, origin) VALUES (@sid, @tag, @mask, @origin);
               SELECT last_insert_rowid();
               """;
            cmd.Parameters.AddWithValue("@sid", schemaId);
            var tag = cmd.Parameters.Add("@tag", SqliteType.Text);
            var mask = cmd.Parameters.Add("@mask", SqliteType.Integer);
            var org = cmd.Parameters.Add("@origin", SqliteType.Text);
            foreach (var s in supports)
            {
               tag.Value = s.NodeTag; mask.Value = s.Mask; org.Value = s.Origin;
               int id = (int)(long)cmd.ExecuteScalar()!;
               if (updateObjects) { s.Id = id; s.SchemaId = schemaId; }
            }
         }
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = """
               INSERT INTO fem_springs (schema_id, target_kind, node_tag, kx, ky, kz, kux, kuy, kuz, origin, source_elem_tag)
               VALUES (@sid, @tk, @tag, @kx, @ky, @kz, @kux, @kuy, @kuz, @origin, @src);
               SELECT last_insert_rowid();
               """;
            foreach (var s in springs)
            {
               cmd.Parameters.Clear();
               cmd.Parameters.AddWithValue("@sid", schemaId);
               cmd.Parameters.AddWithValue("@tk", s.TargetKind);
               cmd.Parameters.AddWithValue("@tag", s.NodeTag);
               cmd.Parameters.AddWithValue("@kx", s.Kx);
               cmd.Parameters.AddWithValue("@ky", s.Ky);
               cmd.Parameters.AddWithValue("@kz", s.Kz);
               cmd.Parameters.AddWithValue("@kux", s.Kux);
               cmd.Parameters.AddWithValue("@kuy", s.Kuy);
               cmd.Parameters.AddWithValue("@kuz", s.Kuz);
               cmd.Parameters.AddWithValue("@origin", s.Origin);
               cmd.Parameters.AddWithValue("@src", (object?)s.SourceElemTag ?? DBNull.Value);
               int id = (int)(long)cmd.ExecuteScalar()!;
               if (updateObjects) { s.Id = id; s.SchemaId = schemaId; }
            }
         }
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = """
               INSERT INTO fem_rigid_bodies (schema_id, master_node_tag, slave_node_tags_json, mask, origin, source_elem_tag)
               VALUES (@sid, @master, @slaves, @mask, @origin, @src);
               SELECT last_insert_rowid();
               """;
            foreach (var b in rigidBodies)
            {
               cmd.Parameters.Clear();
               cmd.Parameters.AddWithValue("@sid", schemaId);
               cmd.Parameters.AddWithValue("@master", b.MasterNodeTag);
               cmd.Parameters.AddWithValue("@slaves", b.SlaveNodeTagsJson);
               cmd.Parameters.AddWithValue("@mask", b.Mask);
               cmd.Parameters.AddWithValue("@origin", b.Origin);
               cmd.Parameters.AddWithValue("@src", (object?)b.SourceElemTag ?? DBNull.Value);
               int id = (int)(long)cmd.ExecuteScalar()!;
               if (updateObjects) { b.Id = id; b.SchemaId = schemaId; }
            }
         }
      }

      /// <summary>
      /// Заменяет повороты сечения импортированных стержней схемы (оси программы-источника, перечитанные из .SPR): КЭ из
      /// словаря получают свой угол, остальные импортные — «оси не прочитаны».
      /// </summary>
      /// <returns>Число КЭ, получивших угол.</returns>
      public int ReplaceFemElementBeamRotations(int schemaId, IReadOnlyDictionary<string, double> rotationByElemTag)
      {
         using var tx = _connection.BeginTransaction();
         using (var clear = _connection.CreateCommand())
         {
            clear.CommandText = "UPDATE fem_elements SET beam_rotation_deg=NULL WHERE schema_id=@sid AND origin=@origin";
            clear.Parameters.AddWithValue("@sid", schemaId);
            clear.Parameters.AddWithValue("@origin", FemMember.MeshSourceImported);
            clear.ExecuteNonQuery();
         }
         int updated = 0;
         using (var cmd = _connection.CreateCommand())
         {
            cmd.CommandText = """
               UPDATE fem_elements SET beam_rotation_deg=@deg
               WHERE schema_id=@sid AND origin=@origin AND elem_type='beam' AND elem_tag=@tag
               """;
            cmd.Parameters.AddWithValue("@sid", schemaId);
            cmd.Parameters.AddWithValue("@origin", FemMember.MeshSourceImported);
            var deg = cmd.Parameters.Add("@deg", SqliteType.Real);
            var tag = cmd.Parameters.Add("@tag", SqliteType.Text);
            foreach (var (elemTag, value) in rotationByElemTag)
            {
               deg.Value = value;
               tag.Value = elemTag;
               updated += cmd.ExecuteNonQuery();
            }
         }
         tx.Commit();
         return updated;
      }

      /// <summary>Заменяет освобождения и C1 импортированных КЭ схемы (внутри транзакции вызывающего).</summary>
      int ReplaceFemElementBoundaryPropsCore(int schemaId, IReadOnlyDictionary<string, FemElementBoundaryProps> props)
      {
         using (var clear = _connection.CreateCommand())
         {
            clear.CommandText = """
               UPDATE fem_elements SET release_i=NULL, release_j=NULL, foundation_c1=NULL
               WHERE schema_id=@sid AND origin=@origin
               """;
            clear.Parameters.AddWithValue("@sid", schemaId);
            clear.Parameters.AddWithValue("@origin", FemMember.MeshSourceImported);
            clear.ExecuteNonQuery();
         }
         int updated = 0;
         using var cmd = _connection.CreateCommand();
         cmd.CommandText = """
            UPDATE fem_elements SET release_i=@ri, release_j=@rj, foundation_c1=@c1
            WHERE schema_id=@sid AND origin=@origin AND elem_tag=@tag
            """;
         cmd.Parameters.AddWithValue("@sid", schemaId);
         cmd.Parameters.AddWithValue("@origin", FemMember.MeshSourceImported);
         var ri = cmd.Parameters.Add("@ri", SqliteType.Integer);
         var rj = cmd.Parameters.Add("@rj", SqliteType.Integer);
         var c1 = cmd.Parameters.Add("@c1", SqliteType.Real);
         var tag = cmd.Parameters.Add("@tag", SqliteType.Text);
         foreach (var (elemTag, p) in props)
         {
            ri.Value = (object?)p.ReleaseI ?? DBNull.Value;
            rj.Value = (object?)p.ReleaseJ ?? DBNull.Value;
            c1.Value = (object?)p.FoundationC1 ?? DBNull.Value;
            tag.Value = elemTag;
            updated += cmd.ExecuteNonQuery();
         }
         return updated;
      }

      /// <summary>Параметры @ri, @rj, @c1 — ГУ КЭ (v78), @brd — поворот сечения стержня (v79).</summary>
      static void AddFemElementBoundaryParameters(SqliteCommand cmd, FemElement element)
      {
         cmd.Parameters.AddWithValue("@brd", (object?)element.BeamRotationDeg ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@ri", (object?)element.ReleaseI ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@rj", (object?)element.ReleaseJ ?? DBNull.Value);
         cmd.Parameters.AddWithValue("@c1", (object?)element.FoundationC1 ?? DBNull.Value);
      }
   }
}
