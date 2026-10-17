using System.Globalization;
using System.IO;
using System.IO.Compression;
using CScore.Fem;
using CScore.Fem.Loads;
using CSfea.CScoreBridge.Structural;

namespace OpenCS.Services;

/// <summary>
/// Вектор величин огибающей по полям шага CSfea (раскладка по опорному шагу — номера узлов и КЭ у всех полей одни):
/// узел — 6 перемещений и |u|; пластина — 8 усилий, ux/uy/uz среднее по узлам КЭ (м), отпор C1 (кПа, NaN без основания);
/// стержень — 12 концевых сил; опора — 6 реакций; в конце — трещины пластин (<see cref="CrackQ"/> на КЭ: доли треснувших
/// слоёв низа и верха, a_crc,1 низа и верха, a_crc низа и верха, мм). Всё, кроме |u| и трещин, линейно по загружениям
/// (огибающая линейного расчёта суммирует векторы загружений, |u| — после суммы, трещин у неё нет — NaN); у пакета
/// вектор варианта — одно состояние: |u| и трещины сразу.
/// </summary>
public sealed class FemCsfeaEnvelopeQuantities
{
    public const int NodeQ = RcSecantStepFields.NodeComponents + 1;
    public const int ShellQ = RcSecantStepFields.ShellForceComponents + 4;
    public const int CrackQ = 6;

    readonly IReadOnlyDictionary<int, int[]> _contours;
    readonly IReadOnlyDictionary<int, (double C1, double Nx, double Ny, double Nz)> _foundation;

    /// <param name="contours">Контур пластины (номера узлов сетки) по номеру КЭ.</param>
    /// <param name="foundation">Пластины на основании: коэффициент постели (Н/м³) и единичная нормаль (n_z ≥ 0).</param>
    public FemCsfeaEnvelopeQuantities(IReadOnlyDictionary<int, int[]> contours,
        IReadOnlyDictionary<int, (double C1, double Nx, double Ny, double Nz)> foundation)
    {
        _contours = contours;
        _foundation = foundation;
    }

    /// <summary>Контуры и основание пластин по сетке схемы (как в окне результата).</summary>
    public static FemCsfeaEnvelopeQuantities FromMesh(IReadOnlyList<FemMeshNode> meshNodes, IReadOnlyList<FemElement> elements)
    {
        var byTag = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        var points = new Dictionary<int, (double X, double Y, double Z)>();
        foreach (var n in meshNodes)
            if (FemMeshTopology.CanonicalNodeTag(n.NodeTag) is { } tag && int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
            {
                byTag.TryAdd(tag, n);
                points[id] = (n.X, n.Y, n.Z);
            }
        var contours = new Dictionary<int, int[]>();
        var foundation = new Dictionary<int, (double, double, double, double)>();
        foreach (var e in elements)
        {
            if (!int.TryParse(e.ElemTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tag)) continue;
            if (FemElementGeometry.Of(e, byTag) is not { IsShell: true } g) continue;
            var ids = g.NodeTags.Select(t => int.Parse(t, CultureInfo.InvariantCulture)).ToArray();
            var contour = g.Contour.Select(i => ids[i]).ToArray();
            contours[tag] = contour;
            if (e.FoundationC1 is double c1 && c1 > 0)
            {
                var (x, y, z) = UpNormal(contour.Select(n => points[n]).ToArray());
                foundation[tag] = (c1, x, y, z);
            }
        }
        return new FemCsfeaEnvelopeQuantities(contours, foundation);
    }

    /// <summary>Единичная нормаль многоугольника, направленная вверх (n_z ≥ 0; в вертикальной плоскости — по X, затем Y).</summary>
    public static (double X, double Y, double Z) UpNormal(IReadOnlyList<(double X, double Y, double Z)> p)
    {
        double nx = 0, ny = 0, nz = 0;
        for (int i = 0; i < p.Count; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % p.Count];
            nx += (a.Y - b.Y) * (a.Z + b.Z);
            ny += (a.Z - b.Z) * (a.X + b.X);
            nz += (a.X - b.X) * (a.Y + b.Y);
        }
        double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (len < 1e-300) return (0, 0, 1);
        nx /= len; ny /= len; nz /= len;
        const double eps = 1e-9;
        bool flip = nz < -eps || Math.Abs(nz) <= eps && (nx < -eps || Math.Abs(nx) <= eps && ny < 0);
        return flip ? (-nx, -ny, -nz) : (nx, ny, nz);
    }

    /// <summary>Индекс узла в полях по номеру узла.</summary>
    public static Dictionary<int, int> NodeIndex(RcSecantStepFields f)
    {
        var index = new Dictionary<int, int>();
        for (int i = 0; i < f.NodeIds.Length; i++) index.TryAdd(f.NodeIds[i], i);
        return index;
    }

