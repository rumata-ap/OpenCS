using System.Runtime.InteropServices;
using CScore.Import;
using CScore.Planar;

namespace OpenCS.Services.Scad;

/// <summary>Что читать из проекта SCAD помимо схемы (узлы, КЭ, жёсткости, группы, блоки).</summary>
internal sealed record ScadReadOptions(bool OutputAxes = true, bool ConcreteGroups = true, bool AssignedRebar = true,
    bool SteelGroups = true, bool AnalysisModel = true);

/// <summary>
/// Итог чтения схемы: данные и сведения для предупреждений (текст — в потоке UI по ресурсам).
/// </summary>
/// <param name="Data">Схема SCAD (координаты и размеры — в метрах).</param>
/// <param name="SkippedByType">Пропущенные КЭ: код типа SCAD → число.</param>
/// <param name="DeletedElements">Удалённые КЭ (ApiIsElemDeleted).</param>
/// <param name="DeletedNodes">Удалённые узлы.</param>
/// <param name="BarStiffnessesWithoutShape">Жёсткости стержней без «бруса» (STZ и т.п.), назначенные КЭ.</param>
/// <param name="DegenerateAxisElements">Оболочки с вырожденной осью выдачи (направление ⟂ плоскости КЭ).</param>
internal sealed record ScadApiReadResult(ScadSchemaData Data, IReadOnlyDictionary<int, int> SkippedByType,
    int DeletedElements, int DeletedNodes, IReadOnlyList<int> BarStiffnessesWithoutShape,
    int DegenerateAxisElements);

/// <summary>Сводка проекта для диалога импорта.</summary>
internal sealed record ScadProjectSummary(int Nodes, int Bars, int Shells, int SkippedElements, int Stiffnesses,
    int Groups, int Blocks, int ConcreteGroups, int AxisSystems, string LengthUnit, string SectionUnit,
    string ForceUnit, int? ResultLoads, bool HasForces, bool HasRsu);

/// <summary>Чтение схемы SCAD через открытую <see cref="ScadApiSession"/>. Только чтение, один поток.</summary>
internal static unsafe class ScadApiReader
{
    const int CancelStride = 4096;
    const int RigidBufferSize = 4096;

