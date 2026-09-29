using CScore.Import;
using CScore.PlateRebar;
using Xunit;

namespace CScore.Tests.PlateRebar;

/// <summary>Мозаика армирования пластин: источники ЛИРЫ (ASP/RBT), разность, дискретная шкала.</summary>
public class PlateRebarMosaicTests
{
   static LiraAspFile Asp() => new()
   {
      Plates = new Dictionary<int, LiraAspPlate>
      {
         [1] = new(1, 1.1, 2.2, 3.3, 4.4, 0.5, 0.2, "A500", "B25"),
         [2] = new(2, 6, 0, 0, -274, 0, 0.2, "A500", "B25"),
      },
   };

   static LiraPlateReinforcementType Type(int id, params (LiraPlateRebarSlot Slot, double Area)[] layers) => new()
   {
      Id = id,
      Kind = LiraRbtReader.KindPlateSimple,
      Binding = LiraRebarBinding.Centroid,
      Layers = layers.Select(l => new LiraPlateRebarLayer { Slot = l.Slot, IsTotalArea = true, TotalAreaCm2 = l.Area, A = 3 }).ToList(),
   };

   static LiraRbtFile Rbt() => new()
   {
      PlateTypes = new Dictionary<int, LiraPlateReinforcementType>
      {
         [1] = Type(1, (LiraPlateRebarSlot.XB, 3.93), (LiraPlateRebarSlot.XT, 3.93)),
         [2] = Type(2, (LiraPlateRebarSlot.XB, 2.0)),
      },
   };

   static LiraRbtPlateRebarSource RbtSource() => new(Rbt(), new Dictionary<string, string?>
   {
      ["1"] = "1 2",
      ["2"] = "1",
      ["3"] = null,
      ["4"] = "1 9",
   });

   [Fact]
   public void Asp_ComponentsMapToAs1ToAs4AndAsw()
   {
      var s = new LiraAspPlateRebarSource(Asp());
      Assert.Equal(1.1, s.Get("1", PlateRebarMosaicComponent.BottomX).Value);
      Assert.Equal(2.2, s.Get("1", PlateRebarMosaicComponent.TopX).Value);
      Assert.Equal(3.3, s.Get("1", PlateRebarMosaicComponent.BottomY).Value);
      Assert.Equal(4.4, s.Get("1", PlateRebarMosaicComponent.TopY).Value);
      Assert.Equal(0.5, s.Get("1", PlateRebarMosaicComponent.Transverse).Value);
   }

   [Fact]
   public void Asp_NegativeIsFailure_UnknownElementIsMissing()
   {
      var s = new LiraAspPlateRebarSource(Asp());
      Assert.Equal(274, s.Get("2", PlateRebarMosaicComponent.TopY).FailureCode);
      Assert.True(s.Get("7", PlateRebarMosaicComponent.TopY).IsMissing);
      Assert.True(s.Get("abc", PlateRebarMosaicComponent.TopY).IsMissing);
   }

   [Fact]
   public void Rbt_CompositeCellSums_AbsentSlotIsZero_UnknownTypeIsMissing()
   {
      var s = RbtSource();
      Assert.Equal(5.93, s.Get("1", PlateRebarMosaicComponent.BottomX).Value!.Value, 9);
      Assert.Equal(3.93, s.Get("1", PlateRebarMosaicComponent.TopX).Value!.Value, 9);
      Assert.Equal(0, s.Get("2", PlateRebarMosaicComponent.BottomY).Value);
      Assert.True(s.Get("3", PlateRebarMosaicComponent.BottomX).IsMissing);
      Assert.True(s.Get("4", PlateRebarMosaicComponent.BottomX).IsMissing);
      Assert.False(s.Supports(PlateRebarMosaicComponent.Transverse));
   }

   [Fact]
   public void Difference_IsAssignedMinusRequired_AndPropagatesFailure()
   {
      var d = new PlateRebarDifferenceSource(RbtSource(), new LiraAspPlateRebarSource(Asp()));
      Assert.Equal(5.93 - 1.1, d.Get("1", PlateRebarMosaicComponent.BottomX).Value!.Value, 9);
      Assert.Equal(3.93 - 6, d.Get("2", PlateRebarMosaicComponent.BottomX).Value!.Value, 9);
      Assert.Equal(274, d.Get("2", PlateRebarMosaicComponent.TopY).FailureCode);
      Assert.True(d.Get("3", PlateRebarMosaicComponent.BottomX).IsMissing);
      Assert.False(d.Supports(PlateRebarMosaicComponent.Transverse));
   }