    /// <summary>Начала разделов вектора: пластины, стержни, опоры, трещины пластин.</summary>
    public static (int Shell, int Beam, int Reaction, int Crack) Offsets(RcSecantStepFields reference)
    {
        int shell = reference.NodeIds.Length * NodeQ;
        int beam = shell + reference.ShellIds.Length * ShellQ;
        int reaction = beam + reference.BeamIds.Length * RcSecantStepFields.BeamForceComponents;
        return (shell, beam, reaction, reaction + reference.ReactionNodeIds.Length * RcSecantStepFields.NodeComponents);
    }

    /// <summary>|u| узлов куска вектора (куски начинаются с записи узла); <paramref name="nodeEnd"/> — конец раздела узлов.</summary>
    public static void DeriveNodeNorms(Span<double> v, int offset, int nodeEnd)
    {
        for (int o = 0; o < v.Length && offset + o < nodeEnd; o += NodeQ)
            v[o + 6] = Math.Sqrt(v[o] * v[o] + v[o + 1] * v[o + 1] + v[o + 2] * v[o + 2]);
    }

    /// <summary>
    /// Величины огибающей полей. <paramref name="single"/> — вектор одного состояния (вариант пакета): |u| и доли трещин
    /// сразу; иначе — слагаемое суммы (загружение линейного расчёта): |u| — NaN, досчитывается после суммы, трещин нет.
    /// <paramref name="cracks"/> — ширина раскрытия КЭ состояния; null или КЭ нет — NaN.
    /// </summary>
    public double[] Vector(RcSecantStepFields f, Dictionary<int, int> nodeIndex, bool single = false,
        IReadOnlyDictionary<int, FemCsfeaCrackWidth>? cracks = null)
    {
        int nn = f.NodeIds.Length, ns = f.ShellIds.Length;
        const int sc = RcSecantStepFields.ShellForceComponents, nc = RcSecantStepFields.NodeComponents;
        var v = new double[nn * NodeQ + ns * ShellQ + f.BeamForces.Length + f.Reactions.Length + ns * CrackQ];
        var d = f.Displacements;
        for (int i = 0; i < nn; i++)
        {
            for (int c = 0; c < nc; c++) v[i * NodeQ + c] = d[nc * i + c];
            v[i * NodeQ + nc] = double.NaN;
        }
        if (single) DeriveNodeNorms(v, 0, nn * NodeQ);
        int o = nn * NodeQ;
        for (int i = 0; i < ns; i++, o += ShellQ)
        {
            Array.Copy(f.ShellForces, i * sc, v, o, sc);
            int elem = f.ShellIds[i];
            if (!_contours.TryGetValue(elem, out var contour))
            {
                for (int c = 0; c < 4; c++) v[o + sc + c] = double.NaN;
                continue;
            }
            // Как ShellValue/FoundationPressure окна результата: среднее по узлам контура (узел вне полей — ноль).
            for (int c = 0; c < 3; c++)
            {
                double sum = 0;
                foreach (int n in contour)
                    if (nodeIndex.TryGetValue(n, out int k)) sum += d[nc * k + c];
                v[o + sc + c] = sum / contour.Length;
            }
            if (_foundation.TryGetValue(elem, out var fd))
            {
                double un = 0;
                foreach (int n in contour)
                    if (nodeIndex.TryGetValue(n, out int k))
                        un += fd.Nx * d[nc * k] + fd.Ny * d[nc * k + 1] + fd.Nz * d[nc * k + 2];
                v[o + sc + 3] = -fd.C1 * un / contour.Length / 1e3;
            }
            else v[o + sc + 3] = double.NaN;
        }
        Array.Copy(f.BeamForces, 0, v, o, f.BeamForces.Length);
        Array.Copy(f.Reactions, 0, v, o + f.BeamForces.Length, f.Reactions.Length);
        o += f.BeamForces.Length + f.Reactions.Length;
        const int st = RcSecantStepFields.ShellStateComponents;
        for (int i = 0; i < ns; i++, o += CrackQ)
        {
            v[o] = single && f.ShellStates.Length >= (i + 1) * st ? f.ShellStates[i * st] : double.NaN;
            v[o + 1] = single && f.ShellStates.Length >= (i + 1) * st ? f.ShellStates[i * st + 1] : double.NaN;
            if (single && cracks != null && cracks.TryGetValue(f.ShellIds[i], out var w))
                (v[o + 2], v[o + 3], v[o + 4], v[o + 5]) = (w.LongBottom, w.LongTop, w.ShortBottom, w.ShortTop);
            else
                for (int c = 2; c < CrackQ; c++) v[o + c] = double.NaN;
        }
        return v;
    }
}

