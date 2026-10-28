using CScore.Submodel;

namespace CScore.Fem.Fragment.Masks;

/// <summary>
/// Двоичный блок сэмплов маски (БД v94, <c>fem_fragment_mask_items.data</c>): байт версии, число сэмплов; на сэмпл —
/// s (double), флаги (1 — есть U, 2 — есть F), затем 6 double U и/или 6 double F. Little-endian.
/// </summary>
public static class FragmentMaskCodec
{
    const byte Version = 1, HasU = 1, HasF = 2;

    public static byte[] Encode(IReadOnlyList<FragmentMaskSample> samples)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write(Version);
            w.Write(samples.Count);
            foreach (var s in samples)
            {
                w.Write(s.S);
                w.Write((byte)((s.U != null ? HasU : 0) | (s.F != null ? HasF : 0)));
                if (s.U is { } u) Write(w, u);
                if (s.F is { } f) Write(w, f);
            }
        }
        return ms.ToArray();
    }

    /// <summary>Разбор блока; неизвестная версия или обрыв — <see cref="InvalidDataException"/>.</summary>
    public static List<FragmentMaskSample> Decode(byte[] data)
    {
        try
        {
            using var r = new BinaryReader(new MemoryStream(data));
            byte version = r.ReadByte();
            if (version != Version) throw new InvalidDataException($"Неизвестная версия блока маски: {version}.");
            int n = r.ReadInt32();
            var result = new List<FragmentMaskSample>(n);
            for (int i = 0; i < n; i++)
            {
                double s = r.ReadDouble();
                byte flags = r.ReadByte();
                Dof6? u = (flags & HasU) != 0 ? Read(r) : null;
                Dof6? f = (flags & HasF) != 0 ? Read(r) : null;
                result.Add(new FragmentMaskSample(s, u, f));
            }
            return result;
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("Блок маски оборван.", ex);
        }
    }

    static void Write(BinaryWriter w, Dof6 v)
    {
        for (int i = 0; i < 6; i++) w.Write(v[i]);
    }

    static Dof6 Read(BinaryReader r) =>
        new(r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble());
}
