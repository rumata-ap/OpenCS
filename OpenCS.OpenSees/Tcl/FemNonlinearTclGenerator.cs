using System.Text;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tcl;

/// <summary>Генерирует Tcl нелинейного статического расчёта 3D-стержневой схемы (ndm 3, ndf 6):
/// fiber-сечения, forceBeamColumn, по-шаговая история через recorder + явный лог сходимости.</summary>
public sealed class FemNonlinearTclGenerator
{
    /// <summary>Строит текст script.tcl из типизированной нелинейной модели.</summary>
    public string Generate(FemNonlinearModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.Validate();

        var sb = new StringBuilder();
        void L(string s = "") => sb.Append(s).Append('\n');
        string F(double v) => TclNumber.Format(v);

        L("# OpenCS OpenSees нелинейный расчёт FEM-схемы");
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

        // Материалы + fiber-секции, в порядке присвоенных тегов секций
        foreach (var kv in model.Sections.OrderBy(kv => kv.Key))
        {
            int sectionTag = kv.Key;
            var section = kv.Value;
            foreach (var mat in section.Materials)
            {
                // Точки: отрицательная огибающая целиком + положительная без первой (общей) точки —
                // тот же приём, что и в SectionMomentCurvatureTclGenerator.
                var points = mat.NegativeEnvelope.Concat(mat.PositiveEnvelope.Skip(1)).ToList();
                var strains = string.Join(' ', points.Select(p => F(p.Strain)));
                var stresses = string.Join(' ', points.Select(p => F(p.StressPa)));
                L($"uniaxialMaterial ElasticMultiLinear {mat.Tag} -strain {strains} -stress {stresses}");
            }
            L($"section Fiber {sectionTag} -GJ {F(section.GJ)} {{");
            foreach (var fiber in section.Fibers)
                L($"    fiber {F(fiber.Y)} {F(fiber.Z)} {F(fiber.AreaM2)} {fiber.MaterialTag}");
            L("}");
        }
        L();

        // geomTransf по уникальным vecxz
        var transfByVec = new Dictionary<(double, double, double), int>();
        foreach (var e in model.Elements)
            if (!transfByVec.ContainsKey(e.Vecxz))
            {
                int tag = transfByVec.Count + 1;
                transfByVec[e.Vecxz] = tag;
                L($"geomTransf {model.GeomTransfKind} {tag} {F(e.Vecxz.X)} {F(e.Vecxz.Y)} {F(e.Vecxz.Z)}");
            }
        L();

        foreach (var e in model.Elements)
        {
            int t = transfByVec[e.Vecxz];
            L($"element forceBeamColumn {e.Tag} {e.NodeI} {e.NodeJ} {e.NumIntegrationPoints} {e.SectionTag} {t}");
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
        L($"test NormUnbalance {F(model.Tolerance)} {model.MaxIterations} 0");
        L("algorithm Newton");
        L($"integrator LoadControl {F(1.0 / model.LoadSteps)}");
        L("analysis Static");
        L();

        var nodeTags = model.Nodes.Select(n => n.Tag).ToList();
        var restrainedTags = model.Nodes.Where(n => n.Fixed.Any(f => f)).Select(n => n.Tag).ToList();
        var elemTags = model.Elements.Select(e => e.Tag).ToList();

        L($"recorder Node -file nonlinear_node_disp.out -time -node {string.Join(' ', nodeTags)} -dof 1 2 3 4 5 6 disp");
        if (restrainedTags.Count > 0)
            L($"recorder Node -file nonlinear_node_reactions.out -time -node {string.Join(' ', restrainedTags)} -dof 1 2 3 4 5 6 reaction");
        L($"recorder Element -file nonlinear_element_forces.out -time -ele {string.Join(' ', elemTags)} localForce");
        L();

        // recorder_order.json — статический эхо-вывод уже известных на этапе генерации списков тегов,
        // чтобы парсер сопоставлял колонки recorder-матриц без хрупких допущений об их порядке.
        string orderJson = "{\"nodeTags\":[" + string.Join(',', nodeTags) +
            "],\"restrainedTags\":[" + string.Join(',', restrainedTags) +
            "],\"elemTags\":[" + string.Join(',', elemTags) + "]}";
        L("set orderFile [open recorder_order.json w]");
        L("puts $orderFile {" + orderJson + "}");
        L("close $orderFile");
        L();

        L("set stepStatus [open step_status.out w]");
        L("puts $stepStatus {# step loadFactor converged}");
        L("for {set i 1} {$i <= " + model.LoadSteps + "} {incr i} {");
        L("    set rc [analyze 1]");
        L("    puts $stepStatus \"$i [getTime] [expr {$rc == 0}]\"");
        L("    if {$rc != 0} {break}");
        L("}");
        L("close $stepStatus");
        L();

        L("set marker [open completed.marker w]");
        L("puts $marker done");
        L("close $marker");
        L("wipe");

        return sb.ToString();
    }
}
