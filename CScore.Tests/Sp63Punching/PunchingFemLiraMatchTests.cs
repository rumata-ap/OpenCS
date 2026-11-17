using CScore.Import;
using CScore.Planar;
using CScore.Sp63Punching.Fem;
using Xunit;

namespace CScore.Tests.Sp63Punching;

/// <summary>Сопоставление узлов продавливания ЛИРЫ (*.asp) с узлами проверки по МКЭ.</summary>
public class PunchingFemLiraMatchTests
{
   static LiraAspPunching Rec(int index, int? node, double x, double y, double z, bool contour = true) => new()
   {
      Index = index, Node = node, Point = (x, y, z),
      Contour = contour ? [new LiraAspSegment(x - 0.3, y, z, x + 0.3, y, z)] : [],
   };

   static readonly (string, PlanarVector3)[] Joints =
   [
      ("4940", new PlanarVector3(7.275, 6.13, 0)),
      ("4939", new PlanarVector3(7.275, 6.13, -3.4)),
      ("15059", new PlanarVector3(1, 2, 3)),
   ];

   [Fact]
   public void ByNodeNumber_V6()
   {
      var m = PunchingFemLiraMatch.Build([Rec(1, 4940, 7.275, 6.13, 0), Rec(2, 4939, 7.275, 6.13, -3.4)], Joints);

      Assert.Equal(1, m.ByNode["4940"].Index);
      Assert.Equal(2, m.ByNode["4939"].Index);
      Assert.Empty(m.Unmatched);
   }

   [Fact]
   public void ByPoint_V5_WithinTolerance()
   {
      var m = PunchingFemLiraMatch.Build([Rec(1, null, 7.28, 6.13, -3.4), Rec(2, null, 1.0, 2.0, 3.02)], Joints);

      Assert.Equal(1, m.ByNode["4939"].Index);
      Assert.Single(m.Unmatched);   // 2 см — дальше допуска 1 см
      Assert.Equal(2, m.Unmatched[0].Index);
   }

   [Fact]
   public void UnknownNumber_FallsBackToPoint_AndJointNotReused()
   {
      var m = PunchingFemLiraMatch.Build([
         Rec(1, 4940, 7.275, 6.13, 0),
         Rec(2, 99999, 7.275, 6.13, 0),          // номер не найден, точка занята узлом 4940
         Rec(3, 77777, 1, 2, 3),                 // номер не найден, точка — узел 15059
         Rec(4, null, 1, 2, 3, contour: false),  // без контура точка не известна
      ], Joints);

      Assert.Equal(1, m.ByNode["4940"].Index);
      Assert.Equal(3, m.ByNode["15059"].Index);
      Assert.Equal([2, 4], m.Unmatched.Select(r => r.Index));
   }
}