    /// <summary>Прочитать схему.</summary>
    public static ScadApiReadResult Read(ScadApiSession s, ScadReadOptions options,
        IProgress<double>? progress, CancellationToken ct)
    {
        var n = s.Native;
        nint h = s.Handle;
        var data = new ScadSchemaData();
        using var trace = ScadApiTrace.Step("Read");

        var (lengthUnit, sectionUnit, _) = Units(s);
        data.LengthUnitM = lengthUnit.ToMeters;
        data.SectionUnitM = sectionUnit.ToMeters;
        double lu = data.LengthUnitM;

        // Узлы.
        uint nodeCount = n.ApiGetQuantityNode(h);
        int deletedNodes = 0;
        var coords = new Dictionary<int, PlanarVector3>((int)nodeCount);
        for (uint i = 1; i <= nodeCount; i++)
        {
            if (i % CancelStride == 0) { ct.ThrowIfCancellationRequested(); progress?.Report(0.3 * i / nodeCount); }
            if (n.ApiIsNodeDeleted(h, i) != 0) { deletedNodes++; continue; }
            byte* p = n.ApiGetNode(h, i);
            if (p == null) continue;
            double x = *(double*)(p + ScadApiLayouts.NodeX) * lu;
            double y = *(double*)(p + ScadApiLayouts.NodeY) * lu;
            double z = *(double*)(p + ScadApiLayouts.NodeZ) * lu;
            data.Nodes.Add(new ScadNodeRecord((int)i, x, y, z));
            coords[(int)i] = new PlanarVector3(x, y, z);
        }

        // КЭ.
        uint elemCount = n.ApiGetElemQuantity(h);
        int deletedElements = 0;
        var skipped = new Dictionary<int, int>();
        var rigidBodies = new List<(int Elem, int Stiffness, int[] Nodes)>();
        var springs = new List<(int Elem, int Stiffness, int Node)>();
        for (uint i = 1; i <= elemCount; i++)
        {
            if (i % CancelStride == 0) { ct.ThrowIfCancellationRequested(); progress?.Report(0.3 + 0.6 * i / elemCount); }
            if (n.ApiIsElemDeleted(h, i) != 0) { deletedElements++; continue; }
            uint type, rigid, qn;
            uint* list;
            if (n.ApiElemGetData(h, i, &type, &rigid, &qn, &list) != 0 || list == null && qn > 0) continue;
            if (ScadElementKinds.Classify((int)type, (int)qn) == ScadElementKind.Skip)
            {
                if (type == ScadRigidBodyType && qn >= 2)
                {
                    var bodyNodes = new int[qn];
                    for (int k = 0; k < qn; k++) bodyNodes[k] = (int)list[k];
                    rigidBodies.Add(((int)i, (int)rigid, bodyNodes));
                    continue;
                }
                if (type == ScadSpringType && qn == 1)
                {
                    springs.Add(((int)i, (int)rigid, (int)list[0]));
                    continue;
                }
                skipped[(int)type] = skipped.GetValueOrDefault((int)type) + 1;
                continue;
            }
            var nodes = new int[qn];
            for (int k = 0; k < qn; k++) nodes[k] = (int)list[k];
            // Порядок узлов SCAD («1 2 4 3» по контуру) совпадает с хранением сетки OpenCS.
            data.Elements.Add(new ScadElementRecord((int)i, (int)type, (int)rigid, nodes));
        }
        ct.ThrowIfCancellationRequested();

        ScadApiTrace.Write($"Read: узлов {data.Nodes.Count}, КЭ {data.Elements.Count}");
        using (ScadApiTrace.Step("Жёсткости")) ReadStiffnesses(s, data);
        using (ScadApiTrace.Step("Группы")) ReadGroups(s, data);
        using (ScadApiTrace.Step("Блоки")) ReadBlocks(s, data);
        if (options.ConcreteGroups)
            using (ScadApiTrace.Step("ЖБ-группы")) ReadConcreteGroups(s, data);
        if (options.SteelGroups)
            using (ScadApiTrace.Step("Стальные группы")) ReadSteelGroups(s, data);
        if (options.AssignedRebar)
            using (ScadApiTrace.Step("Заданное армирование")) data.AssignedRebar = ReadAssignedRebar(s);
        if (options.AnalysisModel)
            using (ScadApiTrace.Step("Опоры, пружины, жёсткие тела, шарниры, нагрузки"))
                data.AnalysisModel = ReadAnalysisModel(s, data, rigidBodies, springs, skipped.GetValueOrDefault(ScadLinkType));
        int degenerate = 0;
        if (options.OutputAxes)
            using (ScadApiTrace.Step("Оси выдачи усилий")) degenerate = ReadOutputAxes(s, data, coords, lu);
        progress?.Report(1);

        var stiffById = data.Stiffnesses.ToDictionary(r => r.Id);
        var noShape = data.Elements
            .Where(e => ScadElementKinds.Classify(e.TypeCode, e.NodeIds.Length) == ScadElementKind.Beam)
            .Select(e => e.StiffnessId).Distinct()
            .Where(id => stiffById.TryGetValue(id, out var r) && r.Kind == ScadStiffnessKind.Bar && r.BarRect == null)
            .Order().ToList();

        return new ScadApiReadResult(data, skipped, deletedElements, deletedNodes, noShape, degenerate);
    }

    /// <summary>Код типа КЭ «абсолютно жёсткое тело» (пробник 03.10: 9 узлов, жёсткость «SPRING … Type 100»).</summary>
    const uint ScadRigidBodyType = 100;
    /// <summary>Код типа КЭ «связь конечной жёсткости» узел — земля (1 узел, «SPRING … Type 51»).</summary>
    const uint ScadSpringType = 51;
    /// <summary>Код типа КЭ «упругая связь двух узлов» (не переносится).</summary>
    const int ScadLinkType = 55;

