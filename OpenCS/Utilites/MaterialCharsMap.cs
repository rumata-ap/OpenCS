using CScore;
using CsvHelper.Configuration;
using System.Globalization;

namespace OpenCS.Utilites
{
   public sealed class MaterialCharsMap : ClassMap<MaterialChars>
   {
      public MaterialCharsMap()
      {
         AutoMap(CultureInfo.InvariantCulture);
         Map(m => m.Id).Ignore();
         Map(m => m.MaterialId).Ignore();
         Map(m => m.Material).Ignore();
         Map(m => m.Material.Id).Ignore();
         Map(m => m.Material.Num).Ignore();
         Map(m => m.Material.Json).Ignore();
         Map(m => m.Material.Description).Ignore();
      }
   }
}
