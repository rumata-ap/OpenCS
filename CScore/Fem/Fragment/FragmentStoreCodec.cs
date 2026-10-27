using System.Text;
using CScore.Planar;

namespace CScore.Fem.Fragment;

/// <summary>
/// Двоичные блоки БД фрагмента: узлы КЭ опорной сетки (подряд double x, y, z) и записи нагрузки-поля
/// (число записей; на запись — тег UTF-8, число значений, значения double). Порядок байтов — little-endian.
/// </summary>
public static class FragmentStoreCodec
{
    public static byte[] EncodePoints(IReadOnlyList<PlanarVector3> points)
    {
        var data = new double[points.Count * 3];
        for (int i = 0; i < points.Count; i++)
        {
            data[3 * i] = points[i].X;
            data[3 * i + 1] = points[i].Y;
            data[3 * i + 2] = points[i].Z;
        }
        var bytes = new byte[data.Length * sizeof(double)];
        Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public static List<PlanarVector3> DecodePoints(byte[] bytes)
    {
        if (bytes.Length % (3 * sizeof(double)) != 0)
            throw new ArgumentException($"Длина блока узлов ({bytes.Length} байт) не кратна точке из 3 чисел.", nameof(bytes));
        var data = new double[bytes.Length / sizeof(double)];
        Buffer.BlockCopy(bytes, 0, data, 0, bytes.Length);
        var result = new List<PlanarVector3>(data.Length / 3);
        for (int i = 0; i < data.Length; i += 3) result.Add(new PlanarVector3(data[i], data[i + 1], data[i + 2]));
        return result;
    }

    public static byte[] EncodeEntries(IReadOnlyList<FragmentFieldEntry> entries)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(entries.Count);
            foreach (var e in entries)
            {
                w.Write(e.RefTag);
                w.Write(e.Values.Count);
                foreach (double v in e.Values) w.Write(v);
            }
        }
        return ms.ToArray();
    }

    public static List<FragmentFieldEntry> DecodeEntries(byte[] bytes)
    {
        try
        {
            using var r = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
            int count = r.ReadInt32();
            if (count < 0) throw new ArgumentException("Отрицательное число записей поля.", nameof(bytes));
            var result = new List<FragmentFieldEntry>(Math.Min(count, 1 << 20));
            for (int i = 0; i < count; i++)
            {
                string tag = r.ReadString();
                int n = r.ReadInt32();
                if (n < 0) throw new ArgumentException("Отрицательное число значений записи поля.", nameof(bytes));
                var values = new double[n];
                for (int k = 0; k < n; k++) values[k] = r.ReadDouble();
                result.Add(new FragmentFieldEntry(tag, values));
            }
            if (r.BaseStream.Position != bytes.Length)
                throw new ArgumentException("Лишние байты в блоке записей поля.", nameof(bytes));
            return result;
        }
        catch (EndOfStreamException ex)
        {
            throw new ArgumentException("Блок записей поля оборван.", nameof(bytes), ex);
        }
    }
}
