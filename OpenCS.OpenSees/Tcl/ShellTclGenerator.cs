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

        L("pattern Plain 1 Linear {");
        foreach (ShellNodalLoad load in model.Stages.SelectMany(s => s.Loads).OrderBy(load => load.NodeTag))
            L($"    load {load.NodeTag} {F(load.Fx)} {F(load.Fy)} {F(load.Fz)} {F(load.Mx)} {F(load.My)} {F(load.Mz)}");
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

        string nodeTags = string.Join(' ', model.Nodes.OrderBy(node => node.Tag).Select(node => node.Tag));
        string restrainedTags = string.Join(' ', model.Nodes
            .Where(node => node.Fixed.Any(fixedDof => fixedDof))
            .OrderBy(node => node.Tag)
            .Select(node => node.Tag));
        string elementTags = string.Join(' ', model.Elements.OrderBy(element => element.Tag).Select(element => element.Tag));
        L($"set shell_node_tags {{{nodeTags}}}");
        L($"set shell_restrained_tags {{{restrainedTags}}}");
        L($"set shell_element_tags {{{elementTags}}}");
        L("reactions");
        L("set nf [open node_disp.out w]");
        L("foreach n $shell_node_tags { puts $nf \"$n [nodeDisp $n 1] [nodeDisp $n 2] [nodeDisp $n 3] [nodeDisp $n 4] [nodeDisp $n 5] [nodeDisp $n 6]\" }");
        L("close $nf");
        L("set rf [open node_reactions.out w]");
        L("foreach n $shell_restrained_tags { puts $rf \"$n [nodeReaction $n 1] [nodeReaction $n 2] [nodeReaction $n 3] [nodeReaction $n 4] [nodeReaction $n 5] [nodeReaction $n 6]\" }");
        L("close $rf");
        L("set ef [open element_forces.out w]");
        L("foreach e $shell_element_tags { puts $ef \"$e [eleResponse $e force]\" }");
        L("close $ef");
        L("set shell_section_forces [open section_forces.out w]");
        foreach (NormalizedShellElement element in model.Elements.OrderBy(element => element.Tag))
            for (int point = 1; point <= element.IntegrationPointCount; point++)
                L($"puts $shell_section_forces \"{element.Tag} {point} [eleResponse {element.Tag} material {point} force]\"");
        L("close $shell_section_forces");
        L("set marker [open completed.marker w]");
        L("puts $marker $ok");
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
