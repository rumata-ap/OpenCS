using System.Text;
using OpenCS.OpenSees.Analysis;
using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.Tcl;

/// <summary>
/// Генерирует 3D Tcl-модель одного monotonic луча взаимодействия N–Mx–My.
/// </summary>
public sealed class SpatialSectionTclGenerator : ISpatialSectionTclGenerator
{
    /// <inheritdoc />
    public string Generate(OpenSeesSectionModel model, SpatialSectionAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(request);
        model.Validate();
        request.Validate();

        StringBuilder script = new();
        AppendLine(script, "# OpenCS OpenSees spatial section analysis");
        AppendLine(script, "# Units: m, N, Pa, rad/m");
        AppendLine(script, "wipe");
        AppendLine(script, "model basic -ndm 3 -ndf 6");
        AppendLine(script);

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
        AppendLine(script);

        AppendLine(script, "node 1 0 0 0");
        AppendLine(script, "node 2 0 0 0");
        AppendLine(script, "fix 1 1 1 1 1 1 1");
        AppendLine(script, "fix 2 0 1 1 1 0 0");
        AppendLine(script, "element zeroLengthSection 1 1 2 1");
        AppendLine(script, "constraints Penalty 1.0e20 1.0e20");
        AppendLine(script, "numberer Plain");
        AppendLine(script, "system BandGeneral");
        AppendLine(script, "test NormUnbalance 1.0e-8 50 0");
        AppendLine(script, "algorithm Newton");
        AppendLine(script, "analysis Static");
        AppendLine(script);

        AppendLine(script, "set sectionHistory [open section_history.out w]");
        AppendLine(
            script,
            "puts $sectionHistory {# step loadFactor axialForceN openSeesMzNm openSeesMyNm rotationY rotationZ curvatureMagnitude converged residual}");
        AppendLine(script, "recorder Element -file element_history.out -time -ele 1 section 1 force");
        AppendLine(script, "recorder Node -file node_history.out -time -node 2 -dof 1 5 6 disp");
        AppendLine(script);

        AppendLine(script, "pattern Plain 1 Linear {");
        AppendLine(script, "    load 2 ", TclNumber.Format(request.AxialForceN), " 0 0 0 0 0");
        AppendLine(script, "}");
        AppendLine(script, "integrator LoadControl 1.0");
        AppendLine(script, "set axialRc [analyze 1]");
        AppendLine(script, "if {$axialRc != 0} {");
        AppendLine(script, "    puts $sectionHistory {0 1 0 0 0 0 0 0 0 0}");
        AppendLine(script, "    close $sectionHistory");
        AppendLine(script, "    set marker [open completed.marker w]");
        AppendLine(script, "    puts $marker failed");
        AppendLine(script, "    close $marker");
        AppendLine(script, "    wipe");
        AppendLine(script, "    exit 2");
        AppendLine(script, "}");
        AppendLine(script, "loadConst -time 0.0");
        AppendLine(script);

        AppendLine(script, "pattern Plain 2 Linear {");
        AppendLine(script, "    sp 2 5 ", TclNumber.Format(request.CurvatureMyAtMax));
        AppendLine(script, "    sp 2 6 ", TclNumber.Format(request.CurvatureMxAtMax));
        AppendLine(script, "}");
        AppendLine(script, "integrator LoadControl ", TclNumber.Format(1.0 / request.Increments));
        AppendLine(script, "set step 0");
        AppendLine(script, "for {set i 1} {$i <= ", request.Increments.ToString(), "} {incr i} {");
        AppendLine(script, "    set rc [analyze 1]");
        AppendLine(script, "    set step [expr {$step + 1}]");
        AppendLine(script, "    set forces [eleResponse 1 section 1 force]");
        AppendLine(script, "    set axialForce [lindex $forces 0]");
        AppendLine(script, "    set momentMx [lindex $forces 1]");
        AppendLine(script, "    set momentMy [lindex $forces 2]");
        AppendLine(script, "    set curvatureMy [lindex [nodeDisp 2 5] 0]");
        AppendLine(script, "    set curvatureMx [lindex [nodeDisp 2 6] 0]");
        AppendLine(script, "    set rotationY $curvatureMy");
        AppendLine(script, "    set rotationZ $curvatureMx");
        AppendLine(script, "    set curvatureMagnitude [expr {sqrt($curvatureMx * $curvatureMx + $curvatureMy * $curvatureMy)}]");
        AppendLine(script, "    puts $sectionHistory [list $step [getTime] $axialForce $momentMx $momentMy $rotationY $rotationZ $curvatureMagnitude [expr {$rc == 0}] 0.0]");
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
