using System.Globalization;
using System.Text;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Tests.Fixtures;

namespace OpenCS.OpenSees.Tests.Hysteresis;

/// <summary>
/// Одноосная программа деформаций для материала OpenSees: zeroLength между узлами 1–2 (деформация = ε, усилие = σ),
/// управление по перемещению узла 2. В OpenSees 3.8 Tcl нет testUniaxialMaterial.
/// </summary>
internal static class UniaxialLoopRunner
{
    public static async Task<List<(double Eps, double Sig)>> RunAsync(string materialTcl,
        IReadOnlyList<double> targets, int stepsPerSegment)
    {
        string exe = OpenSeesTestExecutable.ResolveOrSkip();
        string dir = Path.Combine(Path.GetTempPath(), $"opencs-uni-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var inv = CultureInfo.InvariantCulture;
        var tcl = new StringBuilder()
            .AppendLine("model basic -ndm 1 -ndf 1")
            .AppendLine("node 1 0.0").AppendLine("node 2 0.0").AppendLine("fix 1 1")
            .AppendLine(materialTcl)
            .AppendLine("element zeroLength 1 1 2 -mat 1 -dir 1")
            .AppendLine("pattern Plain 1 Linear { load 2 1.0 }")
            .AppendLine("constraints Plain").AppendLine("numberer Plain").AppendLine("system BandGeneral")
            .AppendLine("test NormDispIncr 1.0e-12 50 0").AppendLine("algorithm Newton").AppendLine("analysis Static")
            .AppendLine("set cur 0.0")
            .Append("foreach target {").Append(string.Join(" ", targets.Select(t => t.ToString("R", inv)))).AppendLine("} {")
            .AppendLine($"  set n {stepsPerSegment}")
            .AppendLine("  set du [expr ($target - $cur)/$n]")
            .AppendLine("  integrator DisplacementControl 2 1 $du")
            .AppendLine("  for {set i 0} {$i < $n} {incr i} {")
            .AppendLine("    analyze 1")
            .AppendLine("    puts \"P [nodeDisp 2 1] [lindex [eleResponse 1 force] 1]\"")
            .AppendLine("  }")
            .AppendLine("  set cur $target")
            .AppendLine("}");
        string script = Path.Combine(dir, "uni.tcl");
        File.WriteAllText(script, tcl.ToString());
        var run = await new OpenSeesProcessRunner().RunAsync(new OpenSeesRunRequest
        {
            ExecutablePath = exe, WorkingDirectory = dir, ScriptPath = script, Timeout = TimeSpan.FromSeconds(60),
        }, CancellationToken.None);
        if (run.ExitCode != 0) throw new InvalidOperationException($"OpenSees: {run.Stdout}\n{run.Stderr}");
        return (run.Stdout + "\n" + run.Stderr).Split('\n')
            .Select(l => l.Trim()).Where(l => l.StartsWith("P ", StringComparison.Ordinal))
            .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(p => (double.Parse(p[1], inv), double.Parse(p[2], inv))).ToList();
    }
}
