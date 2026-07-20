using System.Text;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tcl;

/// <summary>Генерирует Tcl линейного статического расчёта 3D-стержневой схемы (ndm 3, ndf 6).</summary>
public sealed class FemLinearTclGenerator
{
    /// <summary>Строит текст script.tcl из типизированной линейной модели.</summary>
    public string Generate(FemLinearModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.Validate();

        var sb = new StringBuilder();
        void L(string s = "") => sb.Append(s).Append('\n');
        string F(double v) => TclNumber.Format(v);

        L("# OpenCS OpenSees линейный расчёт FEM-схемы");
        L("# Units: m, N, Pa");
        L("wipe");
        L("model basic -ndm 3 -ndf 6");
        L();

        foreach (var n in model.Nodes)
            L($"node {n.Tag} {F(n.X)} {F(n.Y)} {F(n.Z)}");
        L();

        foreach (var n in model.Nodes)
            L($"fix {n.Tag} {string.Join(' ', n.Fixed.Select(f => f ? 1 : 0))}");
        L();

        // geomTransf Linear по уникальным vecxz
        var transfByVec = new Dictionary<(double, double, double), int>();
        foreach (var e in model.Elements)
            if (!transfByVec.ContainsKey(e.Vecxz))
            {
                int tag = transfByVec.Count + 1;
                transfByVec[e.Vecxz] = tag;
                L($"geomTransf Linear {tag} {F(e.Vecxz.X)} {F(e.Vecxz.Y)} {F(e.Vecxz.Z)}");
            }
        L();

        foreach (var e in model.Elements)
        {
            int t = transfByVec[e.Vecxz];
            L($"element elasticBeamColumn {e.Tag} {e.NodeI} {e.NodeJ} {F(e.A)} {F(e.E)} {F(e.G)} {F(e.J)} {F(e.Iy)} {F(e.Iz)} {t}");
        }
        L();

        L("pattern Plain 1 Linear {");
        foreach (var ld in model.Loads)
            L($"    load {ld.NodeTag} {F(ld.Fx)} {F(ld.Fy)} {F(ld.Fz)} {F(ld.Mx)} {F(ld.My)} {F(ld.Mz)}");
        L("}");
        L();

        L("constraints Transformation");
        L("numberer RCM");
        L("system BandGeneral");
        L("integrator LoadControl 1.0");
        L("algorithm Linear");
        L("analysis Static");
        L("set ok [analyze 1]");
        L();

        // Явная запись результатов
        var nodeTags = string.Join(' ', model.Nodes.Select(n => n.Tag));
        var restrainedTags = string.Join(' ', model.Nodes.Where(n => n.Fixed.Any(f => f)).Select(n => n.Tag));
        var elemTags = string.Join(' ', model.Elements.Select(e => e.Tag));

        L($"set nodeTags {{{nodeTags}}}");
        L($"set restrainedTags {{{restrainedTags}}}");
        L($"set elemTags {{{elemTags}}}");
        L("reactions");
        L("set df [open node_disp.out w]");
        L("foreach n $nodeTags { puts $df \"$n [nodeDisp $n 1] [nodeDisp $n 2] [nodeDisp $n 3] [nodeDisp $n 4] [nodeDisp $n 5] [nodeDisp $n 6]\" }");
        L("close $df");
        L("set rf [open node_reactions.out w]");
        L("foreach n $restrainedTags { puts $rf \"$n [nodeReaction $n 1] [nodeReaction $n 2] [nodeReaction $n 3] [nodeReaction $n 4] [nodeReaction $n 5] [nodeReaction $n 6]\" }");
        L("close $rf");
        L("set ef [open element_forces.out w]");
        L("foreach e $elemTags { puts $ef \"$e [eleResponse $e localForce]\" }");
        L("close $ef");
        L("set marker [open completed.marker w]");
        L("puts $marker $ok");
        L("close $marker");
        L("wipe");

        return sb.ToString();
    }
}
