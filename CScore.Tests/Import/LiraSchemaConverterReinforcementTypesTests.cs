using System.Text.Json;
using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>ТЗА КЭ из таблицы «Элементы - ТЗА» при конвертации схемы ЛИРЫ.</summary>
public class LiraSchemaConverterReinforcementTypesTests
{
   static LiraSchemaData Schema()
   {
      var d = new LiraSchemaData();
      d.PlateStiffnesses.Add(new LiraPlateStiffnessRecord(1, "Плита 200", 3e6, 0.2, 200));
      d.PlateStiffnesses.Add(new LiraPlateStiffnessRecord(2, "Стена 250", 3e6, 0.2, 250));
      d.Elements.Add(new LiraElementRecord(1, 44, 0, 1, [1, 2, 3, 4]));
      d.Elements.Add(new LiraElementRecord(2, 44, 0, 1, [2, 5, 6, 3]));
      d.Elements.Add(new LiraElementRecord(3, 44, 0, 1, [5, 7, 8, 6]));
      d.Elements.Add(new LiraElementRecord(4, 44, 0, 2, [9, 10, 11, 12]));
      d.Elements.Add(new LiraElementRecord(5, 44, 0, 1, [7, 13, 14, 8]));
      d.Elements.Add(new LiraElementRecord(6, 10, 0, 3, [1, 9]));
      d.ElementReinforcementTypes[1] = [1, 2, 4];
      d.ElementReinforcementTypes[2] = [4, 2, 1];   // тот же набор в другом порядке
      d.ElementReinforcementTypes[3] = [1];
      d.ElementReinforcementTypes[4] = [1];         // тот же набор, другая жёсткость
      d.ElementReinforcementTypes[6] = [16, 20];    // стержень — в группы пластин не попадает
      return d;
   }

   [Fact]
   public void ShellElements_CarryNormalizedTypeIds()
   {
      var els = LiraSchemaConverter.ToFemMeshShellElements(Schema(), 1).ToDictionary(e => e.ElemTag);

      Assert.Equal("1 2 4", els["1"].ReinforcementTypeIds);
      Assert.Equal("1 2 4", els["2"].ReinforcementTypeIds);
      Assert.Equal("1", els["3"].ReinforcementTypeIds);
      Assert.Null(els["5"].ReinforcementTypeIds);
   }

   [Fact]
   public void BarElements_CarryTypeIds()
   {
      var bar = Assert.Single(LiraSchemaConverter.ToFemMeshBarElements(Schema(), 1));

      Assert.Equal("6", bar.ElemTag);
      Assert.Equal("16 20", bar.ReinforcementTypeIds);
   }

   [Fact]
   public void Groups_ByTypeSetAndPlateStiffness()
   {
      var groups = LiraSchemaConverter.ToFemMemberGroupsByReinforcementTypes(Schema(), 1);

      Assert.Equal(["ТЗА 1 · Плита 200", "ТЗА 1 2 4 · Плита 200", "ТЗА 1 · Стена 250"],
         groups.Select(g => g.Tag));
      Assert.All(groups, g => Assert.Equal("shell", g.MemberType));
      Assert.Equal([3], JsonSerializer.Deserialize<int[]>(groups[0].MemberTagsJson));
      Assert.Equal([1, 2], JsonSerializer.Deserialize<int[]>(groups[1].MemberTagsJson));
      Assert.Equal([4], JsonSerializer.Deserialize<int[]>(groups[2].MemberTagsJson));
   }

   [Fact]
   public void NoTable33_NoGroups()
   {
      var d = Schema();
      d.ElementReinforcementTypes.Clear();

      Assert.Empty(LiraSchemaConverter.ToFemMemberGroupsByReinforcementTypes(d, 1));
   }
}
