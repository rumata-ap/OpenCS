using CScore;
using CScore.Fem;
using OpenCS.Services;
using OpenCS.Views;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Подготовка проверки по КЭ: наборы усилий цели и представление результата.</summary>
public class FemCheckContextTests
{
    static FemElement Shell(string tag, string? memberTag) =>
        new() { ElemTag = tag, ElemType = "shell", SourceMemberTag = memberTag };

    static ShellLoadItem Row(int? elem) => new() { Label = $"э.{elem}", SourceElementNum = elem };

    /// <summary>
    /// Усилия импортированы на группу «ПЛИТА №56», проверка нацелена на элемент из того же кБ:
    /// набор группы должен попасть в наборы элемента — по строкам его КЭ.
    /// </summary>
    [Fact]
    public void TargetForceSets_MemberSeesSchemaSetsWithRowsOfItsElements()
    {
        var slab = new FemMember { Id = 5, SchemaId = 1, ElemTag = "ПЛИТА №56", ElemType = "shell" };
        var group = new FemMemberGroup { Id = 9, SchemaId = 1, Tag = "ПЛИТА №56", MemberTagsJson = "[10,11,12]" };
        FemElement[] mesh = [Shell("10", "ПЛИТА №56"), Shell("11", "ПЛИТА №56"), Shell("12", null)];
        var scope = FemCheckScope.ForMember(slab, mesh);

        var ofGroup = new ForceSet { Id = 1, Kind = "shell", SourceSchemaId = 1, SourceMemberId = group.Id,
                                     ShellItems = [Row(10), Row(10), Row(11), Row(12)] };
        var otherBlock = new ForceSet { Id = 2, Kind = "shell", SourceSchemaId = 1, SourceMemberId = 77, ShellItems = [Row(40)] };
        var otherSchema = new ForceSet { Id = 3, Kind = "shell", SourceSchemaId = 2, ShellItems = [Row(10)] };
        var ownManual = new ForceSet { Id = 4, Kind = "shell", SourceElementId = slab.Id, ShellItems = [Row(null)] };
        // id группы другой схемы может совпасть с id элемента — привязка группы элементу не подходит.
        var sameIdGroup = new ForceSet { Id = 6, Kind = "shell", SourceSchemaId = 2, SourceMemberId = slab.Id, ShellItems = [Row(null)] };

        var sets = FemCheckContext.TargetForceSets([ofGroup, otherBlock, otherSchema, ownManual, sameIdGroup],
            slab, schemaId: 1, scope, isPlate: true);

        Assert.Equal([(ofGroup, 3), (ownManual, 1)], sets);
    }

    /// <summary>
    /// У элемента из кБ своего пластинчатого сечения нет, оно задано на группе кБ с теми же КЭ —
    /// проверка элемента берёт сечение группы (и наоборот: группа — сечение элемента своих КЭ).
    /// </summary>
    [Fact]
    public void TargetPlateSectionId_IsInheritedFromRelatedTargetWithSameElements()
    {
        var slab = new FemMember { Id = 5, SchemaId = 1, ElemTag = "ПЛИТА №56", ElemType = "shell" };
        FemElement[] mesh = [Shell("10", "ПЛИТА №56"), Shell("11", "ПЛИТА №56"), Shell("12", null)];
        var ofBlock = new FemMemberGroup { Id = 9, Tag = "ПЛИТА №56 [этаж]", MemberTagsJson = "[10,11]", PlateSectionId = 3 };
        var wider = new FemMemberGroup { Id = 10, Tag = "Все плиты", MemberTagsJson = "[10,40,41]", PlateSectionId = 4 };
        var other = new FemMemberGroup { Id = 11, Tag = "СТЕНА №7", MemberTagsJson = "[40]", PlateSectionId = 8 };
        var noSection = new FemMemberGroup { Id = 12, Tag = "Без сечения", MemberTagsJson = "[10,11]" };

        int? id = FemCheckContext.TargetPlateSectionId(slab, FemCheckScope.ForMember(slab, mesh),
            [other, wider, noSection, ofBlock], out string? from);
        Assert.Equal((3, "ПЛИТА №56 [этаж]"), (id, from));

        Assert.Null(FemCheckContext.TargetPlateSectionId(slab, FemCheckScope.ForMember(slab, mesh), [other, noSection], out from));
        Assert.Null(from);

        // Собственное сечение цели важнее.
        slab.PlateSectionId = 6;
        Assert.Equal(6, FemCheckContext.TargetPlateSectionId(slab, FemCheckScope.ForMember(slab, mesh), [ofBlock], out from));
        Assert.Null(from);

        // Группа без сечения — берёт у элемента, которому принадлежат её КЭ.
        id = FemCheckContext.TargetPlateSectionId(noSection, FemCheckScope.ForGroup(noSection, [slab], mesh), [], out from);
        Assert.Equal((6, "ПЛИТА №56"), (id, from));
    }

