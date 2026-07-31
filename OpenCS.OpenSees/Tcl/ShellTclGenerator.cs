using System.Text;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tcl;

/// <summary>Генерирует детерминированный Tcl статического shell-расчёта OpenSees.</summary>
public sealed class ShellTclGenerator
{
    /// <summary>Строит script.tcl из валидированной нормализованной shell-модели.</summary>
    public string Generate(ShellOpenSeesModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.Validate();

        var sb = new StringBuilder();
        void L(string line = "") => sb.Append(line).Append('\n');
        string F(double value) => TclNumber.Format(value);

        L("# OpenCS OpenSees shell static analysis");
        L("# Units: m, N, Pa");
        L("wipe");
        L("model Basic -ndm 3 -ndf 6");
        L();

        foreach (NormalizedShellNode node in model.Nodes.OrderBy(node => node.Tag))
            L($"node {node.Tag} {F(node.X)} {F(node.Y)} {F(node.Z)}");
        L();

        foreach (NormalizedShellNode node in model.Nodes.OrderBy(node => node.Tag))
            L($"fix {node.Tag} {string.Join(' ', node.Fixed.Select(fixedDof => fixedDof ? 1 : 0))}");
        L();

        // Материалы могут ссылаться друг на друга по tag (PlateRebar → uniaxial,
        // PlateFromPlaneStress → базовый plane-stress материал) — топологический порядок
        // эмиссии, не порядок по Tag (зависимость может иметь БОЛЬШИЙ tag, чем зависимый от неё
        // материал).
        foreach (NativeShellMaterialDefinition material in TopologicalOrder(model.Materials))
        {
            foreach (string auxiliary in material.Spec.AuxiliaryCommands)
                L(auxiliary);
            L(material.Spec.ToTcl(material.Tag));
        }
        L();

        foreach (KeyValuePair<int, OpenSeesSectionModel> beamSection in model.NonlinearBeamSections.OrderBy(kv => kv.Key))
            foreach (OpenSeesMaterialDefinition mat in beamSection.Value.Materials)
                L(NativeMaterialTclEmitter.ToTcl(mat, F));
        L();

        foreach (RCShellLayeredSection section in model.Sections.OrderBy(section => section.Tag))
        {
            if (section.MappingMode is ShellMappingMode.Blocked or ShellMappingMode.ReferenceOnly)
                throw new InvalidOperationException(
                    $"Секция {section.Tag} имеет mapping mode {section.MappingMode} и не может быть сгенерирована для native LayeredShell.");
            if (section.Layers.Count < 3)
                throw new InvalidOperationException(
                    $"Секция {section.Tag}: native LayeredShell требует минимум три слоя.");

            string layerArgs = string.Join(' ', section.Layers
                .OrderBy(layer => layer.Index)
                .SelectMany(layer => new[] { layer.MaterialTag.ToString(), F(layer.Thickness) }));
            L($"section LayeredShell {section.Tag} {section.Layers.Count} {layerArgs}");
        }
        foreach (KeyValuePair<int, OpenSeesSectionModel> beamSection in model.NonlinearBeamSections.OrderBy(kv => kv.Key))
        {
            L($"section Fiber {beamSection.Key} -GJ {F(beamSection.Value.GJ)} {{");
            foreach (OpenSeesFiber fiber in beamSection.Value.Fibers)
                L($"    fiber {F(fiber.Y)} {F(fiber.Z)} {F(fiber.AreaM2)} {fiber.MaterialTag}");
            L("}");
        }
        L();

        foreach (NormalizedShellElement element in model.Elements.OrderBy(element => element.Tag))
        {
            string nodes = string.Join(' ', element.NodeTags);
            string command = element.Kind == ShellElementKind.ASDShellQ4
                ? $"element ASDShellQ4 {element.Tag} {nodes} {element.SectionTag}"
                : $"element ASDShellT3 {element.Tag} {nodes} {element.SectionTag}";
            if (element.Kind == ShellElementKind.ASDShellT3 &&
                element.IntegrationPolicy == ShellIntegrationPolicy.Reduced)
                command += " -reducedIntegration";
            command += $" -local {F(element.Frame.Ex.X)} {F(element.Frame.Ex.Y)} {F(element.Frame.Ex.Z)}";
            L(command);
        }
        L();

        var beamTransfByVec = new Dictionary<(double X, double Y, double Z, string Kind), int>();
        int NextTransf((double, double, double) vecxz, string kind)
        {
            var key = (vecxz.Item1, vecxz.Item2, vecxz.Item3, kind);
            if (!beamTransfByVec.TryGetValue(key, out int tag))
            {
                tag = beamTransfByVec.Count + 1;
                beamTransfByVec[key] = tag;
                L($"geomTransf {kind} {tag} {F(vecxz.Item1)} {F(vecxz.Item2)} {F(vecxz.Item3)}");
            }
            return tag;
        }
        foreach (FemLinearElement beam in model.BeamElements.OrderBy(beam => beam.Tag))
        {
            int transf = NextTransf(beam.Vecxz, "Linear");
            L($"element elasticBeamColumn {beam.Tag} {beam.NodeI} {beam.NodeJ} {F(beam.A)} {F(beam.E)} {F(beam.G)} {F(beam.J)} {F(beam.Iy)} {F(beam.Iz)} {transf}");
        }
        foreach (FemNonlinearElement beam in model.NonlinearBeamElements.OrderBy(beam => beam.Tag))
        {
            int transf = NextTransf(beam.Vecxz, model.NonlinearBeamGeomTransfKind);
            L($"element {model.NonlinearBeamElementFormulation} {beam.Tag} {beam.NodeI} {beam.NodeJ} {beam.NumIntegrationPoints} {beam.SectionTag} {transf}");
        }
        L();

        foreach (ShellEqualDofConstraint constraint in model.EqualDofConstraints.OrderBy(c => (c.MasterNode, c.SlaveNode)))
            L($"equalDOF {constraint.MasterNode} {constraint.SlaveNode} {string.Join(' ', constraint.Dofs)}");
        foreach (ShellRigidLinkConstraint constraint in model.RigidLinks.OrderBy(c => (c.MasterNode, c.SlaveNode)))
            L($"rigidLink {(constraint.Type == ShellRigidLinkType.Bar ? "bar" : "beam")} {constraint.MasterNode} {constraint.SlaveNode}");
        L();

        L("constraints Transformation");
        L("numberer RCM");
        L("system BandGeneral");
        L($"test {model.Policy.ConvergenceTest} {F(model.Policy.Tolerance)} {model.Policy.MaxIterations} 0");
        L($"algorithm {model.Policy.Algorithm}");
        L("analysis Static");
        L();

        int[] nodeTags = model.Nodes.OrderBy(n => n.Tag).Select(n => n.Tag).ToArray();
        int[] restrainedTags = model.Nodes.Where(n => n.Fixed.Any(f => f)).OrderBy(n => n.Tag).Select(n => n.Tag).ToArray();
        int[] shellElementTags = model.Elements.OrderBy(e => e.Tag).Select(e => e.Tag).ToArray();
        int[] nonlinearBeamTags = model.NonlinearBeamElements.OrderBy(e => e.Tag).Select(e => e.Tag).ToArray();

        L("proc writeCloseOnWrite {filename row} {");
        L("    set ch [open $filename a]");
        L("    puts $ch $row");
        L("    close $ch");
        L("}");
        L($"recorder Node -file shell_node_disp.out -closeOnWrite -time -node {string.Join(' ', nodeTags)} -dof 1 2 3 4 5 6 disp");
        if (restrainedTags.Length > 0)
            L($"recorder Node -file shell_node_reactions.out -closeOnWrite -time -node {string.Join(' ', restrainedTags)} -dof 1 2 3 4 5 6 reaction");
        if (shellElementTags.Length > 0)
            L($"recorder Element -file shell_element_forces.out -closeOnWrite -time -ele {string.Join(' ', shellElementTags)} force");
        if (nonlinearBeamTags.Length > 0)
            L($"recorder Element -file shell_beam_element_forces.out -closeOnWrite -time -ele {string.Join(' ', nonlinearBeamTags)} localForce");
        L();

        // Один recorder на КАЖДЫЙ индекс точки интегрирования (1..max), включающий все
        // элементы, у которых есть эта точка (IntegrationPointCount >= point) — Q4=4, T3
        // full=3, T3 reduced=1. Группировка строго по exact IntegrationPointCount давала бы
        // коллизию имён файлов между группами (у обеих групп есть "точка 1"); порог "хотя бы
        // N точек" даёт один файл на индекс точки без коллизий.
        int maxIpCount = model.Elements.Count > 0 ? model.Elements.Max(e => e.IntegrationPointCount) : 0;
        var sectionForceGroups = new List<(int Point, int[] ElementTags, string FileName)>();
        for (int point = 1; point <= maxIpCount; point++)
        {
            int[] tags = model.Elements.Where(e => e.IntegrationPointCount >= point)
                .OrderBy(e => e.Tag).Select(e => e.Tag).ToArray();
            if (tags.Length == 0) continue;
            string fileName = $"shell_section_forces_ip{point}.out";
            L($"recorder Element -file {fileName} -closeOnWrite -time -ele {string.Join(' ', tags)} material {point} force");
            sectionForceGroups.Add((point, tags, fileName));
        }
        L();

        string sectionForceGroupsJson = string.Join(',', sectionForceGroups.Select(g =>
            "{\"point\":" + g.Point + ",\"elementTags\":[" + string.Join(',', g.ElementTags) +
            "],\"file\":\"" + g.FileName + "\"}"));
        string orderJson = "{\"nodeTags\":[" + string.Join(',', nodeTags) +
            "],\"restrainedTags\":[" + string.Join(',', restrainedTags) +
            "],\"shellElementTags\":[" + string.Join(',', shellElementTags) +
            "],\"nonlinearBeamElementTags\":[" + string.Join(',', nonlinearBeamTags) +
            "],\"sectionForceGroups\":[" + sectionForceGroupsJson + "]}";
        L("set orderFile [open recorder_order.json w]");
        L("puts $orderFile {" + orderJson + "}");
        L("close $orderFile");
        L();

        L("writeCloseOnWrite step_status.out {# step stageIndex loadFactor converged isRefinement}");
        L($"set refinementDivisions {model.Policy.RefinementDivisions}");
        L($"set maxRefinementDepth {model.Policy.MaxRefinementDepth}");
        L("set currentLambda 0.0");
        L("set stepIndex 0");
        L("set analysisFailed 0");
        L("set currentStageIndex 0");
        L();

        L("proc advanceTo {fromLambda toLambda depth} {");
        L("    global refinementDivisions maxRefinementDepth stepIndex currentLambda currentStageIndex");
        L("    integrator LoadControl [expr {$toLambda - $fromLambda}]");
        L("    set rc [analyze 1]");
        L("    if {$rc == 0} {");
        L("        incr stepIndex");
        L("        set currentLambda [getTime]");
        L("        writeCloseOnWrite step_status.out [list $stepIndex $currentStageIndex $currentLambda 1 [expr {$depth > 0}]]");
        L("        return 1");
        L("    }");
        L("    if {$depth >= $maxRefinementDepth} { return 0 }");
        L("    set piece [expr {($toLambda - $fromLambda) / double($refinementDivisions)}]");
        L("    for {set i 0} {$i < $refinementDivisions} {incr i} {");
        L("        set subFrom [expr {$fromLambda + $piece * $i}]");
        L("        set subTo [expr {$fromLambda + $piece * ($i + 1)}]");
        L("        if {![advanceTo $subFrom $subTo [expr {$depth + 1}]]} { return 0 }");
        L("    }");
        L("    return 1");
        L("}");
        L();

        for (int stageIdx = 0; stageIdx < model.Stages.Count; stageIdx++)
        {
            ShellNonlinearStage stage = model.Stages[stageIdx];
            int patternTag = stageIdx + 1;
            bool guarded = stageIdx > 0;
            string indent = guarded ? "    " : "";
            if (guarded)
            {
                // Стадия >0 выполняется только если все предыдущие стадии сошлись; loadConst
                // фиксирует накопленное НДС перед активацией добавочной нагрузки этой стадии.
                L("if {!$analysisFailed} {");
                L($"{indent}loadConst -time 0.0");
            }
            L($"{indent}# --- Стадия {patternTag}: {stage.Tag} ---");
            L($"{indent}set currentStageIndex {stageIdx}");
            L($"{indent}set loadFactorStep {F(stage.LoadFactorStep)}");
            L($"{indent}set maxLoadFactor {F(stage.MaxLoadFactor)}");
            L($"{indent}pattern Plain {patternTag} Linear {{");
            foreach (ShellNodalLoad load in stage.Loads.OrderBy(load => load.NodeTag))
                L($"{indent}    load {load.NodeTag} {F(load.Fx)} {F(load.Fy)} {F(load.Fz)} {F(load.Mx)} {F(load.My)} {F(load.Mz)}");
            L($"{indent}}}");
            L($"{indent}set currentLambda 0.0");
            L($"{indent}while {{$currentLambda < $maxLoadFactor - 1.0e-12}} {{");
            L($"{indent}    set targetLambda [expr {{min($currentLambda + $loadFactorStep, $maxLoadFactor)}}]");
            L($"{indent}    set fromLambda $currentLambda");
            L($"{indent}    if {{![advanceTo $fromLambda $targetLambda 0]}} {{");
            L($"{indent}        set currentLambda [getTime]");
            L($"{indent}        writeCloseOnWrite step_status.out [list [expr {{$stepIndex + 1}}] $currentStageIndex $currentLambda 0 1]");
            L($"{indent}        set analysisFailed 1");
            L($"{indent}        break");
            L($"{indent}    }}");
            L($"{indent}}}");
            if (guarded) L("}");
            L();
        }

        L("set marker [open completed.marker w]");
        L("puts $marker done");
        L("close $marker");
        L("wipe");

        return sb.ToString();
    }

    /// <summary>Возвращает материалы в порядке, где зависимость (DependsOnMaterialTag)
    /// эмитируется раньше материала, который на неё ссылается — произвольная глубина цепочки
    /// (алгоритм Кана по слоям готовности).</summary>
    private static IEnumerable<NativeShellMaterialDefinition> TopologicalOrder(
        IReadOnlyList<NativeShellMaterialDefinition> materials)
    {
        var remaining = materials.OrderBy(material => material.Tag).ToList();
        var emitted = new HashSet<int>();
        var ordered = new List<NativeShellMaterialDefinition>(materials.Count);

        while (remaining.Count > 0)
        {
            List<NativeShellMaterialDefinition> ready = remaining
                .Where(material => material.Spec.DependsOnMaterialTag is not int dependsOn || emitted.Contains(dependsOn))
                .OrderBy(material => material.Tag)
                .ToList();

            if (ready.Count == 0)
                throw new InvalidOperationException(
                    "Обнаружена неразрешимая или циклическая зависимость shell-материалов.");

            foreach (NativeShellMaterialDefinition material in ready)
            {
                ordered.Add(material);
                emitted.Add(material.Tag);
                remaining.Remove(material);
            }
        }

        return ordered;
    }
}