    /// <summary>
    /// Расчётная модель сверх сетки: закрепления (ApiGetBound), жёсткие тела и пружины (КЭ 100 и 51, собраны при
    /// чтении КЭ), шарниры стержней (ApiGetJoint), нагрузки загружений (ApiGetForceNode/Elem/Area) в единицах проекта;
    /// счётчики того, что не переносится (КЭ 55, объединения перемещений, упругие шарниры, вставки, основание).
    /// </summary>
    static ScadAnalysisModel ReadAnalysisModel(ScadApiSession s, ScadSchemaData data,
        IReadOnlyList<(int Elem, int Stiffness, int[] Nodes)> rigidBodies,
        IReadOnlyList<(int Elem, int Stiffness, int Node)> springElements, int linkElements)
    {
        var n = s.Native;
        nint h = s.Handle;
        var bounds = new Dictionary<int, int>();
        foreach (var node in data.Nodes)
        {
            uint mask = n.ApiGetBound(h, (uint)node.Id);
            if ((mask & 0x3F) != 0) bounds[node.Id] = (int)(mask & 0x3F);
        }

        var stiffById = data.Stiffnesses.ToDictionary(r => r.Id);
        var bodies = rigidBodies.Select(b => new ScadRigidBody(b.Elem, b.Stiffness, b.Nodes[0], b.Nodes[1..],
            ScadAnalysisModel.RigidBodyMask(stiffById.GetValueOrDefault(b.Stiffness)?.Text))).ToList();

        var notTransferred = new Dictionary<string, int>();
        void Count(string kind, int value) { if (value > 0) notTransferred[kind] = notTransferred.GetValueOrDefault(kind) + value; }
        Count(ScadNotTransferredKinds.Fe55, linkElements);

        int schemaType = (int)n.ApiGetTypeSystem(h);
        var springs = new List<ScadSpring>(springElements.Count);
        foreach (var sp in springElements)
        {
            if (ScadAnalysisModel.SpringStiffness(stiffById.GetValueOrDefault(sp.Stiffness)?.Text, schemaType) is { } k)
                springs.Add(new ScadSpring(sp.Elem, sp.Stiffness, sp.Node, k));
            else Count(ScadNotTransferredKinds.SpringUnparsed, 1);
        }

        // Шарниры: по обоим концам стержней; упругие (ненулевые жёсткости) не переносятся.
        var joints = new List<ScadJoint>();
        Span<int> masks = stackalloc int[2];
        foreach (var e in data.Elements)
        {
            if (ScadElementKinds.Classify(e.TypeCode, e.NodeIds.Length) != ScadElementKind.Beam) continue;
            for (uint k = 1; k <= 2; k++)
            {
                byte place;
                double* value = null;
                int mask = (int)(n.ApiGetJoint(h, (uint)e.Id, k, &place, &value) & 0x3F);
                if (mask != 0 && value != null && new ReadOnlySpan<double>(value, 6).ContainsAnyExcept(0.0))
                {
                    Count(ScadNotTransferredKinds.ElasticJoint, 1);
                    mask = 0;
                }
                masks[(int)k - 1] = mask;
            }
            if ((masks[0] | masks[1]) != 0) joints.Add(new ScadJoint(e.Id, masks[0], masks[1]));
        }

        Count(ScadNotTransferredKinds.BoundUnite, (int)n.ApiGetQuantityBoundUnite(h));
        Count(ScadNotTransferredKinds.Insert, GroupElements(h, n.ApiGetQuantityInsert(h), n.ApiGetNumInsert));
        Count(ScadNotTransferredKinds.Bed, GroupElements(h, n.ApiGetQuantityBed(h), n.ApiGetBed));

        var loads = new List<ScadLoadCase>();
        uint loadCount = n.ApiGetQuantityLoad(h);
        for (uint l = 1; l <= loadCount; l++)
            loads.Add(new ScadLoadCase((int)l, ScadApiSession.Str(n.ApiGetLoadName(h, l)),
                ReadLoads(h, l, n.ApiGetQuantityForceNode(h, l), n.ApiGetForceNode),
                ReadLoads(h, l, n.ApiGetQuantityForceElem(h, l), n.ApiGetForceElem),
                ReadLoads(h, l, n.ApiGetQuantityForceArea(h, l), n.ApiGetForceArea)));

        var (_, _, force) = Units(s);
        return new ScadAnalysisModel
        {
            Bounds = bounds, RigidBodies = bodies, LoadCases = loads, LengthUnitM = data.LengthUnitM,
            ForceUnitN = force.Coef > 0 ? 9810.0 / force.Coef : 1,
            SchemaType = schemaType, Springs = springs, Joints = joints, NotTransferred = notTransferred,
            HasBoundaryV2 = true,
        };
    }

