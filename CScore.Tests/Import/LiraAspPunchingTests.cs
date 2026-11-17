using System.Text;
using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>
/// Записи продавливания *.asp ЛИРЫ: результат проверки узла (Asw, Ksw, код ошибки, классы, узел) и контур
/// из хвоста файла. Раскладка — по коду экспорта ЛИРА-САПР 2024 (CArmFileBase) и сверке с таблицей 47
/// «Арматура для контуров продавливания» (периметр U = сумма длин отрезков контура).
/// </summary>
public class LiraAspPunchingTests
{
   static readonly Lazy<LiraAspFile> Scheme = new(() =>
      LiraAspReader.Read(Path.Combine(AppContext.BaseDirectory, "Import", "Fixtures", "asp-scheme-1lin.asp")));

   [Fact]
   public void Fixture_RecordsAndContours()
   {
      var f = Scheme.Value;

      Assert.Empty(f.Warnings);
      Assert.Equal(46, f.Punching.Count);
      Assert.Equal(46, f.PunchingCount);
      Assert.Equal(Enumerable.Range(1, 46), f.Punching.Select(p => p.Index));

      var first = f.Punching[0];
      Assert.Equal(497, first.Node);
      Assert.Equal(0, first.ErrorCode);
      Assert.Equal(0, first.Asw);
      Assert.Equal(2.5121, first.Ksw, 4);
      Assert.Equal("A240", first.RebarClass);
      Assert.Equal("B25", first.ConcreteClass);

      // Записи 24…46 — «усилия не соответствуют расчёту на продавливание»: код 170, классы пустые.
      Assert.All(f.Punching.Skip(23), p =>
      {
         Assert.Equal(170, p.ErrorCode);
         Assert.Equal(0, p.Asw);
         Assert.Equal(0, p.Ksw);
         Assert.Contains("N ≥ 0", p.ErrorText);
      });
      Assert.All(f.Punching.Take(23), p => Assert.Null(p.ErrorText));
      Assert.Equal(4892, f.Punching[45].Node);

      // Контуры — в порядке записей; точка контура — узел записи (последний — на отметке 36,27).
      Assert.All(f.Punching, p => Assert.InRange(p.Contour.Count, 2, 4));
      Assert.Equal(36.27, f.Punching[45].Point.Z, 2);
      Assert.Equal(3, f.Punching[45].Contour.Count);
      // Замкнуты только 6 контуров; разомкнутые бывают и из 4 отрезков (запись 2: П с хвостом у края).
      Assert.Equal(6, f.Punching.Count(p => p.IsClosed));
      Assert.All(f.Punching.Where(p => p.Contour.Count < 4), p => Assert.False(p.IsClosed));
      Assert.False(f.Punching[1].IsClosed);
      Assert.Equal(4, f.Punching[1].Contour.Count);
   }

   [Fact]
   public void V6_Synthetic_AllFields()
   {
      var data = Build(6, [
         Rec(1, 44, node: 4940, "A240", "B20", asw: 0, ksw: 2f, err: 0),
         Rec(2, 46, node: 4944, "A240", "B20", asw: 6.0775f, ksw: 1.6f, err: 0),
         Rec(3, 71, node: 15059, "", "", asw: -170f, ksw: 0, err: 170),
      ], [
         Square(7.275, 6.13, 0, 0.418, 0.768),
         Square(7.275, 8.425, 0, 0.418, 0.768),
         Edge(19.275, 2.05, 9.4),
      ]);

      var f = LiraAspReader.Read(data);

      Assert.Empty(f.Warnings);
      Assert.Equal(3, f.Punching.Count);
      var a = f.Punching[0];
      Assert.Equal((1, 44, 4940), (a.Index, a.Group, a.Node!.Value));
      Assert.Equal(("A240", "B20"), (a.RebarClass, a.ConcreteClass));
      Assert.Equal((0.0, 2.0, 0), (a.Asw, a.Ksw, a.ErrorCode));
      Assert.Equal((7.275, 6.13, 0.0), (Math.Round(a.Point.X, 3), Math.Round(a.Point.Y, 3), a.Point.Z));
      Assert.True(a.IsClosed);
      Assert.Equal(2.372, a.Perimeter, 3);

      Assert.Equal(6.0775, f.Punching[1].Asw, 4);
      Assert.Equal(1.6, f.Punching[1].Ksw, 4);

      var e = f.Punching[2];
      Assert.Equal((170, 0.0, 0.0, 15059), (e.ErrorCode, e.Asw, e.Ksw, e.Node!.Value));
      Assert.False(e.IsClosed);
      Assert.Equal(2, e.Contour.Count);
      Assert.Equal(1.0 + 0.5, e.Perimeter, 6);
   }