   [Fact]
   public void AutoScale_HasZeroBand_MonotoneThresholds_MaxInLastBand()
   {
      var scale = PlateRebarMosaicScale.Auto([0, 1.2, 7.7, 15.3], diverging: false);
      Assert.True(scale.Bands[0].IsZero);
      Assert.Equal(9, scale.Bands.Count);
      Assert.Equal(2.0, scale.Thresholds[0], 9);            // 15,3 / 8 → шаг 2
      Assert.True(scale.Thresholds.Zip(scale.Thresholds.Skip(1)).All(p => p.First < p.Second));
      Assert.Equal(0, scale.BandOf(0));
      Assert.Equal(1, scale.BandOf(1.2));
      Assert.Equal(scale.Bands.Count - 1, scale.BandOf(15.3));
   }

   [Fact]
   public void ManualScale_DropsNonPositiveThresholds()
   {
      var scale = PlateRebarMosaicScale.Manual([5.65, 3.93, 0, -1, 3.93], diverging: false);
      Assert.Equal([3.93, 5.65], scale.Thresholds);
      Assert.Equal(1, scale.BandOf(2));
      Assert.Equal(2, scale.BandOf(3.93));
      Assert.Equal(3, scale.BandOf(10));
   }

   [Fact]
   public void DivergingScale_IsSymmetric_NegativeBandsBelowZero()
   {
      var scale = PlateRebarMosaicScale.Auto([-3, 1, 7], diverging: true);
      Assert.Equal(8, scale.Bands.Count);                  // 7 / 4 → шаг 2, 4 полосы в каждую сторону
      Assert.Equal(-scale.Thresholds[0], scale.Thresholds[^1], 9);
      Assert.Contains(0.0, scale.Thresholds);
      Assert.True(scale.Bands[scale.BandOf(-0.1)].IsNegative);
      Assert.True(scale.Bands[scale.BandOf(0)].IsPositive);
      Assert.True(scale.Bands[scale.BandOf(7)].IsPositive);
   }

   [Fact]
   public void AutoScale_DropsEmptyUpperBands()
   {
      var scale = PlateRebarMosaicScale.Auto([0.3, 2.1], diverging: false);   // шаг 0,5 → до 2,5, а не до 4
      Assert.Equal([0.5, 1.0, 1.5, 2.0], scale.Thresholds);
      Assert.Equal(scale.Bands.Count - 1, scale.BandOf(2.1));
   }

   [Theory]
   [InlineData(0.0, 1.0)]
   [InlineData(1.9, 2.0)]
   [InlineData(2.2, 2.5)]
   [InlineData(0.3, 0.5)]
   [InlineData(6.0, 10.0)]
   public void NiceStep_RoundsUp(double raw, double expected) =>
      Assert.Equal(expected, PlateRebarMosaicScale.NiceStep(raw), 9);

   [Fact]
   public void RealScheme1Lin_AutoScaleCoversAllPlates()
   {
      var asp = LiraAspReader.Read(Path.Combine(AppContext.BaseDirectory, "Import", "Fixtures", "asp-scheme-1lin.asp"));
      var source = new LiraAspPlateRebarSource(asp);
      var tags = asp.Plates.Keys.Select(k => k.ToString()).ToList();
      foreach (var comp in Enum.GetValues<PlateRebarMosaicComponent>())
      {
         var values = PlateRebarMosaic.Evaluate(source, comp, tags);
         Assert.All(values.Values, v => Assert.False(v.IsMissing));
         var numbers = values.Values.Where(v => v.Value != null).Select(v => v.Value!.Value).ToList();
         var scale = PlateRebarMosaicScale.Auto(numbers, diverging: false);
         Assert.All(numbers, x => Assert.InRange(scale.BandOf(x), 0, scale.Bands.Count - 1));
         // Верхняя полоса не пустая: шкала доходит до максимума, а не обрывается раньше.
         Assert.Contains(numbers, x => scale.BandOf(x) == scale.Bands.Count - 1);
      }
   }
}
