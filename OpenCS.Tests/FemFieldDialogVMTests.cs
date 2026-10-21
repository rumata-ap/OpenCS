using CScore.Fem;
using CScore.Fem.Fields;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Логика диалога поля: автовыбор столбцов и единиц, автоподбор совмещения, предпросмотр, сборка поля.</summary>
public class FemFieldDialogVMTests
{
    /// <summary>Плита 10 × 4 КЭ 1 × 1 м на отметке −16,3; узлы КЭ в порядке хранения «1 2 4 3»; пружины в двух узлах.</summary>
    static FemFieldMeshContext Plate()
    {
        var nodes = new List<FemMeshNode>();
        var elements = new List<FemElement>();
        int Tag(int i, int j) => j * 11 + i + 1;
        for (int j = 0; j <= 4; j++)
            for (int i = 0; i <= 10; i++)
                nodes.Add(new FemMeshNode { NodeTag = Tag(i, j).ToString(), X = i, Y = j, Z = -16.3 });
        for (int j = 0; j < 4; j++)
            for (int i = 0; i < 10; i++)
                elements.Add(new FemElement
                {
                    ElemTag = (j * 10 + i + 1).ToString(), ElemType = "shell",
                    NodeIdsJson = $"[{Tag(i, j)},{Tag(i + 1, j)},{Tag(i, j + 1)},{Tag(i + 1, j + 1)}]",
                });
        var springs = new List<FemSpring> { new() { NodeTag = Tag(2, 2).ToString(), Kz = 1 }, new() { NodeTag = Tag(7, 2).ToString(), Kz = 1 } };
        return new FemFieldMeshContext(nodes, elements, [], [], springs);
    }

    [Fact]
    public void C1Table_GuessesColumnsUnits_FitsShift_Previews()
    {
        var csv = "X;Y;Z;C1_node_smooth_t_m3\n" + string.Join("\n",
            from i in Enumerable.Range(0, 12) from j in Enumerable.Range(0, 6)
            select FormattableString.Invariant($"{i - 63.6 - 0.5};{j - 29.7 - 0.5};132.6;{100 + i}"));
        var vm = new FemFieldDialogVM(Plate(), new FemFieldTargets(), tonToKn: 9.80665);
        vm.LoadText(csv, "Cz.csv");
        Assert.Equal(0, vm.XColumn);
        Assert.Equal(1, vm.YColumn);
        Assert.Equal(2, vm.ZColumn);
        Assert.Equal(FemFieldQuantities.FoundationC1, vm.Quantity);
        var col = Assert.Single(vm.Columns);
        Assert.Equal("тс/м³", col.Unit.Label);
        Assert.Equal(2, vm.MaxDistance, 12);      // 2 × размер КЭ 1 м
        Assert.Equal(1, vm.PlaneTolerance, 12);

        vm.FitByExtents();
        Assert.Equal(63.6, vm.Dx, 6);
        Assert.Equal(29.7, vm.Dy, 6);
        Assert.Equal(-148.9, vm.Dz, 6);
        Assert.NotNull(vm.Fit);

        vm.Preview();
        Assert.NotNull(vm.Evaluation);
        Assert.Equal(40, vm.Evaluation!.Targets.Count);
        Assert.All(vm.Evaluation.Targets, t => Assert.Equal(FieldHow.Interpolated, t.How));
        Assert.NotEmpty(vm.Summary);

        var field = vm.BuildField(schemaId: 3);
        Assert.Equal("Cz", field.Name);
        Assert.Equal(3, field.SchemaId);
        Assert.Equal(72, field.Points.Count);
        Assert.True(field.HasZ);
        Assert.Equal(9806.65, field.Definition.Columns[0].UnitFactor, 6);
        Assert.Equal("Z", field.Definition.ZHeader);
    }

