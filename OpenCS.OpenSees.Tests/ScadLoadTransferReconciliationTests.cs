using CScore.Fem;
using CScore.Fem.Loads;
using CScore.Import;
using OpenCS.OpenSees.CScore;

namespace OpenCS.OpenSees.Tests;

/// <summary>
/// Перенос нагрузок SCAD в нагрузки сеточного уровня даёт те же узловые силы, что эталонный перевод сборщика
/// оболочек (<see cref="ScadShellModelAssembler.NodalLoads"/>, сверен с протоколом SCAD на плите Дорфмана).
/// </summary>
public sealed class ScadLoadTransferReconciliationTests
{
    [Theory]
    [InlineData(1)] // собственный вес плиты и колонны (Qw 96)
    [InlineData(2)] // давление на плиту (Qw 16, Qn 3)
    public void NodalForces_MatchAssembler(int loadCase)
    {
        var data = ScadShellModelAssemblerTests.Data();
        // Средний узел плиты сдвинут — четырёхугольники не прямоугольные, веса Гаусса нетривиальны.
        int mid = data.Nodes.FindIndex(n => n.Id == 5);
        data.Nodes[mid] = data.Nodes[mid] with { X = 1.23, Y = 0.88 };
        var model = data.AnalysisModel!;
        var lc = model.LoadCases.Single(c => c.Num == loadCase);

        var report = new List<string>();
        var expected = ScadShellModelAssembler.NodalLoads(lc, data, data.Stiffnesses.ToDictionary(s => s.Id),
            data.Nodes.ToDictionary(n => n.Id), model.ForceUnitN, model.LengthUnitM, report);

        var meshNodes = ScadSchemaConverter.ToFemMeshNodes(data, 1);
        var elements = ScadSchemaConverter.ToFemMeshElements(data, 1);
        var stiffness = ScadSchemaConverter.ToSchemaStiffnesses(data).ToDictionary(s => s.Id);
        int next = 0;
        var transfer = ScadLoadTransfer.Transfer(model, elements.ToDictionary(e => e.ElemTag, e => e.ElemType),
            [], [], [], () => --next);
        var femCase = transfer.LoadCases.Single(c => c.SourceLoadNum == loadCase);
        var mesh = new FemLoadMeshContext(meshNodes, elements, null,
            new ScadElementStiffnessSource(stiffness, model.ForceUnitN, model.LengthUnitM));
        var actual = FemLoadCaseNodalForces.Resolve(femCase, transfer.ElementLoads, transfer.MeshNodeLoads, mesh);

        Assert.Empty(actual.Diagnostics);
        var byTag = actual.Forces.ToDictionary(f => f.NodeTag);
        Assert.Equal(expected.Keys.Order(), byTag.Keys.Select(int.Parse).Order());
        foreach (var (node, down) in expected)
        {
            var f = byTag[node.ToString()];
            Assert.Equal(-down, f.Fz, 6); // сборщик: плюс — вниз
            Assert.Equal(0, f.Fx, 9);
            Assert.Equal(0, f.Fy, 9);
        }
    }
}
