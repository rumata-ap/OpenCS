using CScore.Fem;

namespace OpenCS.Utilites
{
   /// <summary>
   /// Жёсткие тела на узлах схемы (конструктивный уровень, <see cref="FemNodeRigidBodies"/>): фрагмент этажа переносит
   /// импортные АЖТ родителя «по точкам»; тела на узлах сетки из них строит сборка сетки схемы.
   /// </summary>
   public partial class DatabaseService
   {
      const string FemNodeRigidBodiesDdl = """
         CREATE TABLE IF NOT EXISTS fem_node_rigid_bodies (
             id INTEGER PRIMARY KEY AUTOINCREMENT,
             schema_id INTEGER NOT NULL REFERENCES fem_schemas(id) ON DELETE CASCADE,
             master_node_tag TEXT NOT NULL,
             slave_node_tags_json TEXT NOT NULL DEFAULT '[]',
             mask INTEGER NOT NULL DEFAULT 63,
             origin TEXT NOT NULL DEFAULT 'manual',
             source_elem_tag TEXT,
             kind TEXT NOT NULL DEFAULT 'rigid'
         );
         CREATE INDEX IF NOT EXISTS idx_fem_node_rigid_bodies_schema ON fem_node_rigid_bodies(schema_id);
         """;

      /// <summary>Миграция v96: жёсткие тела на узлах схемы (АЖТ фрагмента «по точкам»).</summary>
      void MigrateV96()
      {
         MigrateV95();
         MigExec(FemNodeRigidBodiesDdl);
      }

      /// <summary>v97: накопленные прежние снимки сеток областей удаляются (дальше чистит каждая запись снимка).</summary>
      void MigrateV97() => PrunePlanarMeshSnapshots(null);

      /// <summary>Жёсткие тела на узлах схемы: ведущий и ведомые — теги <see cref="FemNode"/>.</summary>
      public List<FemRigidBody> GetFemNodeRigidBodies(int schemaId) => QueryRigidBodies("fem_node_rigid_bodies", schemaId);

      /// <summary>Заменяет жёсткие тела на узлах схемы одной транзакцией (Id и схема проставляются).</summary>
      public void ReplaceFemNodeRigidBodies(int schemaId, IReadOnlyList<FemRigidBody> bodies)
      {
         using var tx = _connection.BeginTransaction();
         try
         {
            using (var cmd = _connection.CreateCommand())
            {
               cmd.CommandText = "DELETE FROM fem_node_rigid_bodies WHERE schema_id=@sid";
               cmd.Parameters.AddWithValue("@sid", schemaId);
               cmd.ExecuteNonQuery();
            }
            InsertRigidBodiesCore("fem_node_rigid_bodies", schemaId, bodies, updateObjects: true);
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }
   }
}
