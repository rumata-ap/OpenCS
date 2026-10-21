namespace CScore.Fem.Fields;

/// <summary>Точки поля ↔ двоичный блок БД: подряд double [x, y, z, v1…vn] на точку (порядок байтов платформы).</summary>
public static class FemFieldPointCodec
{
    public static byte[] Encode(IReadOnlyList<FieldPoint> points, int valueCount)
    {
        int stride = 3 + valueCount;
        var data = new double[points.Count * stride];
        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            data[i * stride] = p.P.X;
            data[i * stride + 1] = p.P.Y;
            data[i * stride + 2] = p.P.Z;
            for (int k = 0; k < valueCount; k++) data[i * stride + 3 + k] = p.Values[k];
        }
        var bytes = new byte[data.Length * sizeof(double)];
        Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public static List<FieldPoint> Decode(byte[] bytes, int valueCount)
    {
        int stride = 3 + valueCount;
        var data = new double[bytes.Length / sizeof(double)];
        Buffer.BlockCopy(bytes, 0, data, 0, data.Length * sizeof(double));
        var result = new List<FieldPoint>(data.Length / stride);
        for (int i = 0; i + stride <= data.Length; i += stride)
            result.Add(new FieldPoint(new FieldVec3(data[i], data[i + 1], data[i + 2]), data[(i + 3)..(i + stride)]));
        return result;
    }
}