    [Fact]
    public void RzTable_CmUnits_NoZ_SpringMatch()
    {
        var csv = "X [см];Y [см];Z;Rz [тс/см]\n-6160;-950;;300\n-5660;-950;;200";
        var vm = new FemFieldDialogVM(Plate(), new FemFieldTargets(), tonToKn: 9.80665);
        vm.LoadText(csv, "Rz.csv");
        Assert.Equal(-1, vm.ZColumn);
        Assert.Equal(0.01, vm.LengthScale);
        Assert.Equal(FemFieldQuantities.Spring, vm.Quantity);
        Assert.Equal("kz", vm.Columns[0].Component);
        Assert.Equal("тс/см", vm.Columns[0].Unit.Label);
        Assert.Equal(FieldMethod.Match, vm.Method);

        vm.FitByPoints();
        Assert.True(vm.Fit!.Max < 1e-6);
        vm.Preview();
        Assert.Equal(2, vm.Evaluation!.Targets.Count(t => t.How == FieldHow.Matched));
        Assert.Equal(980665, vm.BuildField(1).Definition.Columns[0].UnitFactor, 6);   // 1 тс/см = 9,80665 кН / 0,01 м
    }

    [Fact]
    public void SetQuantity_RebuildsComponentsAndUnits()
    {
        var vm = new FemFieldDialogVM(Plate(), new FemFieldTargets(), tonToKn: 9.80665);
        vm.LoadText("X;Y;V\n2;2;300\n7;2;200", "v.csv");
        Assert.Equal(FemFieldQuantities.FoundationC1, vm.Quantity);
        Assert.Equal("c1", vm.Columns[0].Component);

        vm.SetQuantity(FemFieldQuantities.Spring);
        Assert.Equal(FieldMethod.Match, vm.Method);
        var row = Assert.Single(vm.Columns);
        Assert.Equal("kz", row.Component);
        Assert.Equal([FemFieldColumnRow.Skip, .. FemFieldQuantities.SpringComponents], row.Components);
        Assert.Equal("тс/м", row.Unit.Label);

        row.Component = "kuz";                       // поворотная — единицы момента на радиан
        Assert.Equal("тс·м/рад", row.Unit.Label);

        vm.Preview();
        Assert.Equal(2, vm.NodeTargets.Count);

        vm.SetQuantity(FemFieldQuantities.FoundationC1);
        Assert.Equal(FieldMethod.Interpolate, vm.Method);
        Assert.Equal("c1", vm.Columns[0].Component);
    }

    [Fact]
    public void ColumnChoice_RebuildsValueColumns()
    {
        var vm = new FemFieldDialogVM(Plate(), new FemFieldTargets(), tonToKn: 9.80665);
        vm.LoadText("X;Y;A;B\n1;1;-16;300\n2;2;-16;200", "v.csv");
        Assert.Equal([2, 3], vm.Columns.Select(c => c.ColumnIndex));
        vm.ZColumn = 2;
        vm.RebuildValueColumns();
        Assert.Equal([3], vm.Columns.Select(c => c.ColumnIndex));
        Assert.True(vm.BuildField(1).HasZ);
    }

    [Fact]
    public void ExistingField_KeepsPointsAndColumns_UntilNewTable()
    {
        var existing = new FemField
        {
            Id = 9, SchemaId = 1, Name = "C1", Quantity = FemFieldQuantities.FoundationC1, HasZ = false, SourceFile = "a.csv",
            Definition = new FemFieldDefinition
            {
                Columns = [new FemFieldColumn("C1", "c1", "кН/м³", 1000)],
                Alignment = new FieldAlignment(Dx: 5),
                Settings = new FieldSettings(FieldMethod.Nearest, 3, 0.7),
            },
            Points = [new(new(0, 0, 0), [10])],
        };
        var vm = new FemFieldDialogVM(Plate(), new FemFieldTargets(), 9.80665, existing);
        var rebuilt = vm.BuildField(1);
        Assert.Equal(9, rebuilt.Id);
        Assert.Single(rebuilt.Points);
        Assert.Equal(existing.Definition.Columns, rebuilt.Definition.Columns);
        Assert.Equal(5, rebuilt.Definition.Alignment.Dx);
        Assert.Equal(FieldMethod.Nearest, rebuilt.Definition.Settings.Method);
    }

