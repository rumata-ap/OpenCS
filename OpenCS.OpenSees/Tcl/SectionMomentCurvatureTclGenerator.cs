using System.Text;
using OpenCS.OpenSees.Analysis;
using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.Tcl;

/// <summary>Генерирует 2D Tcl-модель одноосного moment–curvature анализа.</summary>
public sealed class SectionMomentCurvatureTclGenerator : IOpenSeesTclGenerator
{
    /// <inheritdoc />
    public string Generate(OpenSeesSectionModel model, SectionAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(request);
        model.Validate();
        request.Validate();

        StringBuilder script = new();
        AppendLine(script, "# OpenCS OpenSees stage 0-1 section analysis");
        AppendLine(script, "# Units: m, N, Pa, rad/m");
        AppendLine(script, "wipe");
        AppendLine(script, "model basic -ndm 2 -ndf 3");
        AppendLine(script, "");

        foreach (OpenSeesMaterialDefinition material in model.Materials)
        {
            IEnumerable<EnvelopePoint> points = material.NegativeEnvelope
                .Concat(material.PositiveEnvelope.Skip(1));
            Append(script, "uniaxialMaterial ElasticMultiLinear ", material.Tag.ToString(), " -strain");
            foreach (EnvelopePoint point in points)
                Append(script, " ", TclNumber.Format(point.Strain));
            Append(script, " -stress");
            foreach (EnvelopePoint point in points)
                Append(script, " ", TclNumber.Format(point.StressPa));
            AppendLine(script);
        }

        AppendLine(script, "section Fiber 1 -GJ ", TclNumber.Format(model.GJ), " {");
        foreach (OpenSeesFiber fiber in model.Fibers)
        {
            AppendLine(
                script,
                "fiber ",
                TclNumber.Format(fiber.Y),
                " ",
                TclNumber.Format(fiber.Z),
                " ",
                TclNumber.Format(fiber.AreaM2),
                " ",
                fiber.MaterialTag.ToString());
        }
        AppendLine(script, "}");
        AppendLine(script, "");

        AppendLine(script, "node 1 0 0");
        AppendLine(script, "node 2 0 0");
        AppendLine(script, "fix 1 1 1 1");
        AppendLine(script, "fix 2 0 1 0");
        AppendLine(script, "element zeroLengthSection 1 1 2 1");
        AppendLine(script, "constraints Penalty 1.0e20 1.0e20 1.0e20");
        AppendLine(script, "numberer Plain");
        AppendLine(script, "system BandGeneral");
        AppendLine(script, "test NormUnbalance 1.0e-8 50 0");
        AppendLine(script, "algorithm Newton");
        AppendLine(script, "analysis Static");
        AppendLine(script, "");

        AppendLine(script, "set sectionHistory [open section_history.out w]");
        AppendLine(script, "puts $sectionHistory {# step loadFactor axialForceN bendingMomentNm axialStrain curvature converged residual}");
        OpenSeesFiber firstFiber = model.Fibers[0];
        AppendLine(
            script,
            "recorder Element -file fiber_history.out -time -ele 1 section 1 fiber ",
            TclNumber.Format(firstFiber.Y),
            " ",
            TclNumber.Format(firstFiber.Z),
            " stressStrain");
        AppendLine(script, "recorder Node -file node_history.out -time -node 2 -dof 1 3 disp");
        AppendLine(script, "");

        AppendLine(script, "pattern Plain 1 Linear {");
        AppendLine(script, "    load 2 ", TclNumber.Format(request.AxialForceN), " 0 0");
        AppendLine(script, "}");
        AppendLine(script, "integrator LoadControl 1.0");
        AppendLine(script, "set axialRc [analyze 1]");
        AppendLine(script, "if {$axialRc != 0} { puts $sectionHistory {0 1 0 0 0 0 0 1}; close $sectionHistory; puts [open completed.marker w] done; wipe; exit 2 }");
        AppendLine(script, "loadConst -time 0.0");
        AppendLine(script, "");

        AppendLine(script, "pattern Plain 2 Linear {");
        AppendLine(script, "    load 2 0 0 1");
        AppendLine(script, "}");
        AppendLine(script, "set curvatureStep ", TclNumber.Format(request.MaxCurvature / request.Increments));
        AppendLine(script, "integrator DisplacementControl 2 3 ", TclNumber.Format(request.MaxCurvature / request.Increments));
        AppendLine(script, "set step 0");
        AppendLine(script, "for {set i 1} {$i <= ", request.Increments.ToString(), "} {incr i} {");
        AppendLine(script, "    set rc [analyze 1]");
        AppendLine(script, "    set step [expr {$step + 1}]");
        AppendLine(script, "    set forces [eleResponse 1 section 1 force]");
        AppendLine(script, "    set axialForce [lindex $forces 0]");
        AppendLine(script, "    set bendingMoment [lindex $forces 1]");
        AppendLine(script, "    set axialStrain [lindex [nodeDisp 2 1] 0]");
        AppendLine(script, "    set curvature [lindex [nodeDisp 2 3] 0]");
        AppendLine(script, "    puts $sectionHistory [list $step [getTime] $axialForce $bendingMoment $axialStrain $curvature [expr {$rc == 0}] 0.0]");
        AppendLine(script, "    if {$rc != 0} {break}");
        AppendLine(script, "}");
        AppendLine(script, "close $sectionHistory");
        AppendLine(script, "set marker [open completed.marker w]");
        AppendLine(script, "puts $marker done");
        AppendLine(script, "close $marker");
        AppendLine(script, "set manifest [open run_manifest.json a]");
        AppendLine(script, "puts $manifest \"{}\"");
        AppendLine(script, "close $manifest");
        AppendLine(script, "wipe");

        return script.ToString();
    }

    private static void Append(StringBuilder builder, params string[] values)
    {
        foreach (string value in values)
            builder.Append(value);
    }

    private static void AppendLine(StringBuilder builder, params string[] values)
    {
        Append(builder, values);
        builder.AppendLine();
    }
}
