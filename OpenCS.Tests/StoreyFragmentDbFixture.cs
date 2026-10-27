using System.Globalization;
using CScore;
using CScore.Fem;
using CScore.Planar;
using OpenCS.Utilites;

namespace OpenCS.Tests;

/// <summary>
/// Схема-родитель фрагмента в БД: колонна C1 (узлы 1–2, z 0…6, сетка 501–504 по 1,5 м), плита S1 6×6 на z = 3 (сетка
/// 4×4, КЭ 1–16, узлы 1001…), плита S2 6×6 на z = 9 (сетка 2×2, КЭ 101–104, узлы 2001…). Загружения «Пост.» и
/// «Полезная», сочетание с ролями слагаемых; нагрузки: uniform на КЭ S1, точечная на КЭ S2, на узлы сетки 1007 (S1) и
/// 2001 (S2), нагрузка КонЭ C1.
/// </summary>
static class StoreyFragmentDbFixture
{
    static string S(int v) => v.ToString(CultureInfo.InvariantCulture);

    public static FemSchema CreateParent(DatabaseService db)
    {
        var schema = new FemSchema { Tag = "Каркас" };
        db.SaveFemSchema(schema);
        var r1 = Slab("S1", 3);
        var r2 = Slab("S2", 9);
        db.AddPlanarRegion(r1, schema.Id);
        db.AddPlanarRegion(r2, schema.Id);

        var nodes = new List<FemNode>
        {
            new() { Id = -1, NodeTag = "1", X = 0, Y = 0, Z = 0, DofMask = 63 },
            new() { Id = -2, NodeTag = "2", X = 0, Y = 0, Z = 6 },
        };
        var members = new List<FemMember>
        {
            new() { Id = -1, ElemTag = "C1", ElemType = "beam", NodeIdsJson = "[1,2]", SectionTag = "1" },
            new() { Id = -2, ElemTag = "S1", ElemType = "shell", NodeIdsJson = "[]", PlanarRegionId = r1.Id, Kind = "plate", ThicknessM = 0.2 },
            new() { Id = -3, ElemTag = "S2", ElemType = "shell", NodeIdsJson = "[]", PlanarRegionId = r2.Id, Kind = "plate", ThicknessM = 0.2 },
        };
        var loadCases = new List<FemLoadCase>
        {
            new() { Id = -1, Tag = "Пост.", Sp20Type = "permanent", SelfWeightFactor = 1.1 },
            new() { Id = -2, Tag = "Полезная", LongTermShare = 0.35 },
        };
        var memberLoads = new List<FemMemberLoad>
        {
            new() { LoadCaseId = -1, MemberId = -1, CoordinateSystem = "global", QxStart = 100, QxEnd = 100 },
        };
        var elementLoads = new List<FemElementLoad>
        {
            ElementLoad(-2, FemElementLoadKinds.Uniform, [-5000], Enumerable.Range(1, 16).Select(S).ToArray()),
            ElementLoad(-2, FemElementLoadKinds.Point, [-1000, 0.5, 0.5], "101"),
        };
        var meshNodeLoads = new List<FemMeshNodeLoad>
        {
            new() { LoadCaseId = -2, MeshNodeTag = "1007", Fz = -300 },
            new() { LoadCaseId = -2, MeshNodeTag = "2001", Fz = -700 },
        };
        db.SaveFemSchemaEdit(schema.Id, nodes, members, [], loadCases, [], memberLoads, [], [],
            elementLoads, meshNodeLoads);

        var definition = new FemLoadDefinition { SchemaId = schema.Id, Tag = "Сочетание 1", SourceKind = "manual" };
        definition.SetExpression(new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sum,
            Terms =
            [
                new FemLoadTerm { LoadCaseId = loadCases[0].Id, Coefficient = 1.1, Role = FemLoadTermRoles.Permanent, Mode = FemLoadTerm.AutoMode },
                new FemLoadTerm { LoadCaseId = loadCases[1].Id, Coefficient = 1.2, Role = FemLoadTermRoles.Leading, Mode = FemLoadTerm.ManualMode },
            ],
        });
        db.SaveFemLoadDefinition(definition);

