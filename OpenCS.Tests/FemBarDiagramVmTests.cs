using CScore;
using CScore.Fem;
using CScore.PlateRebar;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Окно эпюр стержней: сбор данных цели из БД и переключение вида эпюры.</summary>
public class FemBarDiagramVmTests
{
    static DatabaseService NewDb() => new(Path.Combine(Path.GetTempPath(),
        "opencs_bar_diagram_" + Guid.NewGuid().ToString("N") + ".db"));

    static LoadItem Row(int elem, int section, double mx) =>
        new() { Label = $"э.{elem} с{section}", SourceElementNum = elem, SourceSectionNum = section, Mx = mx };

    /// <summary>Схема «только сетка»: балка из КЭ 1–2 вдоль X, отдельно стоящая колонна КЭ 3 и пластина КЭ 4.</summary>
    static FemSchema MeshOnlySchema(DatabaseService db)
    {
        var schema = new FemSchema { Tag = "Схема Лира (API)", SourceType = "lira" };
        db.SaveFemSchema(schema);
        db.SaveFemMeshSnapshot(schema.Id,
            [new FemMeshNode { NodeTag = "1", X = 0 }, new FemMeshNode { NodeTag = "2", X = 2 },
             new FemMeshNode { NodeTag = "3", X = 5 }, new FemMeshNode { NodeTag = "4", X = 9 },
             new FemMeshNode { NodeTag = "5", X = 9, Z = 3 }, new FemMeshNode { NodeTag = "6", X = 0, Y = 4 }],
            [new FemElement { ElemTag = "1", NodeIdsJson = "[1,2]" },
             new FemElement { ElemTag = "2", NodeIdsJson = "[2,3]" },
             new FemElement { ElemTag = "3", NodeIdsJson = "[4,5]" },
             new FemElement { ElemTag = "4", ElemType = "shell", NodeIdsJson = "[1,2,6]" }]);
        return schema;
    }

    [Fact]
    public void Load_GroupOfMeshBars_ChainsAndForceSetsOfItsElements()
    {
        using var db = NewDb();
        var schema = MeshOnlySchema(db);
        db.ForceSets.Add(new ForceSet
        {
            Tag = "Собственный вес", Kind = "bar", SourceType = "fea", SourceSchemaId = schema.Id,
            Items = [Row(1, 1, 0), Row(1, 2, 20), Row(2, 1, 20), Row(2, 2, -10)],
        });
        db.ForceSets.Add(new ForceSet
        {
            Tag = "Чужие стержни", Kind = "bar", SourceType = "fea", SourceSchemaId = schema.Id, Items = [Row(77, 1, 5)],
        });
        db.ForceSets.Add(new ForceSet
        {
            Tag = "Другая схема", Kind = "bar", SourceType = "fea", SourceSchemaId = schema.Id + 1, Items = [Row(1, 1, 5)],
        });
        var group = new FemMemberGroup { SchemaId = schema.Id, Tag = "Жёсткость 1", Kind = FemMemberGroup.KindMesh, MemberTagsJson = "[1,2,3,4]" };

        var vm = FemBarDiagramVM.Load(db, group)!;

        Assert.Equal("Жёсткость 1", vm.TargetTag);
        // Балка 1–2 и колонна 3 — отдельные участки; пластина в эпюры не входит.
        Assert.Equal([[1, 2], [3]], vm.Chains.Select(c => c.Chain.Elements.Select(e => e.ElemNum).ToArray()));
        Assert.True(vm.HasSeveralChains);
        Assert.Equal(["Собственный вес"], vm.ForceSets.Select(fs => fs.Tag));
        Assert.Equal([FemBarDiagramKind.Forces], vm.Kinds.Select(k => k.Kind));
        Assert.False(vm.NoData);

        Assert.Equal(BarForceComponent.Mx, vm.SelectedComponent!.Component);
        Assert.Equal(
            [new BarDiagramSegment(0, 2, 0, 20), new BarDiagramSegment(2, 5, 20, -10)], vm.Series.Upper);
        Assert.False(vm.HasEnvelope);
        Assert.Equal(4, vm.Rows.Count);
        Assert.Contains("Собственный вес", vm.Title);

        // У колонны строк в наборе нет — эпюра пустая.
        vm.SelectedChain = vm.Chains[1];
        Assert.Empty(vm.Series.Upper);
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public void Load_GroupWithoutBars_ReturnsNull()
    {
        using var db = NewDb();
        var schema = MeshOnlySchema(db);
        var group = new FemMemberGroup { SchemaId = schema.Id, Tag = "Плита", Kind = FemMemberGroup.KindMesh, MemberTagsJson = "[4]" };

        Assert.Null(FemBarDiagramVM.Load(db, group));
    }

    [Fact]
    public void Load_BarsWithoutForcesAndRebar_NoData()
    {
        using var db = NewDb();
        var schema = MeshOnlySchema(db);
        var group = new FemMemberGroup { SchemaId = schema.Id, Tag = "Колонна", Kind = FemMemberGroup.KindMesh, MemberTagsJson = "[3]" };

        var vm = FemBarDiagramVM.Load(db, group)!;

        Assert.True(vm.NoData);
        Assert.False(vm.HasSeveralChains);
        Assert.Empty(vm.Series.Upper);
    }

    [Fact]
    public void Dialog_CreatesOnStaAndFollowsViewModel()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var chain = BarChains.Build([new BarChainBar(7, 1, 2, (0, 0, 0), (4, 0, 0))]);
                var set = new ForceSet { Tag = "РСУ (C)", Kind = "bar", Items = [Row(7, 1, 5), Row(7, 1, -2), Row(7, 2, 1)] };
                var vm = new FemBarDiagramVM("Балка Б1", chain, [set], new Rebar(), "1-lin.asp");
                var dialog = new Views.FemBarDiagramDialog(vm) { Width = 900, Height = 500 };
                dialog.Measure(new System.Windows.Size(900, 500));
                dialog.Arrange(new System.Windows.Rect(0, 0, 900, 500));
                dialog.UpdateLayout();

                var minColumn = (System.Windows.Controls.DataGridColumn)dialog.FindName("minColumn");
                Assert.Equal(System.Windows.Visibility.Visible, minColumn.Visibility);

                vm.SelectedKind = vm.Kinds.Single(k => k.Kind == FemBarDiagramKind.Rebar);
                vm.SelectedComponent = vm.Components.Single(c => Equals(c.Component, BarRebarComponent.Asw1));
                dialog.UpdateLayout();

                Assert.Equal(System.Windows.Visibility.Collapsed, minColumn.Visibility);
                dialog.Close();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
    }

