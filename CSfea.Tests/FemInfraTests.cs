using CScore;
using CScore.Fem;

namespace CSfea.Tests;

public static class FemInfraTests
{
    public static void RunAll()
    {
        TestHarness.Section("FemInfra: domain model");

        var schema = new FemSchema { Tag = "Рама", SourceType = "lira" };
        TestHarness.Check("FemSchema.Tag",        schema.Tag        == "Рама");
        TestHarness.Check("FemSchema.SourceType", schema.SourceType == "lira");

        var node = new FemNode { NodeTag = "N1", X = 1.0, Y = 2.0, Z = 0.0, DofMask = 63 };
        TestHarness.Check("FemNode.NodeTag",  node.NodeTag  == "N1");
        TestHarness.Check("FemNode.DofMask",  node.DofMask  == 63);

        var elem = new FemElement { ElemTag = "E1", ElemType = "beam" };
        TestHarness.Check("FemElement.ElemType", elem.ElemType == "beam");

        var member = new FemMember { Tag = "К1", MemberType = "column" };
        var p = new FemDesignParams { DesignLengthX = 4.5, MuX = 0.7, GammaM = 1.025 };
        member.DesignParamsJson = p.ToJson();
        var p2 = FemDesignParams.Parse(member.DesignParamsJson);
        TestHarness.CheckRel("FemDesignParams L0x roundtrip", p2.DesignLengthX, 4.5, 1e-9);
        TestHarness.CheckRel("FemDesignParams MuX roundtrip", p2.MuX,           0.7, 1e-9);

        var check = new FemCheck { NormCode = "steel_check", MemberId = 1, SchemaId = 1 };
        TestHarness.Check("FemCheck.NormCode", check.NormCode == "steel_check");

        var fs = new ForceSet { Tag = "РСУ_01", SourceType = "fea", SourceSchemaId = 7,
                                SourceElementTag = "К1" };
        TestHarness.Check("ForceSet.SourceType",       fs.SourceType       == "fea");
        TestHarness.Check("ForceSet.SourceSchemaId",   fs.SourceSchemaId   == 7);
        TestHarness.Check("ForceSet.SourceElementTag", fs.SourceElementTag == "К1");
    }
}