    [Fact]
    public void ExtraNumericColumn_IsSkipped_AndRecognizedColumnWins()
    {
        // «Номер» стоит раньше C1: компоненту c1 получает узнанный столбец, номер — «не использовать».
        var csv = "Номер;X;Y;C1 [кН/м3]\n1;0;0;10\n2;10;0;20\n3;0;4;30\n4;10;4;40";
        var vm = new FemFieldDialogVM(Plate(), new FemFieldTargets(), tonToKn: 9.80665);
        vm.LoadText(csv, "c1.csv");
        Assert.Equal([0, 3], vm.Columns.Select(c => c.ColumnIndex));
        Assert.Equal(FemFieldColumnRow.Skip, vm.Columns[0].Component);
        Assert.Equal("c1", vm.Columns[1].Component);
        Assert.Equal("кН/м³", vm.Columns[1].Unit.Label);
        var field = vm.BuildField(1);
        Assert.Equal("C1 [кН/м3]", Assert.Single(field.Definition.Columns).Header);
        Assert.Equal([10.0], field.Points[0].Values);
        Assert.Null(vm.Validate());

        // Две колонки на одну компоненту — ошибка; ни одной — тоже.
        vm.Columns[0].Component = "c1";
        Assert.NotNull(vm.Validate());
        vm.Columns[0].Component = FemFieldColumnRow.Skip;
        vm.Columns[1].Component = FemFieldColumnRow.Skip;
        Assert.Equal("FemFieldNoValueColumns", vm.Validate());
    }

    [Fact]
    public void ColumnRow_NullUnitFromComboBox_Ignored_UnitsFollowComponent()
    {
        var row = new FemFieldColumnRow(3, "Rz", FemFieldQuantities.Spring, "kz", 9.80665);
        row.Component = "kux";
        row.Unit = null!;
        Assert.Equal("тс·м/рад", row.Unit.Label);
    }

    [Theory]
    [InlineData("Rz [кН/м]", "кН/м")]
    [InlineData("Rz, MN/m", "МН/м")]
    [InlineData("Kz Н/м", "Н/м")]
    [InlineData("Rz [тс/см]", "тс/см")]
    [InlineData("Rz", "тс/м")]
    public void SpringUnit_FromHeader(string header, string expected)
    {
        var csv = $"X;Y;{header}\n0;0;1";
        var vm = new FemFieldDialogVM(Plate(), new FemFieldTargets(), tonToKn: 9.80665);
        vm.LoadText(csv, null);
        Assert.Equal(expected, vm.Columns[0].Unit.Label);
    }

    [Fact]
    public void RepeatWithNewTable_KeepsQuantity_AndValidatesTargets()
    {
        var existing = new FemField
        {
            Id = 4, SchemaId = 1, Name = "Сваи", Quantity = FemFieldQuantities.Spring,
            Definition = new FemFieldDefinition { Columns = [new FemFieldColumn("Rz", "kz", "тс/м", 9806.65)] },
            Points = [new(new(2, 2, 0), [5])],
        };
        var vm = new FemFieldDialogVM(Plate(), new FemFieldTargets(), 9.80665, existing);
        vm.LoadText("X;Y;Value\n2;2;7", "new.csv");
        Assert.Equal(FemFieldQuantities.Spring, vm.Quantity);
        Assert.Equal("kz", Assert.Single(vm.Columns).Component);

        // Узлы как цели C1: пластин нет — сохранять нечего.
        var c1 = new FemFieldDialogVM(Plate(), new FemFieldTargets { NodeTags = ["1", "2"] }, 9.80665);
        c1.LoadText("X;Y;C1\n0;0;1\n1;0;2", null);
        Assert.Equal(FemFieldQuantities.FoundationC1, c1.Quantity);
        Assert.Equal("FemFieldNoShellTargets", c1.Validate());
    }
}
