namespace OpenCS.Utilites;

public partial class DatabaseService
{
   /// <summary>Id дочерних схем-субмоделей, извлечённых из схемы <paramref name="parentSchemaId"/>.</summary>
   public List<int> FemSubmodelSchemaIds(int parentSchemaId)
   {
      using var cmd = _connection.CreateCommand();
      cmd.CommandText = "SELECT submodel_schema_id FROM submodel_extractions WHERE parent_schema_id=@id ORDER BY submodel_schema_id";
      cmd.Parameters.AddWithValue("@id", parentSchemaId);
      var ids = new List<int>();
      using var r = cmd.ExecuteReader();
      while (r.Read()) ids.Add(r.GetInt32(0));
      return ids;
   }

   /// <summary>Число сохранённых результатов постановок схемы (удаляются вместе со схемой).</summary>
   public int FemSchemaResultCount(int schemaId) => CollectFemSchemaResultIds(schemaId).Count;
}