        var meshNodes = new List<FemMeshNode>();
        var meshElements = new List<FemElement>();
        AddSlabMesh("S1", 3, 4, 1001, 1, meshNodes, meshElements);
        AddSlabMesh("S2", 9, 2, 2001, 101, meshNodes, meshElements);
        double[] zs = [0, 1.5, 3, 4.5, 6];
        for (int k = 0; k < zs.Length; k++)
            meshNodes.Add(new FemMeshNode { NodeTag = S(101 + k), X = 0, Y = 0, Z = zs[k] });
        for (int k = 0; k < zs.Length - 1; k++)
            meshElements.Add(new FemElement
            {
                ElemTag = S(501 + k), ElemType = "beam", SourceMemberTag = "C1", SectionTag = "1", StiffnessNum = 3,
                NodeIdsJson = $"[{101 + k},{102 + k}]",
            });
        db.SaveFemMeshSnapshot(schema.Id, meshNodes, meshElements);
        return schema;
    }

    static PlanarRegion Slab(string tag, double z)
    {
        var frame = new Frame3D(new PlanarVector3(0, 0, z), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1));
        var contour = new Contour([-3.0, 3, 3, -3, -3], [-3.0, -3, 3, 3, -3], tag);
        var region = PlanarRegion.CreateFromContour(contour, [], frame, tag);
        region.MeshMaxElementSizeM = 1.5;
        return region;
    }

    static void AddSlabMesh(string member, double z, int n, int firstNode, int firstElem, List<FemMeshNode> nodes, List<FemElement> elements)
    {
        double h = 6.0 / n;
        for (int j = 0; j <= n; j++)
            for (int i = 0; i <= n; i++)
                nodes.Add(new FemMeshNode { NodeTag = S(firstNode + j * (n + 1) + i), X = -3 + i * h, Y = -3 + j * h, Z = z });
        int Tag(int i, int j) => firstNode + j * (n + 1) + i;
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
                elements.Add(new FemElement
                {
                    ElemTag = S(firstElem + j * n + i), ElemType = "shell", SourceMemberTag = member, ThicknessM = 0.2,
                    NodeIdsJson = $"[{Tag(i, j)},{Tag(i + 1, j)},{Tag(i, j + 1)},{Tag(i + 1, j + 1)}]",
                });
    }

    static FemElementLoad ElementLoad(int lc, string kind, double[] values, params string[] elements)
    {
        var load = new FemElementLoad { LoadCaseId = lc, LoadKind = kind, Axis = "z", CoordinateSystem = "global" };
        load.SetTargetTags(elements);
        load.SetValues(values);
        return load;
    }

    /// <summary>Снимок родителя целиком (без фильтра) — эталон для сравнения с отбором полосы.</summary>
    public static CScore.Fem.Fragment.StoreyFragmentParentSnapshot ReadFull(DatabaseService db, int schemaId) => new()
    {
        Nodes = db.GetFemNodes(schemaId),
        Members = db.GetFemMembers(schemaId),
        Regions = db.GetPlanarRegions(schemaId),
        MeshNodes = db.GetFemMeshNodes(schemaId),
        MeshElements = db.GetFemMeshElements(schemaId),
        RigidBodies = db.GetFemRigidBodies(schemaId),
        LoadCases = db.GetFemLoadCases(schemaId),
        LoadDefinitions = db.GetFemLoadDefinitions(schemaId),
        MemberLoads = db.GetFemMemberLoads(schemaId),
        ElementLoads = db.GetFemElementLoads(schemaId),
        MeshNodeLoads = db.GetFemMeshNodeLoads(schemaId),
        NodeLoads = db.GetFemNodeLoads(schemaId),
        KinematicLoads = db.GetFemKinematicLoads(schemaId),
    };
}
