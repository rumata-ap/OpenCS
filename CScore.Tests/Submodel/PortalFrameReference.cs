using System.Text.Json;
using CScore.Fem;
using CScore.Planar;
using CScore.Submodel;
using Xunit;

namespace CScore.Tests.Submodel;

/// <summary>
/// Эталон «колонна–ригель–колонна» для граничного сценария. Файл подключается ссылкой и в
/// OpenCS.OpenSees.Tests: там линейный расчёт этой рамы выполняется живым OpenSees и записывается в
/// фикстуру <see cref="FixtureRelativePath"/>, здесь — сценарий строится по записанной фикстуре.
/// Рама в плоскости XZ: колонны высотой 3 м с заделкой внизу, ригель 6 м из трёх mesh-элементов,
/// равномерная q = −10 кН/м на ригеле (глобальная Z) и горизонтальная сила 5 кН в левом узле рамы.
/// </summary>
public static class PortalFrameReference
{
    public const string FixtureRelativePath = "Submodel/Fixtures/portal-frame-linear.json";
    public const double Q = -10_000;
    public const double H = 5_000;
    public const int SectionId = 5;
    public const double E = 3e10, Area = 0.15, Inertia = 0.003125;

    /// <summary>Mesh-узлы 1..6: 1 (0,0,0), 2 (0,0,3), 3 (2,0,3), 4 (4,0,3), 5 (6,0,3), 6 (6,0,0);
    /// элементы 11 (1→2, колонна C1), 12..14 (ригель B), 15 (6→5, колонна C2).</summary>
    public static ParentModel Model()
    {
        var nodes = new List<FemNode>
        {
            new() { Id = 1, NodeTag = "1", X = 0, Z = 0, DofMask = 63 },
            new() { Id = 2, NodeTag = "2", X = 0, Z = 3 },
            new() { Id = 3, NodeTag = "3", X = 6, Z = 3 },
            new() { Id = 4, NodeTag = "4", X = 6, Z = 0, DofMask = 63 },
        };
        var members = new List<FemMember>
        {
            Member(1, "C1", "[1,2]"), Member(2, "B", "[2,3]"), Member(3, "C2", "[4,3]"),
        };
        var meshNodes = new List<FemMeshNode>
        {
            MeshNode(1, 0, 0, "1", "C1"), MeshNode(2, 0, 3, "2", "B"), MeshNode(3, 2, 3, null, "B"),
            MeshNode(4, 4, 3, null, "B"), MeshNode(5, 6, 3, "3", "B"), MeshNode(6, 6, 0, "4", "C2"),
        };
        var elements = new List<FemElement>
        {
            Element(11, 1, 2, "C1"), Element(12, 2, 3, "B"), Element(13, 3, 4, "B"),
            Element(14, 4, 5, "B"), Element(15, 6, 5, "C2"),
        };
        return new ParentModel(nodes, members, meshNodes, elements);
    }

    public static IReadOnlyList<FemLoadCase> LoadCases() => [new FemLoadCase { Id = 1, SchemaId = 1, Tag = "G+H" }];

    public static IReadOnlyList<FemMemberLoad> MemberLoads() =>
    [
        new FemMemberLoad { Id = 1, LoadCaseId = 1, MemberId = 2, DistributionType = "uniform", CoordinateSystem = "global", QzStart = Q, QzEnd = Q }
    ];

    public static IReadOnlyList<FemNodeLoad> NodeLoads() => [new FemNodeLoad { Id = 1, LoadCaseId = 1, NodeId = 2, Fx = H }];

    static FemMember Member(int id, string tag, string nodes) => new()
    {
        Id = id, ElemTag = tag, ElemType = "beam", NodeIdsJson = nodes, CrossSectionId = SectionId,
        GjStrategy = "manual", GjManualValue = 1e9
    };

    static FemMeshNode MeshNode(int tag, double x, double z, string? source, string member) => new()
    {
        Id = 100 + tag, NodeTag = tag.ToString(), X = x, Z = z, SourceNodeTag = source, SourceMemberTag = member
    };

    static FemElement Element(int tag, int i, int j, string member) => new()
    {
        Id = 200 + tag, ElemTag = tag.ToString(), ElemType = "beam", NodeIdsJson = $"[{i},{j}]", SourceMemberTag = member,
        CrossSectionId = SectionId, GjStrategy = "manual", GjManualValue = 1e9
    };

    /// <summary>Извлечение через реальные Срезы 1–2: адаптер сетки → анализатор → сборщик draft.</summary>
    public static SubmodelExtraction Extract(ParentModel parent, IReadOnlyCollection<string> selectedElements) =>
        ExtractWithMesh(parent, selectedElements).Extraction;

