using CScore.Fem;
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
   public void MixedFile_BarTypesSkippedByHeader_AsSpecParsed()
   {
      var f = Load("tza-plate-and-bar-mix.RBT");

      Assert.Empty(f.Warnings);
      Assert.Equal(17, f.PlateTypes.Count);
      Assert.Equal([9, 9, 9], f.Skipped.Select(s => s.Kind).OrderBy(k => k));
      // AS_spec «24шт_ф28» — 24 стержня Ø28 с координатами внутри сечения-образца.
      var spec = f.BarTypes[17];
      Assert.Equal("24шт_ф28", spec.Comment);
      Assert.Equal(24, spec.Bars.Count);
      Assert.All(spec.Bars, b => Assert.Equal(28, b.DiameterMm));
      Assert.All(spec.Bars, b => Assert.True(Math.Abs(b.XCm) + 1.4 <= spec.TemplateWidthCm / 2
                                             && Math.Abs(b.YCm) + 1.4 <= spec.TemplateHeightCm / 2));

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
      Assert.Equal(6, f.BarTypes.Count);
      Assert.Empty(f.Skipped);
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
   public void Scheme1Lin_BeamTypes_RowsAtBottomAndTopFace()
   {
      var f = Load("tza-scheme-1lin.RBT");

      // Площадь стержня в файле — по сортаменту (Ø16 — 2,011, Ø20 — 3,142 см²).
      // Поля записи сверяются с автоименем ТЗА: «AUAS.B 3d16 c4.0/4.0», «AUAS.T 3d20 a6.0/6.0» …
      void AssertBar(int id, LiraBarRebarFace face, int count, double d, double a, LiraRebarBinding binding, string name)
      {
         var t = f.BarTypes[id];
         Assert.Equal(name, t.Name);
         Assert.Equal((face, count, d, a, a, binding), (t.Face, t.Count, t.DiameterMm, t.A, t.ASide, t.Binding));
         Assert.Equal(count * Bar(d), t.AreaCm2, 1);
      }
      AssertBar(16, LiraBarRebarFace.Bottom, 3, 16, 4, LiraRebarBinding.Cover, "AUAS.B 3d16 c4.0/4.0");
      AssertBar(17, LiraBarRebarFace.Bottom, 2, 20, 4, LiraRebarBinding.Centroid, "AUAS.B 2d20 a4.0/4.0");
      AssertBar(18, LiraBarRebarFace.Bottom, 2, 16, 4, LiraRebarBinding.Centroid, "AUAS.B 2d16 a4.0/4.0");
      AssertBar(19, LiraBarRebarFace.Top, 3, 20, 6, LiraRebarBinding.Centroid, "AUAS.T 3d20 a6.0/6.0");
      AssertBar(20, LiraBarRebarFace.Top, 2, 20, 6, LiraRebarBinding.Centroid, "AUAS.T 2d20 a6.0/6.0");
      AssertBar(21, LiraBarRebarFace.Top, 2, 16, 6, LiraRebarBinding.Centroid, "AUAS.T 2d16 a6.0/6.0");
   }

   [Fact]
   public void BarRebarSource_SumsRowsOfElementTypes()
   {
      var f = Load("tza-scheme-1lin.RBT");
      var source = new LiraRbtBarRebarSource(f, new Dictionary<string, string?>
      {
         ["24"] = "16 20",     // низ 3d16, верх 2d20
         ["25"] = "17 18",     // два ряда у нижней грани
         ["26"] = null,        // ТЗА не назначены
         ["27"] = "1 16",      // пластинчатый ТЗА у стержня — армирование не собрать
      });

      Assert.True(source.HasBars);
      Assert.Equal(3 * Bar(16), source.Get("24", BarRebarComponent.Bottom).Value!.Value, 1);
      Assert.Equal(2 * Bar(20), source.Get("24", BarRebarComponent.Top).Value!.Value, 1);
      Assert.Equal(3 * Bar(16) + 2 * Bar(20), source.Get("24", BarRebarComponent.LongitudinalSum).Value!.Value, 1);
      Assert.Equal(2 * Bar(20) + 2 * Bar(16), source.Get("25", BarRebarComponent.Bottom).Value!.Value, 1);
      Assert.Equal(0, source.Get("25", BarRebarComponent.Top).Value);
      Assert.True(source.Get("26", BarRebarComponent.Bottom).IsMissing);
      Assert.True(source.Get("27", BarRebarComponent.Bottom).IsMissing);
      Assert.True(source.Get("99", BarRebarComponent.Bottom).IsMissing);

      // Заданное армирование постоянно по длине КЭ; величин подбора (AU, AS, ASW) в нём нет.
      Assert.Single(source.GetSections("24", BarRebarComponent.Top));
      Assert.Empty(source.GetSections("26", BarRebarComponent.Top));
      Assert.False(source.Supports(BarRebarComponent.As1));
      Assert.True(source.Get("24", BarRebarComponent.As1).IsMissing);
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

   /// <summary>
   /// AS_spec пилонов (6-k1.RBT, записи 38 и 48): стержни с координатами, сверено с окном ЛИРЫ —
   /// 38: 30×180, 2 Ø20 «d20» + 16 Ø20 «4d20u200»; 48: 35×180, 38 Ø28 (угловые «8d28u82» и «у грани» d28).
   /// </summary>
   [Fact]
   public void BarSpec_BarsWithCoordinates_LayoutAndFaces()
   {
      var f = Load("tza-bar-spec.RBT");

      Assert.Empty(f.Skipped);
      Assert.Empty(f.Warnings);
      var t38 = f.BarTypes[38];
      Assert.True(t38.IsSpec);
      Assert.Equal(LiraRbtReader.KindBarSpec, t38.Kind);
      Assert.Equal("30x180_18d20_1", t38.Comment);
      Assert.Equal((30.0, 180.0), (t38.TemplateWidthCm, t38.TemplateHeightCm));
      Assert.Equal(18, t38.Bars.Count);
      Assert.All(t38.Bars, b => Assert.Equal(20, b.DiameterMm));
      Assert.Equal(18 * 3.142, t38.AreaCm2, 3);        // площадь стержня ЛИРА хранит округлённой
      Assert.Contains(t38.Bars, b => b.XCm == -10 && b.YCm == 0);
      Assert.Contains(t38.Bars, b => b.XCm == 10 && b.YCm == 85);
      Assert.Equal(17, t38.Bars.Max(b => b.YCm) - t38.Bars.Where(b => b.YCm > 0).Min(b => b.YCm) - 49, 6); // ряды 19…85 см

      var t48 = f.BarTypes[48];
      Assert.Equal((35.0, 180.0), (t48.TemplateWidthCm, t48.TemplateHeightCm));
      Assert.Equal(38, t48.Bars.Count);
      Assert.All(t48.Bars, b => Assert.Equal(28, b.DiameterMm));

      // Раскладка «Заданное»: координаты от центра, x ‖ B, y ‖ H.
      var concrete = new Material { Id = 1 };
      var (bars, reason) = LiraBarSectionBuilder.AssignedLayout([t38], new LiraBarProfile(1, 0.30, 1.80, concrete, concrete));
      Assert.Null(reason);
      Assert.Equal(18, bars!.Count);
      Assert.Equal(18 * 3.142e-4, bars.Sum(b => b.AreaM2), 7);
      Assert.Contains(bars, b => Math.Abs(b.X - 0.10) < 1e-9 && Math.Abs(b.Y + 0.85) < 1e-9);
      var (_, misfit) = LiraBarSectionBuilder.AssignedLayout([t38], new LiraBarProfile(1, 1.80, 0.30, concrete, concrete));
      Assert.Contains("AS_spec, образец 30×180", misfit);

      // Мозаика: симметричное армирование — низ = верх, стержни на оси делятся пополам.
      var source = new LiraRbtBarRebarSource(f, [new KeyValuePair<string, string?>("7", "38")]);
      double bottom = source.Get("7", BarRebarComponent.Bottom).Value!.Value;
      Assert.Equal(9 * 3.142, bottom, 3);
      Assert.Equal(bottom, source.Get("7", BarRebarComponent.Top).Value!.Value, 9);
   }
}