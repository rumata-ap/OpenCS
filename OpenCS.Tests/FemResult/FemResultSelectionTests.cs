using System.Text.Json;
using CScore;
using CScore.Fem;
using OpenCS.OpenSees.Structural;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using OpenCS.ViewModels.FemResult;
using Xunit;

namespace OpenCS.Tests.FemResult;

/// <summary>Выбор «КонЭ / КЭ» в показанном результате расчёта и карточка значений выбранного.</summary>
public class FemResultSelectionTests
{
    static FemMeshNode N(string tag, double x, double y) => new() { NodeTag = tag, X = x, Y = y };

    // Стержни 1, 2 — КЭ дискретизации КонЭ «Б1», стержень 3 — импортированный КЭ (его КонЭ не учитывается),
    // пластина 4 — без КонЭ.
    static readonly FemMeshNode[] Nodes = [N("1", 0, 0), N("2", 1, 0), N("3", 2, 0), N("4", 0, 1)];

    static readonly FemElement[] Elements =
    [
        new() { ElemTag = "1", NodeIdsJson = "[1,2]", SourceMemberTag = "Б1" },
        new() { ElemTag = "2", NodeIdsJson = "[2,3]", SourceMemberTag = " Б1 " },
        new() { ElemTag = "3", NodeIdsJson = "[3,4]", SourceMemberTag = "Б1", Origin = FemMember.MeshSourceImported },
        new() { ElemTag = "4", ElemType = "shell", NodeIdsJson = "[1,2,4]" },
    ];

    static FemResultMesh Mesh() => new(Nodes, Elements);

    [Fact]
    public void Mesh_OwnersOfDiscretizedElementsOnly()
    {
        var mesh = Mesh();
        Assert.Equal("Б1", mesh.OwnerOf("1"));
        Assert.Equal("Б1", mesh.OwnerOf("2"));
        Assert.Null(mesh.OwnerOf("3"));
        Assert.Null(mesh.OwnerOf("4"));
        Assert.Equal(["1", "2"], mesh.ElementsOf("Б1"));
        Assert.Empty(mesh.ElementsOf("Б2"));
        Assert.True(mesh.IsBar("3"));
        Assert.False(mesh.IsBar("4"));
    }

    [Fact]
    public void Selection_MembersAndElementsModes()
    {
        var s = new FemResultSelection(Mesh());
        int changed = 0;
        s.Changed += () => changed++;

        // КонЭ: щелчок по КЭ выбирает все КЭ его КонЭ; КЭ без КонЭ — сам.
        s.PickElement("2", FemResultPickMode.Members, additive: false);
        Assert.Equal(["1", "2"], s.Elements.Order());
        Assert.Equal(["Б1"], s.Members);
        Assert.Equal("Б1", s.SoleMember);
        s.PickElement("4", FemResultPickMode.Members, additive: true);
        Assert.Equal(["1", "2", "4"], s.Elements.Order());
        Assert.Null(s.SoleMember);   // КЭ без КонЭ в выборе — набор не привязывается
        // Ctrl по уже выбранному КонЭ — убрать его целиком.
        s.PickElement("1", FemResultPickMode.Members, additive: true);
        Assert.Equal(["4"], s.Elements);

        // Смена режима сбрасывает прежний выбор; КЭ — по одному.
        s.PickElement("1", FemResultPickMode.Elements, additive: true);
        Assert.Equal(["1"], s.Elements);
        Assert.Equal(FemResultPickMode.Elements, s.Mode);

        // Узел вытесняет КЭ; повторный щелчок снимает выбор.
        s.PickNode(3);
        Assert.Equal(3, s.Node);
        Assert.Empty(s.Elements);
        s.PickNode(3);
        Assert.True(s.IsEmpty);
        Assert.Equal(6, changed);

        s.Clear();
        Assert.Equal(6, changed);   // пустой выбор не меняется
    }

    [Fact]
    public void Selection_MemberSkipsHiddenElements()
    {
        var s = new FemResultSelection(Mesh()) { IsVisible = t => t != "1" };
        s.PickElement("2", FemResultPickMode.Members, additive: false);
        Assert.Equal(["2"], s.Elements);
    }