    [Fact]
    public void TargetForceSets_GroupKeepsItsOwnSetsEvenWithoutElementNumbers()
    {
        var group = new FemMemberGroup { Id = 9, SchemaId = 1, Tag = "Балки", MemberTagsJson = "[1,2]" };
        var scope = FemCheckScope.ForGroup(group, [],
            [new FemElement { ElemTag = "1" }, new FemElement { ElemTag = "2" }]);
        var rsu2 = new ForceSet { Id = 1, SourceMemberId = group.Id, Items = [new LoadItem(), new LoadItem()] };

        var sets = FemCheckContext.TargetForceSets([rsu2], group, schemaId: 1, scope, isPlate: false);

        Assert.Equal((rsu2, 2), Assert.Single(sets));
    }

    [Fact]
    public void ResultVM_ReadsPerElementResult()
    {
        var check = new FemCheck { NormCode = "rc_check", Tag = "балки" };
        var group = new FemMemberGroup { Tag = "Балки", MemberTagsJson = "[1,2,3]" };
        var section = new CrossSection { Id = 1, Tag = "Б1" };
        var scope = new FemCheckScope([],
        [
            new FemCheckScopeElement(1, new FemElement { ElemTag = "1" }, null),
            new FemCheckScopeElement(2, new FemElement { ElemTag = "2" }, null),
            new FemCheckScopeElement(3, new FemElement { ElemTag = "3" }, null),
        ], RefersToMeshElements: true);
        var fs = new ForceSet
        {
            Id = 1, Tag = "РСУ (C)",
            Items =
            [
                new LoadItem { Label = "э.1 с1", N = 0.4, SourceElementNum = 1, SourceSectionNum = 1 },
                new LoadItem { Label = "э.2 с1", N = 1.2, SourceElementNum = 2, SourceSectionNum = 1 },
                new LoadItem { Label = "э.9 с1", N = 0.1, SourceElementNum = 9 },
            ],
        };
        var result = FemCheckRunner.RunPerElement(check, group, scope, [fs],
            new FemPerElementInputs { TargetBarSection = section },
            (_, _, item) => new CalcResult
            {
                Status = item.N <= 1 ? "ok" : "not_passed",
                DataJson = System.Text.Json.JsonSerializer.Serialize(new { utilization = item.N }),
            });

        RunSta(() =>
        {
            var vm = new FemCheckResultVM(result.DataJson);

            Assert.True(vm.IsPerElement);
            Assert.Equal(["bar"], vm.SourceKeys);
            var elements = vm.Elements.Cast<FemCheckElementRowVM>().ToList();
            // Порядок: не прошедший, прошедший, без усилий.
            Assert.Equal(["2", "1", "3"], elements.Select(e => e.ElemText));
            Assert.Equal(["failed", "ok", "not_checked"], elements.Select(e => e.State));
            Assert.Equal(1.2.ToString("F3"), elements[0].Utils["bar"]);
            Assert.Equal("Б1", elements[0].SectionLabel);
            Assert.Equal(2, vm.Rows.Cast<FemCheckRowVM>().Count());

            vm.OnlyFailed = true;
            Assert.Equal(["2", "3"], vm.Elements.Cast<FemCheckElementRowVM>().Select(e => e.ElemText));
            vm.OnlyFailed = false;
            vm.SelectedElement = elements[1];
            Assert.Equal("э.1 с1", Assert.Single(vm.Rows.Cast<FemCheckRowVM>()).Label);
        });
    }

