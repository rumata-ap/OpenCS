using CScore.Fem;

namespace OpenCS.Utilites
{
   /// <summary>Разворот КонЭ: КЭ сетки с новым порядком узлов (<see cref="CScore.Fem.Editing.FemOrientationFlip"/>).</summary>
   public partial class DatabaseService
   {
      /// <summary>
      /// Записывает у КЭ (по Id) порядок узлов, шарниры концов, поворот сечения стержня и угол осей выдачи усилий пластины
      /// одной транзакцией; остальные поля КЭ не трогаются.
      /// </summary>
      public void UpdateFemElementsOrientation(IReadOnlyCollection<FemElement> elements)
      {
         if (elements.Count == 0) return;
         using var tx = _connection.BeginTransaction();
         try
         {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
               UPDATE fem_elements SET node_ids_json=@nodes, release_i=@ri, release_j=@rj, beam_rotation_deg=@brd,
                  local_axis_angle_deg=@laa
               WHERE id=@id
               """;
            var pNodes = cmd.Parameters.Add("@nodes", Microsoft.Data.Sqlite.SqliteType.Text);
            var pRi = cmd.Parameters.Add("@ri", Microsoft.Data.Sqlite.SqliteType.Integer);
            var pRj = cmd.Parameters.Add("@rj", Microsoft.Data.Sqlite.SqliteType.Integer);
            var pBrd = cmd.Parameters.Add("@brd", Microsoft.Data.Sqlite.SqliteType.Real);
            var pLaa = cmd.Parameters.Add("@laa", Microsoft.Data.Sqlite.SqliteType.Real);
            var pId = cmd.Parameters.Add("@id", Microsoft.Data.Sqlite.SqliteType.Integer);
            foreach (var e in elements)
            {
               pNodes.Value = e.NodeIdsJson;
               pRi.Value = (object?)e.ReleaseI ?? DBNull.Value;
               pRj.Value = (object?)e.ReleaseJ ?? DBNull.Value;
               pBrd.Value = (object?)e.BeamRotationDeg ?? DBNull.Value;
               pLaa.Value = (object?)e.LocalAxisAngleDeg ?? DBNull.Value;
               pId.Value = e.Id;
               cmd.ExecuteNonQuery();
            }
            tx.Commit();
         }
         catch { tx.Rollback(); throw; }
      }
   }
}
