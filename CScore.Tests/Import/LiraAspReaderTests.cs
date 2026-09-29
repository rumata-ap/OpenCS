using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>
/// Читатель *.asp ЛИРЫ (подобранная арматура) — на выгрузке схемы 1-lin из ЛИРА-САПФИР 2025.
/// Эталоны — таблицы подбора ЛИРЫ той же схемы (округление до 0,01; значения меньше 0,01 ЛИРА не показывает).
/// </summary>
public class LiraAspReaderTests
{
   static readonly Lazy<LiraAspFile> Scheme = new(() =>
      LiraAspReader.Read(Path.Combine(AppContext.BaseDirectory, "Import", "Fixtures", "asp-scheme-1lin.asp")));

   const int Digits = 2;

   [Fact]
   public void Header_CountsAndNumbering()
   {
      var f = Scheme.Value;

      Assert.Empty(f.Warnings);
      Assert.StartsWith("ARM-SAPFIR_6", f.Signature);
      Assert.Equal("СП 63.13330.2012/2018", f.DesignCode);
      Assert.Equal("Усилия", f.ForceSource);
      Assert.StartsWith("Вариант 1", f.Variant);
      Assert.Equal(46, f.PunchingCount);

      // 278 стержней (1…278) + 5251 пластина (279…5529) = все КЭ схемы.
      Assert.Equal(278, f.Bars.Count);
      Assert.Equal(5251, f.Plates.Count);
      Assert.Equal(Enumerable.Range(1, 278), f.Bars.Keys.Order());
      Assert.Equal(Enumerable.Range(279, 5251), f.Plates.Keys.Order());
      Assert.All(f.Bars.Values, b => Assert.Equal(2, b.Sections.Count));
   }

   [Theory]
   [InlineData(279, 3.89, 3.89, 3.89, 3.89, 0, 0.18)]
   [InlineData(472, 3.89, 3.89, 3.89, 3.89, 105.05, 0.18)]
   [InlineData(862, 3.89, 3.89, 6.04, 6.04, 47.62, 0.18)]
   [InlineData(1446, 2.6, 9.02, 2.6, 7.29, 35.47, 0.2)]
   public void Plate_MatchesLiraTable(int id, double as1, double as2, double as3, double as4, double asw, double h)
   {
      var p = Scheme.Value.Plates[id];

      Assert.Equal(as1, p.As1, Digits);
      Assert.Equal(as2, p.As2, Digits);
      Assert.Equal(as3, p.As3, Digits);
      Assert.Equal(as4, p.As4, Digits);
      Assert.Equal(asw, p.Asw, Digits);
      Assert.Equal(h, p.ThicknessM, 4);
      Assert.Equal("A500", p.RebarClass);
      Assert.Equal("B25", p.ConcreteClass);
   }

   [Fact]
   public void Plate_AswOnlyWhereRequired()
   {
      Assert.Equal(162, Scheme.Value.Plates.Values.Count(p => p.Asw > 0));
   }

   [Fact]
   public void Column_Symmetric_SectionsAndEnvelope()
   {
      var b = Scheme.Value.Bars[1];

      Assert.Equal("A500", b.RebarClass);
      Assert.Equal(29.97, b.Start.Z, 3);
      Assert.Equal(33.12, b.End.Z, 3);
      Assert.Equal((5.0, 5.0, 5.0), b.Covers);
      Assert.All(b.Sections, s => Assert.True(s.IsSymmetric));

      AssertAreas(b.Sections[0].Areas, 0, 0, 0, 0, 1.44, 1.44, 1.44, 1.44, 0.16, 0.13, 0.13);
      AssertAreas(b.Sections[1].Areas, 0, 0, 0, 0, 6.39, 6.39, 6.39, 6.39, 0.71, 0.13, 0.13);
      AssertAreas(b.Envelope, 0, 0, 0, 0, 6.39, 6.39, 6.39, 6.39, 0.71, 0.13, 0.13);
   }

