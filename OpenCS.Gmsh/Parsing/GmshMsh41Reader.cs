using System.Globalization;
using CScore.Fem;

namespace OpenCS.Gmsh.Parsing;

/// <summary>Строго читает ASCII MSH 4.1 без зависимости от Gmsh SDK.</summary>
public static class GmshMsh41Reader
{
    static readonly IReadOnlyDictionary<int, int> LinearNodeCounts = new Dictionary<int, int>
    {
        [15] = 1,
        [1] = 2,
        [2] = 3,
        [3] = 4
    };

    public static GmshMsh41Document Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var index = 0;
        var hasMeshFormat = false;
        var hasNodes = false;
        var hasElements = false;
        var physicalNames = new Dictionary<(int Dimension, int Tag), string>();
        var entities = new Dictionary<(int Dimension, long Tag), EntityInfo>();
        var nodes = new List<GmshMsh41Node>();
        var elements = new List<GmshMsh41Element>();
        var diagnostics = new List<FemValidationDiagnostic>();
        var nodeIds = new HashSet<long>();
        var elementIds = new HashSet<long>();

        while (NextNonEmpty(lines, ref index, out var section))
        {
            switch (section)
            {
                case "$MeshFormat":
                    ReadMeshFormat(lines, ref index);
                    hasMeshFormat = true;
                    break;
                case "$PhysicalNames":
                    ReadPhysicalNames(lines, ref index, physicalNames);
                    break;
                case "$Entities":
                    ReadEntities(lines, ref index, entities);
                    break;
                case "$Nodes":
                    ReadNodes(lines, ref index, nodes, nodeIds);
                    hasNodes = true;
                    break;
                case "$Elements":
                    ReadElements(lines, ref index, entities, physicalNames, elements, elementIds, diagnostics);
                    hasElements = true;
                    break;
                default:
                    SkipSection(lines, ref index, section);
                    break;
            }
        }

