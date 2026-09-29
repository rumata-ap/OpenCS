using CScore.Import;
using CScore.PlateRebar;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>Читатель ТЗА ЛИРЫ (.RBT) и сборка армирования КЭ — на выгрузках ЛИРА-САПФИР 2025.</summary>
public class LiraRbtReaderTests
{
   static LiraRbtFile Load(string name) =>
      LiraRbtReader.Read(Path.Combine(AppContext.BaseDirectory, "Import", "Fixtures", name));

   static double Bar(double dMm) => Math.PI * dMm * dMm / 400.0;

   [Fact]
   public void PlateVariants_CentroidTotalAreaFormulaSymmetryComposite()
   {
      var f = Load("tza-plate-variants.RBT");

      Assert.Empty(f.Warnings);
      Assert.Empty(f.Skipped);
      Assert.Equal([1, 2, 3, 4, 5], f.PlateTypes.Keys.OrderBy(k => k));

      // 1: ЦТ, XT d12s200, a = 5,5
      var t1 = f.PlateTypes[1];
      Assert.Equal(LiraRebarBinding.Centroid, t1.Binding);
      var l1 = Assert.Single(t1.Layers);
      Assert.Equal(LiraPlateRebarSlot.XT, l1.Slot);
      Assert.Equal("d12s200", l1.Formula);
      Assert.Equal(Bar(12) * 5, l1.AreaPerMeterCm2, 3);
      Assert.Equal(5.5, l1.CentroidDistanceCm(t1.Binding), 6);

      // 2: ΣAs = 7,85 см²/м, a = 4,5 (массивы d/s — устаревшие, игнорируются)
      var l2 = Assert.Single(f.PlateTypes[2].Layers);
      Assert.True(l2.IsTotalArea);
      Assert.Empty(l2.Terms);
      Assert.Equal(7.85, l2.AreaPerMeterCm2, 4);
      Assert.Equal(4.5, l2.CentroidDistanceCm(f.PlateTypes[2].Binding), 6);

      // 3: ЗС, d12s200 + d16s200, c = 3 → ц. т. каждого слагаемого c + d/2
      var t3 = f.PlateTypes[3];
      Assert.Equal(LiraRebarBinding.Cover, t3.Binding);
      var l3 = Assert.Single(t3.Layers);
      Assert.Equal(2, l3.Terms.Count);
      double a12 = Bar(12) * 5, a16 = Bar(16) * 5;
      Assert.Equal(a12 + a16, l3.AreaPerMeterCm2, 3);
      Assert.Equal((a12 * 3.6 + a16 * 3.8) / (a12 + a16), l3.CentroidDistanceCm(t3.Binding), 3);

      // 4: AS_S_All — хранится только XT, размножается на все четыре слоя
      var t4 = f.PlateTypes[4];
      Assert.Equal(1, t4.Symmetry);
      Assert.Equal(4, t4.Layers.Count);
      Assert.All(t4.Layers, l =>
      {
         Assert.Equal(Bar(14) * 1000 / 150, l.AreaPerMeterCm2, 3);
         Assert.Equal(3.2, l.CentroidDistanceCm(t4.Binding), 4);
      });

      // 5: AS_mult из ТЗА 1 и 3
      var t5 = f.PlateTypes[5];
      Assert.True(t5.IsComposite);
      Assert.Equal([1, 3], t5.ComponentIds);
      Assert.Empty(t5.Layers);
   }

   [Fact]
   public void CompositeType_IsExpandedIntoComponents()
   {
      var f = Load("tza-plate-variants.RBT");

      var r = LiraPlateReinforcementAssembler.Assemble([5], f.PlateTypes);

      Assert.Empty(r.MissingTypeIds);
      var xt = Assert.Single(r.Slots).Value;
      double a1 = Bar(12) * 5, a3 = Bar(12) * 5 + Bar(16) * 5;
      Assert.Equal(a1 + a3, xt.AreaPerMeterCm2, 3);
      double c3 = (Bar(12) * 5 * 3.6 + Bar(16) * 5 * 3.8) / a3;
      Assert.Equal((a1 * 5.5 + a3 * c3) / (a1 + a3), xt.CentroidDistanceCm, 3);
   }

   [Fact]
   public void BindingFromMaterials_IsRecognized()
   {
      var f = Load("tza-plate-binding-m.RBT");

      var t = Assert.Single(f.PlateTypes.Values);
      Assert.Equal("Стенка", t.Comment);
      Assert.Equal(LiraRebarBinding.FromMaterials, t.Binding);
      Assert.Equal(4, t.Layers.Count);
      Assert.All(t.Layers, l => Assert.Equal(4.0, l.CentroidDistanceCm(t.Binding), 6));
   }

