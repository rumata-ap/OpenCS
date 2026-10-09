using System.IO;
using System.IO.Compression;
using CSfea.CScoreBridge.Structural;

namespace OpenCS.Services;

/// <summary>
/// Сжатая запись полей шага секущего расчёта (<see cref="RcSecantStepFields"/>) для таблицы <c>fem_result_steps</c>:
/// двоичный поток (версия, шаг, узлы, пластины, стержни) под GZip. Перемещения и усилия — double, состояния пластин —
/// float.
/// </summary>
public static class FemCsfeaStepFieldsCodec
{
    const int Magic = 0x31465343;   // «CSF1»
    const int Version = 1;

    public static byte[] Pack(RcSecantStepFields f)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        using (var w = new BinaryWriter(gz))
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(f.Stage);
            w.Write(f.Step);
            w.Write(f.LoadFactor);
            w.Write(f.IsRefinement);
            Ints(w, f.NodeIds);
            Doubles(w, f.Displacements);
            Ints(w, f.ShellIds);
            Doubles(w, f.ShellForces);
            w.Write(f.ShellStates.Length);
            foreach (double v in f.ShellStates) w.Write((float)v);
            Bytes(w, f.ShellFlags);
            Ints(w, f.BeamIds);
            Doubles(w, f.BeamForces);
            Bytes(w, f.BeamFlags);
        }
        return ms.ToArray();
    }

    /// <summary>Разбор записи; неизвестный формат — <see cref="InvalidDataException"/>.</summary>
    public static RcSecantStepFields Unpack(byte[] data)
    {
        using var gz = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var r = new BinaryReader(gz);
        if (r.ReadInt32() != Magic) throw new InvalidDataException("Не поля шага CSfea.");
        int version = r.ReadInt32();
        if (version != Version) throw new InvalidDataException($"Неизвестная версия полей шага CSfea: {version}.");
        int stage = r.ReadInt32(), step = r.ReadInt32();
        double lambda = r.ReadDouble();
        bool refinement = r.ReadBoolean();
        var nodeIds = Ints(r);
        var disp = Doubles(r);
        var shellIds = Ints(r);
        var shellForces = Doubles(r);
        var shellStates = new double[r.ReadInt32()];
        for (int i = 0; i < shellStates.Length; i++) shellStates[i] = r.ReadSingle();
        var shellFlags = Bytes(r);
        var beamIds = Ints(r);
        var beamForces = Doubles(r);
        var beamFlags = Bytes(r);
        return new RcSecantStepFields
        {
            Stage = stage, Step = step, LoadFactor = lambda, IsRefinement = refinement,
            NodeIds = nodeIds, Displacements = disp,
            ShellIds = shellIds, ShellForces = shellForces, ShellStates = shellStates, ShellFlags = shellFlags,
            BeamIds = beamIds, BeamForces = beamForces, BeamFlags = beamFlags,
        };
    }

    static void Ints(BinaryWriter w, int[] a) { w.Write(a.Length); foreach (int v in a) w.Write(v); }
    static void Doubles(BinaryWriter w, double[] a) { w.Write(a.Length); foreach (double v in a) w.Write(v); }
    static void Bytes(BinaryWriter w, byte[] a) { w.Write(a.Length); w.Write(a); }

    static int[] Ints(BinaryReader r)
    {
        var a = new int[r.ReadInt32()];
        for (int i = 0; i < a.Length; i++) a[i] = r.ReadInt32();
        return a;
    }

    static double[] Doubles(BinaryReader r)
    {
        var a = new double[r.ReadInt32()];
        for (int i = 0; i < a.Length; i++) a[i] = r.ReadDouble();
        return a;
    }

    static byte[] Bytes(BinaryReader r) => r.ReadBytes(r.ReadInt32());
}
