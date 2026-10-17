using System.IO;
using System.IO.Compression;
using CSfea.CScoreBridge.Structural;

namespace OpenCS.Services;

/// <summary>
/// Сжатая запись полей шага секущего расчёта (<see cref="RcSecantStepFields"/>) для таблицы <c>fem_result_steps</c>:
/// двоичный поток (версия, шаг, узлы, пластины, стержни, с версии 2 — реакции опор и ΣF шага, с версии 3 — флаги и
/// блоки жёсткостей, деформаций сечений и полных секущих матриц) под GZip. Перемещения, усилия и реакции — double,
/// состояния пластин и блоки версии 3 — float. Записи версии 1 читаются без реакций, версий 1–2 — без блоков.
/// </summary>
public static class FemCsfeaStepFieldsCodec
{
    const int Magic = 0x31465343;   // «CSF1»
    const int Version = 3;
    const byte HasStiffness = 1, HasStrains = 2, HasMatrices = 4;

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
            Ints(w, f.ReactionNodeIds);
            Doubles(w, f.Reactions);
            Bytes(w, f.ReactionKinds);
            Doubles(w, f.ReactionSums);
            Doubles(w, f.LoadSum);
            w.Write((byte)((f.HasStiffness ? HasStiffness : 0) | (f.HasStrains ? HasStrains : 0) | (f.HasMatrices ? HasMatrices : 0)));
            if (f.HasStiffness) { Floats(w, f.ShellStiffness); Floats(w, f.BeamStiffness); }
            if (f.HasStrains) { Floats(w, f.ShellStrains); Floats(w, f.BeamStrains); }
            if (f.HasMatrices) { Floats(w, f.ShellMatrices); Floats(w, f.BeamMatrices); }
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
        if (version is < 1 or > Version) throw new InvalidDataException($"Неизвестная версия полей шага CSfea: {version}.");
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
        int[] reactionNodes = [];
        double[] reactions = [], reactionSums = [], loadSum = [];
        byte[] reactionKinds = [];
        if (version >= 2)
        {
            reactionNodes = Ints(r);
            reactions = Doubles(r);
            reactionKinds = Bytes(r);
            reactionSums = Doubles(r);
            loadSum = Doubles(r);
        }
        byte blocks = version >= 3 ? r.ReadByte() : (byte)0;
        double[] shellK = [], beamK = [], shellE = [], beamE = [], shellM = [], beamM = [];
        if ((blocks & HasStiffness) != 0) { shellK = Floats(r); beamK = Floats(r); }
        if ((blocks & HasStrains) != 0) { shellE = Floats(r); beamE = Floats(r); }
        if ((blocks & HasMatrices) != 0) { shellM = Floats(r); beamM = Floats(r); }
        return new RcSecantStepFields
        {
            Stage = stage, Step = step, LoadFactor = lambda, IsRefinement = refinement,
            NodeIds = nodeIds, Displacements = disp,
            ShellIds = shellIds, ShellForces = shellForces, ShellStates = shellStates, ShellFlags = shellFlags,
            BeamIds = beamIds, BeamForces = beamForces, BeamFlags = beamFlags,
            ReactionNodeIds = reactionNodes, Reactions = reactions, ReactionKinds = reactionKinds,
            ReactionSums = reactionSums, LoadSum = loadSum,
            HasStiffness = (blocks & HasStiffness) != 0, ShellStiffness = shellK, BeamStiffness = beamK,
            HasStrains = (blocks & HasStrains) != 0, ShellStrains = shellE, BeamStrains = beamE,
            HasMatrices = (blocks & HasMatrices) != 0, ShellMatrices = shellM, BeamMatrices = beamM,
        };
    }

    static void Floats(BinaryWriter w, double[] a) { w.Write(a.Length); foreach (double v in a) w.Write((float)v); }

    static double[] Floats(BinaryReader r)
    {
        var a = new double[r.ReadInt32()];
        for (int i = 0; i < a.Length; i++) a[i] = r.ReadSingle();
        return a;
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