    sealed class TempDb : IDisposable
    {
        readonly string _path = Path.Combine(Path.GetTempPath(), "opencs_result_pick_" + Guid.NewGuid().ToString("N") + ".db");
        public DatabaseService Db { get; }
        public FemSchema Schema { get; } = new() { Tag = "Схема" };

        public TempDb()
        {
            Db = new DatabaseService(_path);
            Db.SaveFemSchema(Schema);
            Db.SaveFemMeshSnapshot(Schema.Id, Nodes, Elements);
        }

        public FemResultMesh Mesh() => new(Db.GetFemMeshNodes(Schema.Id), Db.GetFemMeshElements(Schema.Id));

        public void Dispose()
        {
            Db.Dispose();
            try { File.Delete(_path); } catch (IOException) { }
        }
    }

    static FemResultLayer Layer(TempDb t)
    {
        var result = new FemNonlinearResult
        {
            Status = "ok",
            Steps =
            [
                new FemNonlinearStepResult(1, 0.5, true, [new FemNodeDisplacement(2, 0, 0, -0.01, 0, 0, 0)], [], []),
                new FemNonlinearStepResult(2, 1.0, true,
                    [new FemNodeDisplacement(2, 0, 0, -0.02, 0, 0, 0), new FemNodeDisplacement(3, 0, 0, -0.04, 0, 0, 0)], [], []),
            ],
            CalcTypeName = "C",
        };
        var steps = new OpenSeesResultSteps(new FemAnalysisResultVM(
            new CalcResult { Status = "ok", DataJson = JsonSerializer.Serialize(result) }, t.Db, t.Schema));
        return new FemResultLayer(new FemAnalysis { Tag = "Р1" }, "Р1", steps, t.Mesh());
    }

    static string Row(FemResultCard card, string label) => card.Rows.Single(r => r.Label == label).Value;

    static string Uz => $"uz, {Loc.S("UnitMm")}";

    [Fact]
    public void Card_NodeFollowsStep()
    {
        using var t = new TempDb();
        var layer = Layer(t);
        Assert.Null(layer.Card);
        Assert.Same(layer.OpenSees, layer.OpenSees);
        Assert.NotNull(layer.OpenSees);
        Assert.Same(t.Schema, layer.OpenSees!.Schema);

        layer.Selection.PickNode(2);
        var card = layer.Card!;
        Assert.Equal(string.Format(Loc.S("FemResultCardNode"), 2), card.Title);
        Assert.Equal((-20.0).ToString("0.###"), Row(card, Uz));
        Assert.Equal($"{1.0:0.###}; {0.0:0.###}; {0.0:0.###}", Row(card, Loc.S("FemResultCardCoordinates")));

        layer.SelectedStepIndex = 0;
        Assert.Equal((-10.0).ToString("0.###"), Row(layer.Card!, Uz));

        layer.Selection.Clear();
        Assert.Null(layer.Card);
    }

    [Fact]
    public void Card_ElementAndMember()
    {
        using var t = new TempDb();
        var layer = Layer(t);

        // Один КЭ в режиме КЭ: КонЭ и среднее перемещение узлов (−20 и −40 → −30 мм).
        layer.Selection.PickElement("2", FemResultPickMode.Elements, additive: false);
        var card = layer.Card!;
        Assert.Equal(string.Format(Loc.S("FemResultCardBar"), "2"), card.Title);
        Assert.Equal("Б1", Row(card, Loc.S("FemResultCardOwner")));
        Assert.Equal((-30.0).ToString("0.###"), Row(card, Uz));

        // КонЭ: наибольшая по модулю величина с номером КЭ.
        layer.Selection.PickElement("1", FemResultPickMode.Members, additive: false);
        card = layer.Card!;
        Assert.Equal(string.Format(Loc.S("FemResultCardMember"), "Б1", 2), card.Title);
        Assert.Equal(string.Format(Loc.S("FemResultCardExtremum"), (-30.0).ToString("0.###"), "2"), Row(card, Uz));

        // Пластина без КонЭ в режиме КонЭ — карточка самого КЭ.
        layer.Selection.PickElement("4", FemResultPickMode.Members, additive: false);
        Assert.Equal(string.Format(Loc.S("FemResultCardShell"), "4"), layer.Card!.Title);
    }
}
