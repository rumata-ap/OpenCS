using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using CScore;
using CScore.LimitSurface;

namespace OpenCS.LimitSurface;

/// <summary>Строка набора усилий, снятая при расчёте (правка набора потом не меняет результат).</summary>
public sealed record LimitSurfacePointRecord(int Num, string Label, double N, double Mx, double My);

/// <summary>Контрольная точка области деформаций — для подписей активного ограничения.</summary>
public sealed record LimitSurfaceControlPointRecord(StrainPointKind Kind, double X, double Y, int AreaIndex);

/// <summary>DataJson чужой версии формата — результат надо пересчитать.</summary>
public sealed class LimitSurfaceFormatException(string message) : Exception(message);

/// <summary>Коды предупреждений результата.</summary>
public static class LimitSurfaceWarnings
{
   /// <summary>Точки набора первого порядка нанесены без увеличения моментов на η продольного изгиба.</summary>
   public const string EtaNotApplied = "etaNotApplied";
}

/// <summary>
/// Результат задачи поверхности предельных усилий в CalcResult.DataJson. Сетка — бинарный блок
/// (gzip + base64): вершины, треугольники, плоскости деформаций, активные ограничения, меридианы.
/// Изломы и журнал адаптации не сохраняются — они нужны только для отладки построителя.
/// </summary>
public sealed class LimitSurfaceResultData
{
   /// <summary>Текущая версия формата.</summary>
   public const int CurrentFormatVersion = 1;

   static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

   /// <summary>Версия формата; чужая при разборе — <see cref="LimitSurfaceFormatException"/>.</summary>
   public int FormatVersion { get; set; } = CurrentFormatVersion;
   /// <summary>Критерий поверхности: «uls» — ПС1 (подпроекты 3, 4 добавят свои).</summary>
   public string Criterion { get; set; } = "uls";
   /// <summary>Набор расчётных характеристик задачи.</summary>
   public CalcType CalcType { get; set; }
   /// <summary>Учитывалась ли работа бетона на растяжение.</summary>
   public bool Ten { get; set; }
   /// <summary>Точность построения: «normal» | «high».</summary>
   public string Accuracy { get; set; } = "normal";
   /// <summary>Контрольные точки области деформаций (индексы — как в <see cref="ActiveConstraint.PointIndex"/>).</summary>
   public List<LimitSurfaceControlPointRecord> ControlPoints { get; set; } = [];
   /// <summary>Сетка, см. <see cref="EncodeMesh"/>.</summary>
   public string Mesh { get; set; } = "";
   /// <summary>Снимок строк набора усилий (кН, кН·м).</summary>
   public List<LimitSurfacePointRecord> Points { get; set; } = [];
   /// <summary>Набор усилий, из которого сняты точки; 0 — без точек.</summary>
   public int ForceSetId { get; set; }
   /// <summary>Имя набора на момент расчёта.</summary>
   public string? ForceSetName { get; set; }
   /// <summary>Коды предупреждений, см. <see cref="LimitSurfaceWarnings"/>.</summary>
   public List<string> Warnings { get; set; } = [];
   /// <summary>Число вершин сетки.</summary>
   public int VertexCount { get; set; }
   /// <summary>Число меридианов сетки.</summary>
   public int MeridianCount { get; set; }
   /// <summary>Время построения сетки, с.</summary>
   public double BuildSeconds { get; set; }

   /// <summary>Сериализация в DataJson.</summary>
   public string ToJson() => JsonSerializer.Serialize(this, Json);

   /// <summary>Разбор DataJson; чужая версия формата — <see cref="LimitSurfaceFormatException"/>.</summary>
   public static LimitSurfaceResultData Parse(string json)
   {
      var data = JsonSerializer.Deserialize<LimitSurfaceResultData>(json, Json)
         ?? throw new LimitSurfaceFormatException("Пустой результат поверхности.");
      if (data.FormatVersion != CurrentFormatVersion)
         throw new LimitSurfaceFormatException($"Версия формата {data.FormatVersion} не поддерживается.");
      return data;
   }

   /// <summary>Сетка → gzip + base64.</summary>
   public static string EncodeMesh(LimitSurfaceMesh mesh)
   {
      using var ms = new MemoryStream();
      using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
      using (var w = new BinaryWriter(gz))
      {
         w.Write(mesh.Vertices.Count);
         foreach (var v in mesh.Vertices) { w.Write(v.N); w.Write(v.Mx); w.Write(v.My); }
         w.Write(mesh.Triangles.Count);
         foreach (var (a, b, c) in mesh.Triangles) { w.Write(a); w.Write(b); w.Write(c); }
         w.Write(mesh.StrainPlanes.Count);
         foreach (var k in mesh.StrainPlanes) { w.Write(k.e0); w.Write(k.ky); w.Write(k.kz); }
         w.Write(mesh.Active.Count);
         foreach (var a in mesh.Active) { w.Write(a.PointIndex); w.Write((sbyte)a.Side); w.Write(a.SingleSign); }
         w.Write(mesh.MeridianOf.Count);
         foreach (int m in mesh.MeridianOf) w.Write(m);
      }
      return Convert.ToBase64String(ms.ToArray());
   }

   /// <summary>gzip + base64 → сетка.</summary>
   public static LimitSurfaceMesh DecodeMesh(string encoded)
   {
      using var gz = new GZipStream(new MemoryStream(Convert.FromBase64String(encoded)), CompressionMode.Decompress);
      using var r = new BinaryReader(gz);
      var vertices = new ForcePoint[r.ReadInt32()];
      for (int i = 0; i < vertices.Length; i++) vertices[i] = new(r.ReadDouble(), r.ReadDouble(), r.ReadDouble());
      var triangles = new (int, int, int)[r.ReadInt32()];
      for (int i = 0; i < triangles.Length; i++) triangles[i] = (r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
      var planes = new Kurvature[r.ReadInt32()];
      for (int i = 0; i < planes.Length; i++) planes[i] = new Kurvature { e0 = r.ReadDouble(), ky = r.ReadDouble(), kz = r.ReadDouble() };
      var active = new ActiveConstraint[r.ReadInt32()];
      for (int i = 0; i < active.Length; i++) active[i] = new(r.ReadInt32(), r.ReadSByte(), r.ReadBoolean());
      var meridianOf = new int[r.ReadInt32()];
      for (int i = 0; i < meridianOf.Length; i++) meridianOf[i] = r.ReadInt32();
      return new LimitSurfaceMesh(vertices, triangles, planes, active, meridianOf: meridianOf);
   }
}