    /// <summary>Извлечение вместе с дочерним mesh-снимком draft (как его сохранил бы Срез 2).</summary>
    public static (SubmodelExtraction Extraction, IReadOnlyList<FemMeshNode> MeshNodes, IReadOnlyList<FemElement> MeshElements)
        ExtractWithMesh(ParentModel parent, IReadOnlyCollection<string> selectedElements)
    {
        var adapted = MeshBeamSegmentAdapter.Build(selectedElements, parent.MeshElements, parent.MeshNodes, parent.Members);
        var analysis = StraightBeamAnalyzer.Analyze(adapted.Segments, adapted.Environment, ChainTolerances.Default,
            adapted.PreferredDirection, new BeamLocalAxisFrameProvider(), adapted.Diagnostics);
        var draft = StraightBeamSubmodelBuilder.Build(1, analysis, parent.MeshElements, parent.MeshNodes);
        Assert.True(draft.IsSuccess, string.Join(" | ", draft.Diagnostics.Select(d => d.Message)));
        return (FromDraft(draft.Draft!, "{}"), draft.Draft!.MeshNodes, draft.Draft!.MeshElements);
    }

    /// <summary>Та же доменная модель, что вернул бы DatabaseService после сохранения draft (без БД).</summary>
    public static SubmodelExtraction FromDraft(SubmodelExtractionDraft draft, string loadExpressionJson) => new()
    {
        Id = 1, ParentSchemaId = draft.ParentSchemaId, SubmodelSchemaId = 2, ParentAnalysisId = 3, ParentResultId = 4,
        LoadExpressionJson = loadExpressionJson, ReferenceScale = 1.0, Tolerances = draft.Tolerances, Metrics = draft.Metrics,
        Diagnostics = draft.Diagnostics,
        Nodes = draft.Nodes.Select((n, k) => new SubmodelExtractionNode(k + 1, k + 1, n.SubmodelNode.NodeTag,
            n.ParentNodeId, n.ParentNodeTag, n.SubmodelNode.X, n.SubmodelNode.Y, n.SubmodelNode.Z,
            n.SourceNodeTag, n.SourceMemberTag)).ToList(),
        Segments = draft.Segments.Select(s => new SubmodelExtractionSegment(s.Ordinal + 1, s.Ordinal, s.Ordinal + 1,
            s.SubmodelElement.ElemTag, s.ParentElementId, s.ParentElementTag, s.SourceMemberTag, s.IsReversed,
            s.StartStationM, s.EndStationM, s.LengthM, s.AngleToAxisDeg, s.BetaDeg, s.BetaSource)).ToList()
    };

    public static BoundaryScenario Build(ParentModel parent, SubmodelExtraction extraction, IParentLinearResult result,
        IReadOnlyList<DofOverride>? overrides = null) =>
        StraightBeamBoundaryScenarioBuilder.Build(new BoundaryScenarioInput(extraction, "opensees",
            parent.Nodes, parent.Members, parent.MeshNodes, parent.MeshElements, LoadCases(), NodeLoads(),
            MemberLoads(), [], result, overrides ?? [])).Scenario;