   [Fact]
   public void Beam_Unsymmetric_Sections()
   {
      var b = Scheme.Value.Bars[24];

      Assert.All(b.Sections, s => Assert.False(s.IsSymmetric));
      Assert.True(b.Sections[0].Symmetric.IsEmpty);

      AssertAreas(b.Sections[0].Areas, 0.09, 0.09, 0.96, 0.96, 0.17, 0.17, 0.2, 0.2, 0.35, 8.24, 4.31);
      AssertAreas(b.Sections[1].Areas, 0.24, 0.14, 0.24, 0.14, 0.17, 0.17, 0.2, 0.2, 0.19, 8.14, 4.31);
      AssertAreas(b.Envelope, 0.24, 0.14, 0.96, 0.96, 0.17, 0.17, 0.2, 0.2, 0.35, 8.24, 4.31);
   }

   [Fact]
   public void FailedSection_CarriesLiraMessageCode()
   {
      var f = Scheme.Value;
      // КЭ 146: «Разрушение по наклонной полосе при кручении (п. 8.1.41)» в обоих сечениях.
      var b = f.Bars[146];
      Assert.All(b.Sections, s => Assert.Equal(274, s.Areas.FailureCode));
      Assert.Null(b.Envelope.FailureCode);
      AssertAreas(b.Sections[0].Areas, 0.82, 0.82, 3.04, 3.04, 0.64, 0.64, 0.75, 0.75, 1.31);

      Assert.Equal([146], f.Bars.Values.Where(x => x.Sections.Any(s => s.Areas.FailureCode != null)).Select(x => x.ElementId));
   }

   [Fact]
   public void Envelope_IsMaxOverSections_ForAllBars()
   {
      foreach (var b in Scheme.Value.Bars.Values)
      {
         double sum = b.Sections.Max(s => s.Areas.LongitudinalSum);
         Assert.True(b.Envelope.LongitudinalSum >= sum - 1e-4, $"КЭ {b.ElementId}");
         Assert.Equal(b.Sections.Max(s => s.Areas.As1), b.Envelope.As1, 5);
         Assert.Equal(Math.Max(0, b.Sections.Max(s => s.Areas.Asw1)), b.Envelope.Asw1, 5);
      }
   }

   [Fact]
   public void NotAsp_Throws()
   {
      Assert.Throws<InvalidDataException>(() => LiraAspReader.Read(new byte[300]));
   }

   [Fact]
   public void Truncated_Throws()
   {
      var data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Fixtures", "asp-scheme-1lin.asp"));
      Assert.Throws<InvalidDataException>(() => LiraAspReader.Read(data[..500_000]));
   }

   static void AssertAreas(LiraAspBarAreas a, params double[] expected)
   {
      double[] actual = [a.Au1, a.Au2, a.Au3, a.Au4, a.As1, a.As2, a.As3, a.As4, a.Percent, a.Asw1, a.Asw2];
      for (int i = 0; i < expected.Length; i++)
         Assert.True(Math.Abs(expected[i] - actual[i]) <= 0.0051, $"поле {i}: ожидалось {expected[i]}, получено {actual[i]}");
   }
}

/// <summary>Сверка *.asp со схемой по номерам и видам КЭ.</summary>
public class LiraAspSchemaMatchTests
{
   [Fact]
   public void Check_CountsMissingAndKindMismatch()
   {
      var asp = new LiraAspFile
      {
         Plates = new Dictionary<int, LiraAspPlate>
         {
            [3] = new(3, 1, 1, 1, 1, 0, 0.2, "A500", "B25"),
            [4] = new(4, 1, 1, 1, 1, 0, 0.2, "A500", "B25"),
            [9] = new(9, 1, 1, 1, 1, 0, 0.2, "A500", "B25"),
         },
         Bars = new Dictionary<int, LiraAspBar> { [1] = new() { ElementId = 1 }, [2] = new() { ElementId = 2 } },
      };
      (int, bool)[] schema = [(1, false), (2, true), (3, true), (4, true)];

      var m = LiraAspSchemaMatch.Check(asp, schema);

      Assert.Equal(2, m.PlatesMatched);
      Assert.Equal(1, m.BarsMatched);
      Assert.Equal([9], m.Missing);
      Assert.Equal([2], m.KindMismatch);
   }
}
