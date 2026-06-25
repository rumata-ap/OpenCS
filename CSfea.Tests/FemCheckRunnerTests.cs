using CScore;
using CScore.Fem;

namespace CSfea.Tests;

public static class FemCheckRunnerTests
{
    static void CheckStr(string name, string actual, string expected)
    {
        bool ok = actual == expected;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}: '{actual}' (expected '{expected}')");
    }

    static void CheckCalcType(string name, CalcType actual, CalcType expected)
    {
        TestHarness.CheckRel(name, (double)(int)actual, (double)(int)expected, 0.01);
    }

    public static void RunExtractCalcType()
    {
        TestHarness.Section("FemCheckRunner: ExtractCalcType из тега набора");

        CheckCalcType("(C)  → C",         FemCheckRunner.ExtractCalcType("Плита — РСН 1 (C)",  null), CalcType.C);
        CheckCalcType("(CL) → CL",        FemCheckRunner.ExtractCalcType("Плита — РСН 1 (CL)", null), CalcType.CL);
        CheckCalcType("(N)  → N",         FemCheckRunner.ExtractCalcType("Плита — РСН 1 (N)",  null), CalcType.N);
        CheckCalcType("(NL) → NL",        FemCheckRunner.ExtractCalcType("Плита — ЗН (NL)",    null), CalcType.NL);
        CheckCalcType("без суффикса → C", FemCheckRunner.ExtractCalcType("Балка — ЗН 01",       null), CalcType.C);
        CheckCalcType("override CL",      FemCheckRunner.ExtractCalcType("любой тег",           "CL"), CalcType.CL);
    }

    public static void RunExtractWorstDetail()
    {
        TestHarness.Section("FemCheckRunner: ExtractWorstDetail из DataJson");

        var json = """
            {"utilization":0.85,"details":[
              {"formula":"8.1.1","description":"Сжатие","ratio":0.4,"passed":true},
              {"formula":"8.1.3","description":"Сжатие с изгибом","ratio":0.85,"passed":true}
            ]}
            """;
        var (f, d) = FemCheckRunner.ExtractWorstDetail(json);
        CheckStr("formula", f, "8.1.3");
        CheckStr("description", d, "Сжатие с изгибом");
    }

    public static void RunExtractWorstDetailNoDetails()
    {
        TestHarness.Section("FemCheckRunner: ExtractWorstDetail — нет details");
        var json = """{"utilization":0.5}""";
        var (f, d) = FemCheckRunner.ExtractWorstDetail(json);
        CheckStr("formula пусто", f, "");
        CheckStr("description пусто", d, "");
    }
}