   [Fact]
   public void MixedFile_BarTypesAreSkippedByHeader()
   {
      var f = Load("tza-plate-and-bar-mix.RBT");

      Assert.Empty(f.Warnings);
      Assert.Equal(17, f.PlateTypes.Count);
      Assert.Equal([6, 9, 9, 9], f.Skipped.Select(s => s.Kind).OrderBy(k => k));
      Assert.Contains(f.Skipped, s => s.Id == 17 && s.Name == "AS_spec" && s.Comment == "24шт_ф28");

      // ТЗА 1: ЗС, XT/XB c = 3, YT/YB c = 4, все d12s200
      var t1 = f.PlateTypes[1];
      Assert.Equal(4, t1.Layers.Count);
      var yt = t1.Layers.Single(l => l.Slot == LiraPlateRebarSlot.YT);
      Assert.Equal(4.6, yt.CentroidDistanceCm(t1.Binding), 4);
   }

   [Fact]
   public void RingAndFlangeTypes_AreSkippedWithoutWarnings()
   {
      var f = Load("tza-ring-flange.RBT");

      Assert.Empty(f.PlateTypes);
      Assert.Empty(f.Warnings);
      Assert.Equal([10, 44], f.Skipped.Select(s => s.Kind).OrderBy(k => k));
   }

   [Fact]
   public void Scheme1Lin_BackgroundPlusStrengthening_SumsLayersPerSlot()
   {
      var f = Load("tza-scheme-1lin.RBT");
      Assert.Empty(f.Warnings);
      Assert.Equal(15, f.PlateTypes.Count);
      Assert.Equal(6, f.Skipped.Count);
      Assert.Equal("Фоновое", f.PlateTypes[1].Comment);

      // Ячейка таблицы «Элементы - ТЗА»: фон 1 + BX-d10s300 (2) + BY-d10s300 (4)
      var ids = LiraPlateReinforcementAssembler.ParseTypeIds("1 2 4");
      var r = LiraPlateReinforcementAssembler.Assemble(ids, f.PlateTypes);

      double d10s300 = Bar(10) * 1000 / 300;
      Assert.Empty(r.MissingTypeIds);
      Assert.Equal(d10s300, r.Slots[LiraPlateRebarSlot.XT].AreaPerMeterCm2, 4);
      Assert.Equal(d10s300, r.Slots[LiraPlateRebarSlot.YT].AreaPerMeterCm2, 4);
      Assert.Equal(2 * d10s300, r.Slots[LiraPlateRebarSlot.XB].AreaPerMeterCm2, 4);
      Assert.Equal(2 * d10s300, r.Slots[LiraPlateRebarSlot.YB].AreaPerMeterCm2, 4);
      Assert.All(r.Slots.Values, s => Assert.Equal(4.0, s.CentroidDistanceCm, 6));

      var layers = r.ToPlateRebarLayers(0.2);
      Assert.Equal(2, layers.Count);
      var top = layers.Single(l => l.Face == RebarFace.PlusN);
      var bottom = layers.Single(l => l.Face == RebarFace.MinusN);
      Assert.Equal(d10s300 * 1e-4, top.Asx, 9);
      Assert.Equal(2 * d10s300 * 1e-4, bottom.Asy, 9);
      Assert.Equal(0.06, top.Zsx, 9);
      Assert.Equal(-0.06, bottom.Zsy, 9);
   }

   [Fact]
   public void Scheme1Lin_BeamTypes_AreReportedAsMissing()
   {
      var f = Load("tza-scheme-1lin.RBT");

      var r = LiraPlateReinforcementAssembler.Assemble(
         LiraPlateReinforcementAssembler.ParseTypeIds("16 20"), f.PlateTypes);

      Assert.True(r.IsEmpty);
      Assert.Equal([16, 20], r.MissingTypeIds);
      Assert.Empty(r.ToPlateRebarLayers(0.2));
   }

   [Fact]
   public void ParseTypeIds_EmptyAndInvalidCells()
   {
      Assert.Empty(LiraPlateReinforcementAssembler.ParseTypeIds(""));
      Assert.Empty(LiraPlateReinforcementAssembler.ParseTypeIds(null));
      Assert.Equal([17, 21], LiraPlateReinforcementAssembler.ParseTypeIds(" 17  21 "));
      Assert.Throws<FormatException>(() => LiraPlateReinforcementAssembler.ParseTypeIds("1 x"));
   }

   [Fact]
   public void GarbageInput_ProducesWarningNotException()
   {
      var f = LiraRbtReader.Read(new byte[64]);

      Assert.Empty(f.PlateTypes);
      Assert.Single(f.Warnings);
   }
}