   [Fact]
   public void V5_Synthetic_NoNodeAndIntegerErrorMarker()
   {
      var data = Build(5, [
         Rec(1, 8, node: 0, "", "", asw: 0, ksw: 0, err: 170, intAsw: -170),
         Rec(2, 276, node: 0, "A240", "B20", asw: 24.8956f, ksw: 1.1908f, err: 0),
      ], [
         Square(6, 6, 4, 0.966, 0.966),
         Square(12, 6, 4, 0.966, 0.966),
      ]);

      var f = LiraAspReader.Read(data);

      Assert.Equal(2, f.Punching.Count);
      Assert.Null(f.Punching[0].Node);
      Assert.Equal((170, 0.0), (f.Punching[0].ErrorCode, f.Punching[0].Asw));
      Assert.Equal(276, f.Punching[1].Group);
      Assert.Equal(24.8956, f.Punching[1].Asw, 4);
      Assert.Equal(1.1908, f.Punching[1].Ksw, 4);
      Assert.Equal(4 * 0.966, f.Punching[1].Perimeter, 4);
   }

   [Fact]
   public void ContourTailMissing_Warns()
   {
      var data = Build(6, [Rec(1, 44, 4940, "A240", "B20", 0, 2f, 0)], []);

      var f = LiraAspReader.Read(data);

      Assert.Single(f.Punching);
      Assert.Empty(f.Punching[0].Contour);
      Assert.Contains(f.Warnings, w => w.Contains("контур", StringComparison.OrdinalIgnoreCase));
   }

   /// <summary>
   /// Доп. записи пластин без маркера −777, но с ненулевыми первыми полями (v5, пример 23 ЛИРЫ: −0,15 в +8)
   /// не мешают найти блок; ASW у них 0, у записей с маркером — из +20.
   /// </summary>
   [Fact]
   public void PlateExtras_UnmarkedRecordWithLeadingValues_Accepted()
   {
      var data = Build(5, [Rec(1, 8, 0, "A240", "B20", 1.5f, 1.2f, 0)], [Square(6, 6, 4, 1, 1)], plates: 3,
         extras: [(true, 12.5f), (false, 0), (true, 3f)]);

      var f = LiraAspReader.Read(data);

      Assert.Empty(f.Warnings);
      Assert.Equal(3, f.Plates.Count);
      Assert.Equal(12.5, f.Plates[1].Asw);
      Assert.Equal(0, f.Plates[2].Asw);
      Assert.Equal(3, f.Plates[3].Asw);
      Assert.Equal(4, f.Punching[0].Perimeter, 6);
   }

   // ---------------------------------------------------------------- синтетический *.asp без стержней

   sealed record RecSpec(int Index, int Group, int Node, string Rs, string Bt, float Asw, float Ksw, ushort Err, int? IntAsw);

   static RecSpec Rec(int index, int group, int node, string rs, string bt, float asw, float ksw, ushort err, int? intAsw = null) =>
      new(index, group, node, rs, bt, asw, ksw, err, intAsw);

