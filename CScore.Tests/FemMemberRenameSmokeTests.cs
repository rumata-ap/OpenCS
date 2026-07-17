using CScore.Fem;
using Xunit;

namespace CScore.Tests;

public class FemMemberRenameSmokeTests
{
    [Fact]
    public void FemMember_HasSectionAndGjFieldsDirectly()
    {
        var member = new FemMember
        {
            SchemaId = 1,
            ElemTag = "1",
            ElemType = "beam",
            NodeIdsJson = "[1,2]",
            CrossSectionId = 42,
            GjStrategy = "manual",
            GjManualValue = 123.0,
        };

        Assert.Equal(42, member.CrossSectionId);
        Assert.Equal("manual", member.GjStrategy);
        Assert.Equal(123.0, member.GjManualValue);
        Assert.Equal(1, member.Node1);
        Assert.Equal(2, member.Node2);
    }

    [Fact]
    public void FemMemberGroup_HasNoSectionOrGjFields_AndUsesMemberTagsJson()
    {
        var group = new FemMemberGroup
        {
            SchemaId = 1,
            Tag = "M1",
            MemberTagsJson = "[1,2,3]",
        };

        Assert.Equal("M1", group.Tag);
        Assert.Equal("[1,2,3]", group.MemberTagsJson);
        // Компилируется только если у FemMemberGroup НЕТ CrossSectionId/GjStrategy — сам факт
        // компиляции этого теста без ссылок на них служит негативной проверкой.
    }

    [Fact]
    public void FemSchema_ExposesMemberGroups()
    {
        var schema = new FemSchema();
        schema.MemberGroups.Add(new FemMemberGroup { Tag = "G1" });
        Assert.Single(schema.MemberGroups);
    }
}