/// <summary>
/// Потоковая огибающая: векторы величин добавляются по одному (вариант пакета сразу после расчёта или чтения из БД),
/// хранятся только min/max и источник — память не зависит от числа вариантов.
/// </summary>
public sealed class FemCsfeaEnvelopeAccumulator
{
    double[]? _min, _max;
    int[]? _minSource, _maxSource;
    int _count;

    /// <summary>Добавить вектор; <paramref name="source"/> — номер источника (у пакета — индекс варианта).</summary>
    public void Add(double[] values, int source)
    {
        if (_min == null)
        {
            int len = values.Length;
            _min = new double[len];
            _max = new double[len];
            _minSource = new int[len];
            _maxSource = new int[len];
            Array.Fill(_min, double.NaN);
            Array.Fill(_max, double.NaN);
            Array.Fill(_minSource, -1);
            Array.Fill(_maxSource, -1);
        }
        if (values.Length != _min.Length) throw new ArgumentException("Вектор величин другой длины (другая сетка).", nameof(values));
        for (int i = 0; i < values.Length; i++)
        {
            double v = values[i];
            if (double.IsNaN(v)) continue;
            if (_minSource![i] < 0 || v < _min[i]) { _min[i] = v; _minSource[i] = source; }
            if (_maxSource![i] < 0 || v > _max![i]) { _max![i] = v; _maxSource[i] = source; }
        }
        _count++;
    }

    public int Count => _count;

    /// <summary>Огибающая добавленных; null — ничего не добавлено.</summary>
    public FemCsfeaEnvelope? Result => _min == null ? null : new FemCsfeaEnvelope
    {
        Min = _min, Max = _max!, MinSource = _minSource!, MaxSource = _maxSource!, Count = _count,
    };
}

/// <summary>
/// Запись огибающей группы пакета в <c>fem_result_steps</c> (хранение «только огибающие»): опорные поля без значений
/// (номера узлов и КЭ, длины массивов — для раскладки и показа) и min/max/источники, под GZip.
/// </summary>
public static class FemCsfeaEnvelopeCodec
{
    const int Magic = 0x31455343;   // «CSE1»

    public static byte[] Pack(FemCsfeaEnvelope envelope, RcSecantStepFields reference)
    {
        var skeleton = FemCsfeaStepFieldsCodec.Pack(Skeleton(reference));
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        using (var w = new BinaryWriter(gz))
        {
            w.Write(Magic);
            w.Write(skeleton.Length);
            w.Write(skeleton);
            w.Write(envelope.Count);
            w.Write(envelope.Min.Length);
            foreach (double v in envelope.Min) w.Write(v);
            foreach (double v in envelope.Max) w.Write(v);
            foreach (int v in envelope.MinSource) w.Write(v);
            foreach (int v in envelope.MaxSource) w.Write(v);
        }
        return ms.ToArray();
    }

    public static (FemCsfeaEnvelope Envelope, RcSecantStepFields Reference) Unpack(byte[] data)
    {
        using var gz = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var r = new BinaryReader(gz);
        if (r.ReadInt32() != Magic) throw new InvalidDataException("Не огибающая пакета CSfea.");
        var reference = FemCsfeaStepFieldsCodec.Unpack(r.ReadBytes(r.ReadInt32()));
        int count = r.ReadInt32();
        int len = r.ReadInt32();
        double[] Doubles() { var a = new double[len]; for (int i = 0; i < len; i++) a[i] = r.ReadDouble(); return a; }
        int[] Ints() { var a = new int[len]; for (int i = 0; i < len; i++) a[i] = r.ReadInt32(); return a; }
        var min = Doubles();
        var max = Doubles();
        var minSource = Ints();
        var maxSource = Ints();
        return (new FemCsfeaEnvelope { Min = min, Max = max, MinSource = minSource, MaxSource = maxSource, Count = count }, reference);
    }

    /// <summary>Номера и длины массивов опорных полей, значения — нули (состояния — NaN).</summary>
    static RcSecantStepFields Skeleton(RcSecantStepFields f)
    {
        var states = new double[f.ShellStates.Length];
        Array.Fill(states, double.NaN);
        return new RcSecantStepFields
        {
            Stage = -1, Step = 1, LoadFactor = 1,
            NodeIds = f.NodeIds, Displacements = new double[f.Displacements.Length],
            ShellIds = f.ShellIds, ShellForces = new double[f.ShellForces.Length], ShellStates = states,
            ShellFlags = new byte[f.ShellFlags.Length],
            BeamIds = f.BeamIds, BeamForces = new double[f.BeamForces.Length], BeamFlags = new byte[f.BeamFlags.Length],
            ReactionNodeIds = f.ReactionNodeIds, Reactions = new double[f.Reactions.Length], ReactionKinds = f.ReactionKinds,
        };
    }
}
