using CScore.Sp63;
using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63Normal;

/// <summary>Сверяет упрощённую формульную проверку с существующим НДМ.</summary>
public sealed class Sp63NormalNdmComparisonTests
{
   [Fact]
   public void FormulaAndNdm_AgreeOnAdmissibleRectangleWithinTolerance()
   {
      var section = SectionCutFixtures.BuildReinforcedRectangle(0.3, 0.6);
      var load = new LoadItem { N = -800, Mx = -20, My = 0 };
      var formula = Sp63NormalChecker.Check(section, load,
         CalcType.C, Sp63NormalFixtures.MemberOptions());
      var ndm = LimitForceSolver.ForCrossSection(section, CalcType.C)
         .MomentFactor(n: -800, mx: -20, my: 0);
      var profileAnalysis = Sp63RebarLayoutAnalyzer.Analyze(
         section, Sp63NormalAxis.Mx, CalcType.C, tensionDirection: -1);

      Assert.Equal(Sp63NormalStatus.Calculated, formula.Status);
      Assert.True(ndm.Converged,
         $"НДМ не сошёлся: factor={ndm.Factor}, MxLimit={ndm.MxLimit}");
      Assert.NotNull(profileAnalysis.Profile);

      double formulaCapacity = formula.StrengthDetails.Single().Allowable;
      double centroidY = (profileAnalysis.Profile!.Height / 2.0) +
         Sp63RectangularGeometryPolicy.Classify(section).Geometry!.MinY;
      double momentOriginToTensionRebar =
         Math.Abs(profileAnalysis.Profile.TensionLayer.Coordinate - centroidY);
      // НДМ выдаёт внешний Mx относительно центра тяжести, а (8.10) — момент
      // относительно растянутой арматуры: приводим результаты к одной точке.
      double ndmCapacity = Math.Abs(ndm.MxLimit) +
         Math.Abs(load.N) * momentOriginToTensionRebar;
      Assert.InRange(formulaCapacity, ndmCapacity * 0.90, ndmCapacity * 1.10);
   }
}