    sealed class Rebar : IBarRebarFieldSource
    {
        public bool Supports(BarRebarComponent component) =>
            component is not (BarRebarComponent.Bottom or BarRebarComponent.Top);

        public PlateRebarValue Get(string elemTag, BarRebarComponent component) => PlateRebarValue.Of(9);

        public IReadOnlyList<PlateRebarValue> GetSections(string elemTag, BarRebarComponent component) =>
            component == BarRebarComponent.Asw1
                ? [PlateRebarValue.Failure(274), PlateRebarValue.Of(4)]
                : [PlateRebarValue.Of(3), PlateRebarValue.Of(8)];
    }

    /// <summary>Заданная арматура: на КЭ 7 низ 6, верх 4 см²; постоянна по длине.</summary>
    sealed class Assigned : IBarRebarFieldSource
    {
        public bool Supports(BarRebarComponent component) =>
            component is BarRebarComponent.LongitudinalSum or BarRebarComponent.Bottom or BarRebarComponent.Top;

        public PlateRebarValue Get(string elemTag, BarRebarComponent component) => PlateRebarValue.Of(component switch
        {
            BarRebarComponent.Bottom => 6,
            BarRebarComponent.Top => 4,
            _ => 10,
        });

        public IReadOnlyList<PlateRebarValue> GetSections(string elemTag, BarRebarComponent component) => [Get(elemTag, component)];
    }

