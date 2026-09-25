using CScore.Import;
using Xunit;

namespace CScore.Tests;

/// <summary>Имена групп из конструктивных блоков ЛИРА содержат номер блока — иначе одноимённые блоки неразличимы.</summary>
public class LiraConstructiveBlockGroupTagTests
{
    [Fact]
    public void GroupTag_ContainsLiraBlockNumber()
    {
        var data = new LiraSchemaData();
        data.ConstructiveBlocks.Add(new LiraConstructiveBlockRecord(5, "СТЕНА", "1-й этаж", "", "", [11998, 11999]));
        data.ConstructiveBlocks.Add(new LiraConstructiveBlockRecord(27, "Блок", "", "", "", [1]));
        data.ConstructiveBlocks.Add(new LiraConstructiveBlockRecord(28, "Блок", "", "К-1", "", [2]));

        var groups = LiraSchemaConverter.ToFemMemberGroupsByConstructiveBlocks(data, schemaId: 1);

        Assert.Equal(["СТЕНА №5 [1-й этаж]", "Блок №27", "Блок №28 К-1"], groups.Select(g => g.Tag));
        Assert.Equal("[1]", groups[1].MemberTagsJson);
    }
}