    /// <summary>Число КЭ во всех группах ApiGetNumInsert/ApiGetBed (BOOL: 0 — группы нет).</summary>
    static int GroupElements(nint h, uint groups,
        delegate* unmanaged[Stdcall]<nint, uint, byte*, uint*, double**, uint*, uint**, int> get)
    {
        int total = 0;
        for (uint g = 1; g <= groups; g++)
        {
            byte type;
            uint qs, qe;
            double* size;
            uint* list;
            if (get(h, g, &type, &qs, &size, &qe, &list) != 0) total += (int)qe;
        }
        return total;
    }

    static ScadLoadRecord[] ReadLoads(nint h, uint load, uint count,
        delegate* unmanaged[Stdcall]<nint, uint, uint, byte*, byte*, uint*, double**, uint*, uint**, ushort> get)
    {
        var records = new List<ScadLoadRecord>((int)count);
        for (uint pp = 1; pp <= count; pp++)
        {
            byte qw, qn;
            uint qd, ql;
            double* d;
            uint* list;
            if (get(h, load, pp, &qw, &qn, &qd, &d, &ql, &list) != 0) continue;
            var values = d == null ? [] : new ReadOnlySpan<double>(d, (int)qd).ToArray();
            var ids = new int[list == null ? 0 : ql];
            for (int k = 0; k < ids.Length; k++) ids[k] = (int)list[k];
            records.Add(new ScadLoadRecord(qw, qn, values, ids));
        }
        return [.. records];
    }

    /// <summary>Сводка проекта (без чтения координат); результаты — по рабочему каталогу.</summary>
    public static ScadProjectSummary ReadSummary(ScadApiSession s, string? workDirectory)
    {
        var n = s.Native;
        nint h = s.Handle;
        using var trace = ScadApiTrace.Step("ReadSummary");
        var (lengthUnit, sectionUnit, forceUnit) = Units(s);
        ScadApiTrace.Write($"Единицы: {lengthUnit.Name}, {sectionUnit.Name}, {forceUnit.Name}");

        uint nodeCount = n.ApiGetQuantityNode(h);
        ScadApiTrace.Write($"Узлов: {nodeCount}");
        int nodes = 0;
        for (uint i = 1; i <= nodeCount; i++)
            if (n.ApiIsNodeDeleted(h, i) == 0) nodes++;

        int bars = 0, shells = 0, other = 0;
        uint elemCount = n.ApiGetElemQuantity(h);
        ScadApiTrace.Write($"КЭ: {elemCount}");
        for (uint i = 1; i <= elemCount; i++)
        {
            if (n.ApiIsElemDeleted(h, i) != 0) continue;
            uint type, rigid, qn;
            uint* list;
            if (n.ApiElemGetData(h, i, &type, &rigid, &qn, &list) != 0) continue;
            switch (ScadElementKinds.Classify((int)type, (int)qn))
            {
                case ScadElementKind.Beam: bars++; break;
                case ScadElementKind.Shell: shells++; break;
                default: other++; break;
            }
        }

        int? loads = null;
        bool forces = false, rsu = false;
        ScadApiTrace.Write($"КЭ: стержней {bars}, пластин {shells}, прочих {other}");
        if (workDirectory != null && s.TryInitResult(workDirectory))
        {
            using (ScadApiTrace.Step("ApiGetResultQuantityLoad")) loads = (int)n.ApiGetResultQuantityLoad(h);
            using (ScadApiTrace.Step("ApiYesEffors")) forces = n.ApiYesEffors(h) != 0;
            using (ScadApiTrace.Step("ApiYesRSU")) rsu = n.ApiYesRSU(h) != 0;
        }
        ScadApiTrace.Write("Сводка: число жёсткостей, групп, блоков, ЖБ-групп, осей");

        return new ScadProjectSummary(nodes, bars, shells, other, (int)n.ApiGetQuantityRigid(h),
            (int)n.ApiGetQuantityGroupElem(h), (int)n.ApiGetQuantityBlock(h), (int)n.ApiGetQuantityConcrete(h),
            (int)n.ApiGetQuantitySystemCoordEffors(h), lengthUnit.Name, sectionUnit.Name, forceUnit.Name,
            loads, forces, rsu);
    }