    [Fact]
    public void ResultVM_ReadsLegacyResultWithoutElements()
    {
        const string json = """
            {"normCode":"steel_check","memberTag":"Б1","totalRows":2,"passedRows":1,"failedRows":1,
             "rows":[{"label":"1","forceSetTag":"РСУ","calcType":"C","utilization":0.5,"passed":true,"worstFormula":"(41)","worstDescription":"изгиб"},
                     {"label":"2","forceSetTag":"РСУ","calcType":"C","utilization":null,"passed":false,"worstFormula":"error","worstDescription":"сбой"}]}
            """;

        RunSta(() =>
        {
            var vm = new FemCheckResultVM(json);

            Assert.False(vm.IsPerElement);
            var rows = vm.Rows.Cast<FemCheckRowVM>().ToList();
            Assert.Equal(["2", "1"], rows.Select(r => r.Label));
            Assert.Equal(["failed", "ok"], rows.Select(r => r.State));
            Assert.Empty(vm.Elements.Cast<object>());
        });
    }

    /// <summary>Вид результата создаётся для обоих форматов: столбцы Кисп по источникам добавляются в коде.</summary>
    [Theory]
    [InlineData("""{"normCode":"rc_plate_check","totalRows":0,"passedRows":0,"failedRows":0,"rows":[]}""", 0)]
    [InlineData("""
        {"normCode":"rc_plate_check","perElement":true,"totalRows":0,"passedRows":0,"failedRows":0,"notCheckedRows":0,"skippedRows":0,
         "rebarSources":["assigned","selected"],
         "summary":{"elementsTotal":1,"elementsWithForces":0,"elementsWithoutForces":"7","rowsWithoutElement":0,
                    "sources":[{"source":"assigned","passed":0,"failed":0,"notChecked":1},{"source":"selected","passed":0,"failed":0,"notChecked":1}],
                    "warnings":["предупреждение"]},
         "elements":[{"elemNum":7,"elemTag":"7","rebarSource":"assigned","status":"no_forces","rows":0,"notChecked":0,"sectionLabel":"ТЗА 1"},
                     {"elemNum":7,"elemTag":"7","rebarSource":"selected","status":"no_rebar","reason":"КЭ нет в файле ASP","rows":0,"notChecked":0,"sectionLabel":"ASP"}],
         "rows":[]}
        """, 2)]
    public void ResultView_IsCreatedForBothFormats(string json, int utilColumns)
    {
        RunSta(() =>
        {
            var view = new FemCheckResultView(new CalcResult { DataJson = json });

            var grid = (System.Windows.Controls.DataGrid)view.FindName("ElementsGrid");
            Assert.Equal(10 + utilColumns, grid.Columns.Count);
            var vm = (FemCheckResultVM)view.DataContext;
            if (utilColumns > 0)
            {
                var element = Assert.Single(vm.Elements.Cast<FemCheckElementRowVM>());
                Assert.Equal("ТЗА 1 / ASP", element.SectionLabel);
                Assert.Equal("not_checked", element.State);
                Assert.Contains("предупреждение", vm.DetailsText);
            }
        });
    }

    /// <summary>Представления коллекций WPF создаются в STA-потоке.</summary>
    static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw new Xunit.Sdk.XunitException(error.ToString());
    }
}