        if (!hasMeshFormat) throw new InvalidDataException("MSH 4.1 не содержит $MeshFormat.");
        if (!hasNodes) throw new InvalidDataException("MSH 4.1 не содержит $Nodes.");
        if (!hasElements) throw new InvalidDataException("MSH 4.1 не содержит $Elements.");
        if (elements.Count == 0)
            diagnostics.Add(new("gmsh_mesh_empty", "MSH 4.1 не содержит элементов."));
        return new GmshMsh41Document { Nodes = nodes, Elements = elements, Diagnostics = diagnostics };
    }

    static void ReadMeshFormat(string[] lines, ref int index)
    {
        var values = Split(NextRequired(lines, ref index, "$MeshFormat data"));
        if (values.Length < 3 || values[0] != "4.1")
            throw new InvalidDataException("Поддерживается только MSH 4.1.");
        if (ParseInt(values[1]) != 0)
            throw new InvalidDataException("Поддерживается только ASCII MSH.");
        ExpectEnd(lines, ref index, "$EndMeshFormat");
    }

    static void ReadPhysicalNames(string[] lines, ref int index, IDictionary<(int Dimension, int Tag), string> names)
    {
        var count = ParseInt(NextRequired(lines, ref index, "$PhysicalNames count"));
        for (var i = 0; i < count; i++)
        {
            var line = NextRequired(lines, ref index, "$PhysicalNames row");
            var values = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (values.Length < 3) throw new InvalidDataException("Некорректная строка $PhysicalNames.");
            var name = values[2].Trim();
            if (name.Length >= 2 && name[0] == '"' && name[^1] == '"') name = name[1..^1];
            names.Add((ParseInt(values[0]), ParseInt(values[1])), name);
        }
        ExpectEnd(lines, ref index, "$EndPhysicalNames");
    }

    static void ReadEntities(string[] lines, ref int index, IDictionary<(int Dimension, long Tag), EntityInfo> entities)
    {
        var counts = Split(NextRequired(lines, ref index, "$Entities count"));
        if (counts.Length != 4) throw new InvalidDataException("Некорректный заголовок $Entities.");
        ReadEntityBlock(lines, ref index, 0, ParseInt(counts[0]), entities);
        ReadEntityBlock(lines, ref index, 1, ParseInt(counts[1]), entities);
        ReadEntityBlock(lines, ref index, 2, ParseInt(counts[2]), entities);
        ReadEntityBlock(lines, ref index, 3, ParseInt(counts[3]), entities);
        ExpectEnd(lines, ref index, "$EndEntities");
    }

    static void ReadEntityBlock(
        string[] lines,
        ref int index,
        int dimension,
        int count,
        IDictionary<(int Dimension, long Tag), EntityInfo> entities)
    {
        for (var i = 0; i < count; i++)
        {
            var values = Split(NextRequired(lines, ref index, "$Entities row"));
            var physicalCountIndex = dimension == 0 ? 4 : 7;
            if (values.Length <= physicalCountIndex) throw new InvalidDataException("Некорректная строка $Entities.");
            var physicalCount = ParseInt(values[physicalCountIndex]);
            if (physicalCount < 0 || values.Length < physicalCountIndex + 1 + physicalCount)
                throw new InvalidDataException("Некорректный список physical groups в $Entities.");
            var physicalGroups = values.Skip(physicalCountIndex + 1).Take(physicalCount).Select(ParseInt).ToArray();
            entities.Add((dimension, ParseLong(values[0])), new EntityInfo(physicalGroups));
        }
    }

    static void ReadNodes(string[] lines, ref int index, ICollection<GmshMsh41Node> nodes, ISet<long> nodeIds)
    {
        var header = Split(NextRequired(lines, ref index, "$Nodes header"));
        if (header.Length != 4) throw new InvalidDataException("Некорректный заголовок $Nodes.");
        var blockCount = ParseInt(header[0]);
        for (var block = 0; block < blockCount; block++)
        {
            var blockHeader = Split(NextRequired(lines, ref index, "$Nodes block header"));
            if (blockHeader.Length != 4) throw new InvalidDataException("Некорректный заголовок node block.");
            var parametric = ParseInt(blockHeader[2]);
            var blockNodeCount = ParseInt(blockHeader[3]);
            var rawIds = new long[blockNodeCount];
            for (var node = 0; node < blockNodeCount; node++) rawIds[node] = ParseLong(NextRequired(lines, ref index, "$Nodes tag"));
            for (var node = 0; node < blockNodeCount; node++)
            {
                var values = Split(NextRequired(lines, ref index, "$Nodes coordinates"));
                if (values.Length < 3 + parametric) throw new InvalidDataException("Некорректные координаты узла MSH.");
                if (!nodeIds.Add(rawIds[node])) throw new InvalidDataException($"Дублирующийся node tag {rawIds[node]} в MSH.");
                nodes.Add(new(rawIds[node], ParseDouble(values[0]), ParseDouble(values[1]), ParseDouble(values[2])));
            }
        }
        ExpectEnd(lines, ref index, "$EndNodes");
    }

    static void ReadElements(
        string[] lines,
        ref int index,
        IReadOnlyDictionary<(int Dimension, long Tag), EntityInfo> entities,
        IReadOnlyDictionary<(int Dimension, int Tag), string> physicalNames,
        ICollection<GmshMsh41Element> elements,
        ISet<long> elementIds,
        ICollection<FemValidationDiagnostic> diagnostics)
    {
        var header = Split(NextRequired(lines, ref index, "$Elements header"));
        if (header.Length != 4) throw new InvalidDataException("Некорректный заголовок $Elements.");
        var blockCount = ParseInt(header[0]);
        for (var block = 0; block < blockCount; block++)
        {
            var blockHeader = Split(NextRequired(lines, ref index, "$Elements block header"));
            if (blockHeader.Length != 4) throw new InvalidDataException("Некорректный заголовок element block.");
            var dimension = ParseInt(blockHeader[0]);
            var entityTag = ParseLong(blockHeader[1]);
            var elementType = ParseInt(blockHeader[2]);
            var elementCount = ParseInt(blockHeader[3]);
            for (var element = 0; element < elementCount; element++)
            {
                var values = Split(NextRequired(lines, ref index, "$Elements row")).Select(ParseLong).ToArray();
                if (values.Length < 2) throw new InvalidDataException("Некорректная строка элемента MSH.");
                var rawId = values[0];
                if (!elementIds.Add(rawId)) throw new InvalidDataException($"Дублирующийся element tag {rawId} в MSH.");
                var nodeIds = values[1..];
                var physicalGroup = 0;
                var physicalName = "";
                if (entities.TryGetValue((dimension, entityTag), out var entity) && entity.PhysicalGroups.Length > 0)
                {
                    physicalGroup = entity.PhysicalGroups[0];
                    physicalNames.TryGetValue((dimension, physicalGroup), out physicalName!);
                    physicalName ??= "";
                }

                if (!LinearNodeCounts.TryGetValue(elementType, out var expectedNodes))
                {
                    diagnostics.Add(new("gmsh_unsupported_element", $"Элемент MSH типа {elementType} не поддерживается для PlanarRegion."));
                }
                else if (nodeIds.Length != expectedNodes)
                {
                    throw new InvalidDataException($"Элемент MSH типа {elementType} имеет неверную связность.");
                }
                elements.Add(new(rawId, dimension, entityTag, elementType, nodeIds, physicalGroup, physicalName));
            }
        }
        ExpectEnd(lines, ref index, "$EndElements");
    }

    static bool NextNonEmpty(string[] lines, ref int index, out string value)
    {
        while (index < lines.Length)
        {
            var line = lines[index++].Trim();
            if (line.Length == 0) continue;
            value = line;
            return true;
        }
        value = "";
        return false;
    }

    static string NextRequired(string[] lines, ref int index, string context)
    {
        if (!NextNonEmpty(lines, ref index, out var value)) throw new InvalidDataException($"Неожиданный конец MSH при чтении {context}.");
        return value;
    }

    static void ExpectEnd(string[] lines, ref int index, string expected)
    {
        if (NextRequired(lines, ref index, expected) != expected)
            throw new InvalidDataException($"Ожидалась секция {expected}.");
    }

    static void SkipSection(string[] lines, ref int index, string section)
    {
        var end = "$End" + section[1..];
        while (NextNonEmpty(lines, ref index, out var value))
            if (value == end) return;
        throw new InvalidDataException($"Не закрыта неизвестная секция {section}.");
    }

    static string[] Split(string line) => line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    static int ParseInt(string value) => int.Parse(value, CultureInfo.InvariantCulture);
    static long ParseLong(string value) => long.Parse(value, CultureInfo.InvariantCulture);
    static double ParseDouble(string value) => double.Parse(value, CultureInfo.InvariantCulture);

    sealed record EntityInfo(int[] PhysicalGroups);
}