    /// <summary>Единицы проекта (ApiGetUnits): длины, размеры сечений стержней, силы.</summary>
    static (ScadUnit Length, ScadUnit Section, ScadUnit Force) Units(ScadApiSession s)
    {
        byte* p = s.Native.ApiGetUnits(s.Handle);
        if (p == null) return (new("m", 1), new("m", 1), new("T", 1));
        var span = new ReadOnlySpan<byte>(p, ScadApiLayouts.UnitsSize * 3);
        return (ScadApiLayouts.ParseUnit(span),
                ScadApiLayouts.ParseUnit(span[ScadApiLayouts.UnitsSize..]),
                ScadApiLayouts.ParseUnit(span[(2 * ScadApiLayouts.UnitsSize)..]));
    }

    static void ReadStiffnesses(ScadApiSession s, ScadSchemaData data)
    {
        var n = s.Native;
        nint h = s.Handle;
        uint count = n.ApiGetQuantityRigid(h);
        var buffer = new byte[RigidBufferSize];
        fixed (byte* buf = buffer)
        {
            for (uint i = 1; i <= count; i++)
            {
                buffer.AsSpan().Clear();
                uint qe;
                uint* le;
                if (n.ApiGetRigid(h, i, buf, RigidBufferSize, &qe, &le) != 0) continue;
                string text = ScadApiLayouts.CString(buffer);
                string name = ScadApiSession.Str(n.ApiGetRigidName(h, i));
                data.Stiffnesses.Add(ScadStiffnessParams.Parse((int)i, text, name, data.LengthUnitM, data.SectionUnitM));
            }
        }
    }

    static void ReadGroups(ScadApiSession s, ScadSchemaData data)
    {
        var n = s.Native;
        nint h = s.Handle;
        uint count = n.ApiGetQuantityGroupElem(h);
        for (uint i = 1; i <= count; i++)
        {
            uint type, q;
            uint* list;
            byte* text;
            if (n.ApiGetGroupElem(h, i, &type, &q, &list, &text) != 0) continue;
            data.Groups.Add(new ScadGroupRecord(ScadApiSession.Str(text), Ids(list, q)));
        }
    }

    static void ReadBlocks(ScadApiSession s, ScadSchemaData data)
    {
        var n = s.Native;
        nint h = s.Handle;
        uint count = n.ApiGetQuantityBlock(h);
        for (uint i = 1; i <= count; i++)
        {
            uint q, color;
            uint* list;
            byte* text;
            if (n.ApiGetBlock(h, i, &q, &list, &text, &color) != 0) continue;
            data.Blocks.Add(new ScadGroupRecord(ScadApiSession.Str(text), Ids(list, q)));
        }
    }

    /// <summary>Только ЖБ-группы проекта (дочитывание к схеме, импортированной без них).</summary>
    public static List<ScadConcreteGroup> ReadConcreteGroups(ScadApiSession s)
    {
        var data = new ScadSchemaData();
        ReadConcreteGroups(s, data);
        return data.ConcreteGroups;
    }