    [Fact]
    public void AssignedKind_ComparedWithSelectedWhereComponentIsCommon()
    {
        var chain = BarChains.Build([new BarChainBar(7, 1, 2, (0, 0, 0), (4, 0, 0))]);
        var vm = new FemBarDiagramVM("Балка Б1", chain, [], new Rebar(), "1-lin.asp", new Assigned(), "1-lin.RBT");

        Assert.Equal([FemBarDiagramKind.Rebar, FemBarDiagramKind.Assigned], vm.Kinds.Select(k => k.Kind));
        vm.SelectedKind = vm.Kinds.Single(k => k.Kind == FemBarDiagramKind.Assigned);

        Assert.Equal(
            [BarRebarComponent.LongitudinalSum, BarRebarComponent.Bottom, BarRebarComponent.Top],
            vm.Components.Select(c => c.Component));
        // ΣAs есть и в подборе: заданная 10 на всём КЭ, подобранная 3 и 8 по сечениям.
        Assert.True(vm.ComparesWithSelected);
        Assert.Equal([new BarDiagramSegment(0, 4, 10, 10)], vm.Series.Upper);
        Assert.Equal([new BarDiagramSegment(0, 2, 3, 3), new BarDiagramSegment(2, 4, 8, 8)], vm.Series.Lower);
        Assert.Equal([(10.0, 3.0), (10.0, 8.0)], vm.Series.Points.Select(p => (p.Max!.Value, p.Min!.Value)));
        Assert.Contains("1-lin.RBT", vm.Title);
        Assert.Contains("1-lin.asp", vm.Title);

        // Ряда у нижней грани в подборе нет — только заданная.
        vm.SelectedComponent = vm.Components.Single(c => Equals(c.Component, BarRebarComponent.Bottom));
        Assert.False(vm.ComparesWithSelected);
        Assert.False(vm.HasEnvelope);
        Assert.Equal([new BarDiagramSegment(0, 4, 6, 6)], vm.Series.Upper);
    }

    [Fact]
    public void RebarKind_StepDiagramAndFailedSectionInRows()
    {
        var chain = BarChains.Build([new BarChainBar(7, 1, 2, (0, 0, 0), (4, 0, 0))]);
        var set = new ForceSet { Tag = "РСУ (C)", Kind = "bar", Items = [Row(7, 1, 5), Row(7, 1, -2), Row(7, 2, 1)] };
        var vm = new FemBarDiagramVM("Балка Б1", chain, [set], new Rebar(), "1-lin.asp");

        // Две строки в сечении 1 — огибающая.
        Assert.True(vm.HasEnvelope);
        Assert.Equal([new BarDiagramSegment(0, 4, -2, 1)], vm.Series.Lower);

        vm.SelectedKind = vm.Kinds.Single(k => k.Kind == FemBarDiagramKind.Rebar);

        Assert.False(vm.IsForces);
        Assert.False(vm.HasEnvelope);
        Assert.Equal(BarRebarComponent.LongitudinalSum, vm.SelectedComponent!.Component);
        Assert.Equal([new BarDiagramSegment(0, 2, 3, 3), new BarDiagramSegment(2, 4, 8, 8)], vm.Series.Upper);
        Assert.Contains("1-lin.asp", vm.Title);

        vm.SelectedComponent = vm.Components.Single(c => Equals(c.Component, BarRebarComponent.Asw1));

        Assert.Equal([new BarDiagramSegment(2, 4, 4, 4)], vm.Series.Upper);
        Assert.Equal(274, vm.Series.Points[0].FailureCode);
        Assert.Equal(["1", "2"], vm.Rows.Select(r => r.Section));
    }

    [Fact]
    public void UtilizationKind_CheckAndSourceChoice_ReferenceLineAndNotChecked()
    {
        var chain = BarChains.Build([new BarChainBar(7, 1, 2, (0, 0, 0), (4, 0, 0))]);
        var check = new FemBarDiagramCheck(new FemCheck { Tag = "rc", NormCode = "rc_check" }, "Балки / rc_check",
        [
            new FemCheckRowResult(7, 1, "section", 0.6, true, false),
            new FemCheckRowResult(7, 2, "section", null, false, true),
            new FemCheckRowResult(7, 1, "selected", 1.2, false, false),
            new FemCheckRowResult(7, 2, "selected", 0.8, true, false),
        ]);
        var vm = new FemBarDiagramVM("Балка Б1", chain, [], null, null, checks: [check]);

        Assert.Equal([FemBarDiagramKind.Utilization], vm.Kinds.Select(k => k.Kind));
        Assert.True(vm.IsUtilization);
        Assert.Same(check, vm.SelectedCheck);
        Assert.Equal(["section", "selected"], vm.Components.Select(c => (string)c.Component));
        Assert.Equal([new BarDiagramSegment(0, 2, 0.6, 0.6)], vm.Series.Upper);
        Assert.Equal([new BarDiagramSegment(0, 4, 1, 1)], vm.ReferenceLine);
        Assert.False(vm.HasEnvelope);
        Assert.Equal(Utilites.Loc.S("MosaicNotChecked"), vm.Rows[1].Value);
        Assert.Contains("Балки / rc_check", vm.Title);

        vm.SelectedComponent = vm.Components[1];
        Assert.Equal([new BarDiagramSegment(0, 2, 1.2, 1.2), new BarDiagramSegment(2, 4, 0.8, 0.8)], vm.Series.Upper);
    }
}
