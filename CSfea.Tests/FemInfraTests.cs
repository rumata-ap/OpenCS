using System.Text.Json;
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

        TestHarness.Section("FemInfra: FemCheckRunner");

        // BuildCalcTask: params fallback from member.DesignParamsJson
        var checkNoParams = new FemCheck { Id = 5, NormCode = "steel_check", MemberId = 1, SchemaId = 1 };
        var memberForTask = new FemMember { Tag = "К2" };
        var dp = new FemDesignParams { DesignLengthX = 6.0, MuX = 1.0 };
        memberForTask.DesignParamsJson = dp.ToJson();
        var task = FemCheckRunner.BuildCalcTask(checkNoParams, memberForTask);
        TestHarness.Check("BuildCalcTask.Kind",       task.Kind == "steel_check");
        TestHarness.Check("BuildCalcTask.Tag prefix", task.Tag.StartsWith("К2/"));
        using var doc = JsonDocument.Parse(task.ParamsJson ?? "{}");
        doc.RootElement.TryGetProperty("DesignLengthX", out var lxEl);
        TestHarness.CheckRel("BuildCalcTask.DesignLengthX from member", lxEl.GetDouble(), 6.0, 1e-9);

        // BuildCalcTask: explicit params on check override member params
        var checkWithParams = new FemCheck
        {
            Id = 6, NormCode = "steel_check", MemberId = 1, SchemaId = 1,
            ParamsJson = new FemDesignParams { DesignLengthX = 9.0 }.ToJson()
        };
        var task2 = FemCheckRunner.BuildCalcTask(checkWithParams, memberForTask);
        using var doc2 = JsonDocument.Parse(task2.ParamsJson ?? "{}");
        doc2.RootElement.TryGetProperty("DesignLengthX", out var lx2El);
        TestHarness.CheckRel("BuildCalcTask.DesignLengthX from check override", lx2El.GetDouble(), 9.0, 1e-9);

        // PickWorst: returns result with higher utilization
        var rLow  = MakeResult(0.4);
        var rHigh = MakeResult(0.9);
        TestHarness.Check("PickWorst(null, r) == r",         FemCheckRunner.PickWorst(null,  rLow)  == rLow);
        TestHarness.Check("PickWorst(low, high) == high",    FemCheckRunner.PickWorst(rLow,  rHigh) == rHigh);
        TestHarness.Check("PickWorst(high, low) == high",    FemCheckRunner.PickWorst(rHigh, rLow)  == rHigh);

        // FemMember.Checks collection (eager-load container)
        var mCol = new FemMember { Tag = "К3" };
        mCol.Checks.Add(new FemCheck { Id = 10, NormCode = "steel_check" });
        TestHarness.Check("FemMember.Checks eager container", mCol.Checks.Count == 1);
    }

    static CalcResult MakeResult(double utilization) => new()
    {
        TaskId   = 0,
        TaskKind = "steel_check",
        TaskTag  = "test",
        Created  = "2026-06-23 00:00:00",
        Status   = "ok",
        DataJson = JsonSerializer.Serialize(new { utilization })
    };
}