    /// <summary>Только стальные группы проекта (дочитывание к уже импортированной схеме).</summary>
    public static List<ScadSteelGroup> ReadSteelGroups(ScadApiSession s)
    {
        var data = new ScadSchemaData { LengthUnitM = Units(s).Length.ToMeters };
        ReadSteelGroups(s, data);
        return data.SteelGroups;
    }

    static void ReadSteelGroups(ScadApiSession s, ScadSchemaData data)
    {
        var n = s.Native;
        nint h = s.Handle;
        uint count = n.ApiGetQuantitySteel(h);
        for (uint i = 1; i <= count; i++)
        {
            byte* p;
            uint q;
            uint* list;
            if (n.ApiGetSteel(h, i, &p, &q, &list) != 0 || p == null) continue;
            data.SteelGroups.Add(ScadApiLayouts.ParseSteel(new ReadOnlySpan<byte>(p, ScadApiLayouts.SteelSize), (int)i,
                ScadApiSession.Str(n.ApiGetNameSteel(h, i)), Ids(list, q), data.LengthUnitM));
        }
    }

    /// <summary>Сырые записи ApiArmElemPlate групп заданного армирования пластин — для сверки раскладки.</summary>
    internal static List<byte[]> ReadArmPlateRecords(ScadApiSession s)
    {
        var result = new List<byte[]>();
        uint count = s.Native.ApiGetQuantityArmElemPlate(s.Handle);
        for (uint i = 1; i <= count; i++)
        {
            byte* p;
            if (s.Native.ApiGetArmElemPlate(s.Handle, i, &p) != 0 || p == null) continue;
            result.Add(new ReadOnlySpan<byte>(p + ScadApiLayouts.ArmPlateElem, ScadApiLayouts.ArmElemPlateSize).ToArray());
        }
        return result;
    }

    /// <summary>Группы заданного армирования пластин и стержней (ApiGetArmElemPlate/ApiGetArmElemRod).</summary>
    public static ScadAssignedRebarFile ReadAssignedRebar(ScadApiSession s)
    {
        var n = s.Native;
        nint h = s.Handle;
        var plates = new List<ScadAssignedPlate>();
        uint plateCount = n.ApiGetQuantityArmElemPlate(h);
        for (uint i = 1; i <= plateCount; i++)
        {
            byte* p;
            if (n.ApiGetArmElemPlate(h, i, &p) != 0 || p == null) continue;
            var head = new ReadOnlySpan<byte>(p, ScadApiLayouts.ArmPlateElem + ScadApiLayouts.ArmElemPlateSize);
            plates.Add(ScadApiLayouts.ParseArmPlate(head[ScadApiLayouts.ArmPlateElem..], (int)i,
                ScadApiSession.Str(*(byte**)p), ArmIds(head, ScadApiLayouts.ArmPlateQuantity, ScadApiLayouts.ArmPlateList)));
        }

        var rods = new List<ScadAssignedRod>();
        uint rodCount = n.ApiGetQuantityArmElemRod(h);
        for (uint i = 1; i <= rodCount; i++)
        {
            byte* p;
            if (n.ApiGetArmElemRod(h, i, &p) != 0 || p == null) continue;
            var head = new ReadOnlySpan<byte>(p, ScadApiLayouts.ArmRodSize);
            int partCount = (int)*(uint*)(p + ScadApiLayouts.ArmRodParts);
            byte* partsPtr = *(byte**)(p + ScadApiLayouts.ArmRodPartsPtr);
            var parts = new ScadAssignedRodPart[partsPtr == null ? 0 : partCount];
            for (int k = 0; k < parts.Length; k++)
                parts[k] = ScadApiLayouts.ParseArmRodPart(
                    new ReadOnlySpan<byte>(partsPtr + (long)k * ScadApiLayouts.ArmElemRodSize, ScadApiLayouts.ArmElemRodSize));
            rods.Add(new ScadAssignedRod((int)i, ScadApiSession.Str(*(byte**)p),
                ArmIds(head, ScadApiLayouts.ArmRodQuantity, ScadApiLayouts.ArmRodList), parts));
        }
        return new ScadAssignedRebarFile(plates, rods);
    }