    /// <summary>Результат родителя из записанной фикстуры (всегда доступен, без OpenSees).</summary>
    public static IParentLinearResult FixtureResult()
    {
        string copied = Path.Combine(AppContext.BaseDirectory, FixtureRelativePath);
        if (File.Exists(copied)) return ToParentResult(Deserialize(File.ReadAllText(copied)));
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "CScore.Tests", FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return ToParentResult(Deserialize(File.ReadAllText(candidate)));
        }
        throw new FileNotFoundException("Не найдена фикстура рамы.", FixtureRelativePath);
    }

    /// <summary>
    /// Общие проверки эталона. Главная — совпадение граничного вектора с контрольным остатком
    /// (конвенция localForce); затем равновесие субмодели и знак опорных моментов ригеля по физике.
    /// </summary>
    public static void Verify(ParentModel parent, BoundaryScenario scenario)
    {
        Assert.True(scenario.Status == ScenarioStatus.Complete,
            string.Join(" | ", scenario.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        Assert.Equal(2, scenario.Ends.Count);
        foreach (var end in scenario.Ends)
        {
            Assert.True(end.Control.Available);
            Assert.True(end.Control.Passed,
                $"Конец {end.ParentNodeTag}: расхождение сил {end.Control.ForceMismatch}, моментов {end.Control.MomentMismatch}; " +
                string.Join(" | ", scenario.Diagnostics.Where(d => d.Code == BoundaryScenarioDiagnostics.ControlMismatch).Select(d => d.Message)));
            Assert.All(end.Dofs, d => Assert.Equal(DofMode.Force, d.Mode));
        }
        Assert.Contains(scenario.Diagnostics, d => d.Code == BoundaryScenarioDiagnostics.GaugeRequired);

        var (force, moment, scale) = Residual(parent, scenario);
        Assert.True(force.Length <= 1e-6 * scale.Force, $"Невязка сил субмодели {force.Length} Н");
        Assert.True(moment.Length <= 1e-6 * scale.Moment, $"Невязка моментов субмодели {moment.Length} Н·м");
    }

    /// <summary>
    /// Знак опорных моментов ригеля, извлечённого целиком (узлы 2 и 5). Жёсткие узлы рамы под
    /// вертикальной нагрузкой — опорные моменты растягивают верхнюю (+Z) грань ригеля.
    /// Действие отброшенной левой части (колонны) на левый торец: момент My·ŷ. По правилу правой руки
    /// вокруг +Y (+Z переходит в +X) положительный My вдавливает верх левого торца внутрь балки — сжатие
    /// верха; значит растяжение верха ⇔ My(левый торец) &lt; 0. Для правого торца зеркально: My &gt; 0.
    /// (Для шарнирной балки аналитический эталон CScore.Tests даёт обратные знаки — растянут низ.)
    /// </summary>
    public static void VerifyHoggingAtRigidJoints(BoundaryScenario scenario)
    {
        var left = scenario.Ends.Single(e => e.ParentNodeTag == "2").BoundaryVector!;
        var right = scenario.Ends.Single(e => e.ParentNodeTag == "5").BoundaryVector!;
        Assert.True(left.Ry < 0, $"My левого торца = {left.Ry}: ожидалось растяжение верха (My < 0).");
        Assert.True(right.Ry > 0, $"My правого торца = {right.Ry}: ожидалось растяжение верха (My > 0).");
        // Вертикальные силы торцов в сумме уравновешивают q·L (нагрузка на ригеле целиком retained).
        Assert.Equal(-Q * 6, left.Z + right.Z, 3);
    }

    static (PlanarVector3 Force, PlanarVector3 Moment, (double Force, double Moment) Scale) Residual(
        ParentModel parent, BoundaryScenario scenario)
    {
        var point = parent.MeshNodes.ToDictionary(n => n.NodeTag, n => new PlanarVector3(n.X, n.Y, n.Z));
        var origin = point[scenario.Ends[0].ParentNodeTag];
        var force = PlanarVector3.Zero;
        var moment = PlanarVector3.Zero;
        double forceScale = 1, momentScale = 1;
        foreach (var end in scenario.Ends)
        {
            var v = end.BoundaryVector!;
            force += v.Force;
            moment += v.Moment + (point[end.ParentNodeTag] - origin).Cross(v.Force);
            forceScale = Math.Max(forceScale, v.MaxForceAbs);
            momentScale = Math.Max(momentScale, v.MaxMomentAbs);
        }
        foreach (var load in scenario.RetainedDistributedLoads)
        {
            var element = parent.MeshElements.Single(e => e.ElemTag == load.ChildElementTag);
            var ends = FemMeshTopology.ReadNodeTags(element)!;
            var i = point[ends[0]];
            var j = point[ends[1]];
            double length = (j - i).Length;
            var axis = (j - i) * (1.0 / length);
            double a = load.AOverL * length, b = load.BOverL * length;
            var resultant = (load.QAtA + load.QAtB) * ((b - a) / 2);
            double qa = load.QAtA.Length, qb = load.QAtB.Length;
            double centroid = qa + qb < 1e-12 ? (a + b) / 2 : a + (b - a) * (qa + 2 * qb) / (3 * (qa + qb));
            force += resultant;
            moment += (i + axis * centroid - origin).Cross(resultant);
            forceScale = Math.Max(forceScale, resultant.Length);
        }
        return (force, moment, (forceScale, momentScale));
    }

    // --- Фикстура результата (нейтральный формат, без типов OpenSees) ---

    public sealed record NodeRow(string Node, double[] Values);
    public sealed record ElementRow(string Element, double[] I, double[] J);
    public sealed record ResultFixture(List<NodeRow> Displacements, List<NodeRow> Reactions, List<ElementRow> EndForces);

    static readonly JsonSerializerOptions FixtureJson = new() { WriteIndented = true };

    public static string Serialize(ResultFixture fixture) => JsonSerializer.Serialize(fixture, FixtureJson);

    public static ResultFixture Deserialize(string json) => JsonSerializer.Deserialize<ResultFixture>(json, FixtureJson)!;

    public static IParentLinearResult ToParentResult(ResultFixture fixture)
    {
        static Dof6 D(double[] v) => new(v[0], v[1], v[2], v[3], v[4], v[5]);
        return new DictionaryParentLinearResult(true,
            fixture.Displacements.ToDictionary(r => r.Node, r => D(r.Values)),
            fixture.Reactions.ToDictionary(r => r.Node, r => D(r.Values)),
            fixture.EndForces.ToDictionary(r => r.Element, r => new BeamEndForces(D(r.I), D(r.J))));
    }
}