   static (float[] Point, float[][] Segments) Square(double x, double y, double z, double bx, double by)
   {
      float x0 = (float)(x - bx / 2), x1 = (float)(x + bx / 2), y0 = (float)(y - by / 2), y1 = (float)(y + by / 2), zz = (float)z;
      return ([(float)x, (float)y, zz], [
         [x0, y0, zz, x1, y0, zz], [x1, y0, zz, x1, y1, zz], [x1, y1, zz, x0, y1, zz], [x0, y1, zz, x0, y0, zz]]);
   }

   /// <summary>Краевой контур: два отрезка (1 м вдоль x, 0,5 м вдоль y), разомкнут.</summary>
   static (float[] Point, float[][] Segments) Edge(double x, double y, double z)
   {
      float xx = (float)x, yy = (float)y, zz = (float)z;
      return ([xx, yy, zz], [[xx - 1, yy, zz, xx, yy, zz], [xx, yy, zz, xx, yy + 0.5f, zz]]);
   }

   static byte[] Build(int version, RecSpec[] recs, (float[] Point, float[][] Segments)[] contours,
      int plates = 0, (bool Marked, float Asw)[]? extras = null)
   {
      Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
      var cp = Encoding.GetEncoding(1251);
      int size = version >= 6 ? 120 : 88;
      using var ms = new MemoryStream();
      using var w = new BinaryWriter(ms);

      var header = new byte[272];
      cp.GetBytes(version >= 6 ? "ARM-SAPFIR_6 binary v.6.0 2022" : "ARM-SAPFIR_5 binary v.5.0 2015").CopyTo(header, 0);
      BitConverter.GetBytes(plates).CopyTo(header, 96);
      BitConverter.GetBytes(156).CopyTo(header, 100);
      BitConverter.GetBytes(recs.Length).CopyTo(header, 240);
      BitConverter.GetBytes(size).CopyTo(header, 244);
      BitConverter.GetBytes(0).CopyTo(header, 252);
      w.Write(header);

      for (int i = 0; i < plates; i++)
      {
         var b = new byte[156];
         BitConverter.GetBytes(i).CopyTo(b, 0);   // номер КЭ − 1
         cp.GetBytes("A500").CopyTo(b, 68);
         cp.GetBytes("B25").CopyTo(b, 100);
         BitConverter.GetBytes(0.2f).CopyTo(b, 148);
         w.Write(b);
      }

      foreach (var r in recs)
      {
         var b = new byte[size];
         BitConverter.GetBytes(r.Index).CopyTo(b, 0);
         BitConverter.GetBytes(r.Group).CopyTo(b, 4);
         BitConverter.GetBytes(7).CopyTo(b, 8);
         cp.GetBytes(r.Rs).CopyTo(b, 12);
         cp.GetBytes(r.Bt).CopyTo(b, 44);
         (r.IntAsw is int ia ? BitConverter.GetBytes(ia) : BitConverter.GetBytes(r.Asw)).CopyTo(b, 76);
         BitConverter.GetBytes(r.Ksw).CopyTo(b, 80);
         BitConverter.GetBytes(r.Err).CopyTo(b, 84);
         if (version >= 6)
         {
            BitConverter.GetBytes((ushort)1).CopyTo(b, 86);   // в выгрузках 2025 здесь 1
            BitConverter.GetBytes(r.Node).CopyTo(b, 104);
         }
         w.Write(b);
      }

      foreach (var (marked, asw) in extras ?? [])
      {
         var b = new byte[24];
         if (marked)
         {
            BitConverter.GetBytes(-777f).CopyTo(b, 16);
            BitConverter.GetBytes(asw).CopyTo(b, 20);
         }
         else BitConverter.GetBytes(-0.15f).CopyTo(b, 8);
         w.Write(b);
      }

      foreach (var (p, segs) in contours)
      {
         foreach (float v in p) w.Write(v);
         w.Write(segs.Length);
         foreach (var s in segs)
            foreach (float v in s) w.Write(v);
      }
      if (contours.Length > 0)
      {
         // За контурами ЛИРА пишет нулевую точку и блоки параметров армирования.
         w.Write(new byte[16]);
         w.Write(new byte[65]);
      }
      return ms.ToArray();
   }
}