    static int[] ArmIds(ReadOnlySpan<byte> head, int quantityOffset, int listOffset)
    {
        uint q = MemoryMarshal.Read<uint>(head[quantityOffset..]);
        var list = (uint*)MemoryMarshal.Read<nint>(head[listOffset..]);
        return Ids(list, q);
    }

    static void ReadConcreteGroups(ScadApiSession s, ScadSchemaData data)
    {
        var n = s.Native;
        nint h = s.Handle;
        uint count = n.ApiGetQuantityConcrete(h);
        for (uint i = 1; i <= count; i++)
        {
            byte* p;
            uint q;
            uint* list;
            if (n.ApiGetConcrete(h, i, &p, &q, &list) != 0 || p == null) continue;
            var c = ScadApiLayouts.ParseConcrete(new ReadOnlySpan<byte>(p, ScadApiLayouts.ConcreteSize));
            string name = ScadApiSession.Str(n.ApiGetNameConcrete(h, i));
            data.ConcreteGroups.Add(new ScadConcreteGroup((int)i, name, c.Module, c.RangeM, c.ConcreteClass,
                c.LongitudinalRebarClass, c.TransverseRebarClass, c.CrackResisting, c.CrackWidthMm, Ids(list, q)));
        }
    }

    /// <summary>
    /// Углы осей выдачи оболочек по системам SCAD; оболочкам вне систем — 0 (X1 = узел 1 → узел 2).
    /// Возвращает число КЭ с вырожденной осью (угол неизвестен).
    /// </summary>
    static int ReadOutputAxes(ScadApiSession s, ScadSchemaData data, Dictionary<int, PlanarVector3> coords, double lu)
    {
        var n = s.Native;
        nint h = s.Handle;
        var shells = data.Elements
            .Where(e => ScadElementKinds.Classify(e.TypeCode, e.NodeIds.Length) == ScadElementKind.Shell)
            .ToDictionary(e => e.Id);
        var degenerate = new HashSet<int>();
        var points = new List<PlanarVector3>(4);
        Span<double> values = stackalloc double[6];

        uint count = n.ApiGetQuantitySystemCoordEffors(h);
        for (uint g = 1; g <= count; g++)
        {
            byte type;
            uint qs, ql;
            double* size;
            uint* list;
            if (n.ApiGetSystemCoordEffors(h, g, &type, &qs, &size, &ql, &list) == 0 || size == null) continue;
            int len = (int)Math.Min(qs, 6u);
            for (int k = 0; k < len; k++) values[k] = size[k];
            // Точки 17/18/20/21 — в единицах длины проекта, как и узлы.
            if (type is ScadOutputAxes.X1PointFromNode1 or ScadOutputAxes.X1PointFromCenter
                or ScadOutputAxes.Y1PointFromNode1 or ScadOutputAxes.Y1PointFromCenter)
                for (int k = 0; k < Math.Min(len, 3); k++) values[k] *= lu;

            for (uint k = 0; k < ql; k++)
            {
                int id = (int)list[k];
                if (!shells.TryGetValue(id, out var e)) continue;
                points.Clear();
                foreach (int node in e.NodeIds)
                    if (coords.TryGetValue(node, out var pt)) points.Add(pt);
                if (points.Count != e.NodeIds.Length) continue;
                if (ScadOutputAxes.AngleDeg(type, values[..len], points) is double angle)
                {
                    data.PlateAxisAngles[id] = angle;
                    degenerate.Remove(id);
                }
                else
                {
                    data.PlateAxisAngles.Remove(id);
                    degenerate.Add(id);
                }
            }
        }

        foreach (int id in shells.Keys)
            if (!degenerate.Contains(id)) data.PlateAxisAngles.TryAdd(id, 0);
        return degenerate.Count;
    }

    static int[] Ids(uint* list, uint count)
    {
        if (list == null || count == 0) return [];
        var ids = new int[count];
        for (int i = 0; i < count; i++) ids[i] = (int)list[i];
        return ids;
    }
}
