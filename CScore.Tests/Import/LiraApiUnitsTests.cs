using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>Пересчёт единиц документа ЛИРЫ (LiraMeasurementUnits) в м, кН, кПа.</summary>
public class LiraApiUnitsTests
{
   const double TonToKn = 9.81;

   [Theory]
   [InlineData(0, 1.0)]
   [InlineData(1, 0.01)]
   [InlineData(2, 0.001)]
   [InlineData(3, 0.3048)]
   [InlineData(4, 0.0254)]
   public void LengthToM_GeometryCodes(int code, double expected) =>
      Assert.Equal(expected, LiraApiUnits.LengthToM(code, 1.0), 12);

   [Fact]
   public void LengthToM_NoneOrUnknown_GivesFallback()
   {
      Assert.Equal(0.01, LiraApiUnits.LengthToM(-1, 0.01));
      Assert.Equal(1.0, LiraApiUnits.LengthToM(9, 1.0));
   }

   /// <summary>
   /// Документ «координаты — мм, усилия — т·м, напряжения — кН/м²» (Forces1 = т, Forces2 = м). Единица
   /// координат на усилия не влияет; напряжения API отдаёт в т/м² — сверено с таблицей ЛИРЫ (КЭ 19031,
   /// РСН 1: из API σy = −4,841, в таблице −47,477 кН/м²).
   /// </summary>
   [Fact]
   public void FromCodes_TonMetre_StressesInForceUnits()
   {
      var u = LiraApiUnits.FromCodes(forces1: 2, forces2: 0, TonToKn);

      Assert.Equal(98.1, u.Force(10), 9);            // 10 т → кН
      Assert.Equal(98.1, u.Moment(10), 9);           // 10 т·м → кН·м
      Assert.Equal(98.1, u.PerLength(10), 9);        // 10 т/м → кН/м
      Assert.Equal(98.1, u.MomentPerLength(10), 9);  // 10 т·м/м → кН·м/м
      Assert.Equal(-47.49, u.Stress(-4.841), 2);     // т/м² → кПа
   }

   [Fact]
   public void FromCodes_KnMillimetre()
   {
      var u = LiraApiUnits.FromCodes(forces1: 4, forces2: 2, TonToKn);

      Assert.Equal(1.0, u.Moment(1000), 12);         // 1000 кН·мм = 1 кН·м
      Assert.Equal(1000, u.PerLength(1), 9);         // 1 кН/мм = 1000 кН/м
      Assert.Equal(5, u.MomentPerLength(5), 12);
      Assert.Equal(1e6, u.Stress(1), 3);             // 1 кН/мм² = 10⁶ кПа
   }
}
