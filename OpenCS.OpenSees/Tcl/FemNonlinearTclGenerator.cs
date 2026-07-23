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
        if (model.DistributedLoads.Count > 0 && model.GeomTransfKind == "Corotational")
            throw new InvalidOperationException("Распределённые нагрузки не поддерживаются для 3D forceBeamColumn с geomTransf Corotational.");
        foreach (var ld in model.DistributedLoads)
        {
            if (IsFullUniform(ld))
                L($"    eleLoad -ele {ld.ElementTag} -type -beamUniform {F(ld.WyStart)} {F(ld.WzStart)} {F(ld.WxStart)}");
            else
                L($"    eleLoad -ele {ld.ElementTag} -type -beamUniform {F(ld.WyStart)} {F(ld.WzStart)} {F(ld.WxStart)} {F(ld.AOverL)} {F(ld.BOverL)} {F(ld.WyEnd)} {F(ld.WzEnd)} {F(ld.WxEnd)}");
        }
        L("}");
        L();

        L("constraints Transformation");
        L("numberer RCM");
        L("system BandGeneral");
        L($"test {model.ConvergenceTest} {F(model.Tolerance)} {model.MaxIterations} 0");
        L("algorithm Newton");
        // Интегратор должен быть задан до создания StaticAnalysis.
        L("integrator LoadControl 1.0");
        L("analysis Static");
        L();

        var nodeTags = model.Nodes.Select(n => n.Tag).ToList();
        var restrainedTags = model.Nodes.Where(n => n.Fixed.Any(f => f)).Select(n => n.Tag).ToList();
        var elemTags = model.Elements.Select(e => e.Tag).ToList();

        // Используем Tcl-каналы вместо recorder Node/Element. В OpenSees 3.8.0
        // большие серии eleResponse-запросов к fiber-секциям иногда оставляют
        // нулевые байты в DataFileStream, из-за чего теряется вся строка шага.
        L("set nonlinearNodeDisp [open nonlinear_node_disp.out w]");
        if (restrainedTags.Count > 0)
            L("set nonlinearNodeReactions [open nonlinear_node_reactions.out w]");
        L("set nonlinearElementForces [open nonlinear_element_forces.out w]");
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

        // Сохраняем фактические положения точек интегрирования forceBeamColumn.
        // integrationPoints возвращает нормированные координаты от узла I; длина берётся
        // из исходной геометрии элемента.
        var nodeByTag = model.Nodes.ToDictionary(n => n.Tag);
        L("set sectionOrder [open nonlinear_section_order.json w]");
        L("puts $sectionOrder \"{\\\"locations\\\":\\[\"");
        L("set sectionLocationFirst 1");
        foreach (var e in model.Elements.OrderBy(e => e.Tag))
        {
            if (!nodeByTag.TryGetValue(e.NodeI, out var ni) || !nodeByTag.TryGetValue(e.NodeJ, out var nj))
                throw new InvalidOperationException($"Элемент {e.Tag}: не найдены узлы для вычисления длины.");
            double dx = nj.X - ni.X;
            double dy = nj.Y - ni.Y;
            double dz = nj.Z - ni.Z;
            double length = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (length <= 0 || !double.IsFinite(length))
                throw new InvalidOperationException($"Элемент {e.Tag}: длина должна быть конечной и положительной.");

            var section = model.Sections[e.SectionTag];
            L($"set ipLocations_{e.Tag} [eleResponse {e.Tag} integrationPoints]");
            L($"for {{set ip 1}} {{$ip <= {e.NumIntegrationPoints}}} {{incr ip}} {{");
            L($"    set xi [lindex $ipLocations_{e.Tag} [expr {{$ip - 1}}]]");
            // forceBeamColumn.integrationPoints возвращает координату вдоль элемента
            // в метрах, а не безразмерную координату.
            L("    set distance $xi");
            L($"    set relative [expr {{$xi / {F(length)}}}]");
            L("    if {$sectionLocationFirst == 0} { puts -nonewline $sectionOrder {,} }");
            L("    set sectionLocationFirst 0");
            L($"    puts $sectionOrder [format {{    {{\"elementTag\":{e.Tag},\"integrationPoint\":%d,\"sectionTag\":{e.SectionTag},\"fiberCount\":{section.Fibers.Count},\"distanceFromElementStartM\":%.17g,\"elementLengthM\":{F(length)},\"relativePosition\":%.17g}}}} $ip $distance $relative]");
            L("}");
        }
        L("puts $sectionOrder \"]}\"");
        L("close $sectionOrder");
        L();

        L("set fiberStates [open nonlinear_fiber_states.out w]");
        L("puts $fiberStates {# step loadFactor elementTag integrationPoint fiberIndex stressPa strain}");
        L("set stepStatus [open step_status.out w]");
        L("puts $stepStatus {# step loadFactor converged isRefinement}");
        L($"set loadFactorStep {F(model.LoadFactorStep)}");
        L($"set maxLoadFactor {F(model.MaxLoadFactor)}");
        L($"set refinementDivisions {model.RefinementDivisions}");
        L("set currentLambda 0.0");
        L("set stepIndex 0");
        L("set analysisFailed 0");
        L("while {$currentLambda < $maxLoadFactor - 1.0e-12} {");
        L("    set targetLambda [expr {min($currentLambda + $loadFactorStep, $maxLoadFactor)}]");
        L("    set increment [expr {$targetLambda - $currentLambda}]");
        L("    integrator LoadControl $increment");
        L("    set rc [analyze 1]");
        L("    if {$rc == 0} {");
        L("        set currentLambda [getTime]");
        L("        incr stepIndex");
        L("        puts $stepStatus [list $stepIndex $currentLambda 1 0]");
        EmitFiberStateWrites(L, model);
        EmitRecorderSnapshot(L, nodeTags, restrainedTags, elemTags);
        L("    } else {");
        L("        set refinedIncrement [expr {($targetLambda - $currentLambda) / $refinementDivisions}]");
        L("        set refinementFailed 0");
        L("        for {set r 1} {$r <= $refinementDivisions} {incr r} {");
        L("            integrator LoadControl $refinedIncrement");
        L("            set refinedRc [analyze 1]");
        L("            if {$refinedRc != 0} {");
        L("                set failedLambda [expr {$currentLambda + $refinedIncrement * ($r - 1)}]");
        L("                puts $stepStatus [list [expr {$stepIndex + 1}] $failedLambda 0 1]");
        L("                set refinementFailed 1");
        L("                set analysisFailed 1");
        L("                break");
        L("            }");
        L("            set currentLambda [getTime]");
        L("            incr stepIndex");
        L("            puts $stepStatus [list $stepIndex $currentLambda 1 1]");
        EmitFiberStateWrites(L, model);
        EmitRecorderSnapshot(L, nodeTags, restrainedTags, elemTags);
        L("        }");
        L("        if {$refinementFailed == 1} {break}");
        L("    }");
        L("}");
        L("close $nonlinearNodeDisp");
        if (restrainedTags.Count > 0)
            L("close $nonlinearNodeReactions");
        L("close $nonlinearElementForces");
        L("close $fiberStates");
        L("close $stepStatus");
        L();

        L("set marker [open completed.marker w]");
        L("puts $marker done");
        L("close $marker");
        L("wipe");

        return sb.ToString();
    }

    static void EmitFiberStateWrites(Action<string> line, FemNonlinearModel model)
    {
        foreach (var e in model.Elements.OrderBy(e => e.Tag))
        {
            var section = model.Sections[e.SectionTag];
            line($"        for {{set ip 1}} {{$ip <= {e.NumIntegrationPoints}}} {{incr ip}} {{");
            line($"            for {{set fiberIndex 0}} {{$fiberIndex < {section.Fibers.Count}}} {{incr fiberIndex}} {{");
            string fiberCoordinates = string.Join(' ', section.Fibers.Select(f => $"{TclNumber.Format(f.Y)} {TclNumber.Format(f.Z)}"));
            line($"                set stressStrain [eleResponse {e.Tag} section $ip fiber [lindex {{{fiberCoordinates}}} [expr {{$fiberIndex * 2}}]] [lindex {{{fiberCoordinates}}} [expr {{$fiberIndex * 2 + 1}}]] stressStrain]");
            line($"                if {{[llength $stressStrain] >= 2}} {{ puts $fiberStates [list $stepIndex $currentLambda {e.Tag} $ip $fiberIndex [lindex $stressStrain 0] [lindex $stressStrain 1]] }}");
            line("            }");
            line("        }");
        }
    }

    static bool IsFullUniform(FemLinearDistributedLoad load) =>
        load.AOverL == 0 && load.BOverL == 1 &&
        load.WyStart == load.WyEnd && load.WzStart == load.WzEnd && load.WxStart == load.WxEnd;

    static void EmitRecorderSnapshot(Action<string> line, IReadOnlyList<int> nodeTags,
        IReadOnlyList<int> restrainedTags, IReadOnlyList<int> elemTags)
    {
        line("        set nonlinearNodeDispRow [list [getTime]]");
        foreach (int tag in nodeTags)
            for (int dof = 1; dof <= 6; dof++)
                line($"        lappend nonlinearNodeDispRow [lindex [nodeDisp {tag}] {dof - 1}]");
        line("        puts $nonlinearNodeDisp $nonlinearNodeDispRow");

        if (restrainedTags.Count > 0)
        {
            line("        reactions");
            line("        set nonlinearNodeReactionRow [list [getTime]]");
            foreach (int tag in restrainedTags)
                for (int dof = 1; dof <= 6; dof++)
                    line($"        lappend nonlinearNodeReactionRow [lindex [nodeReaction {tag}] {dof - 1}]");
            line("        puts $nonlinearNodeReactions $nonlinearNodeReactionRow");
        }

        line("        set nonlinearElementForceRow [list [getTime]]");
        foreach (int tag in elemTags)
        {
            line($"        foreach nonlinearElementForceValue [eleResponse {tag} localForce] {{");
            line("            lappend nonlinearElementForceRow $nonlinearElementForceValue");
            line("        }");
        }
        line("        puts $nonlinearElementForces $nonlinearElementForceRow");
    }
}
