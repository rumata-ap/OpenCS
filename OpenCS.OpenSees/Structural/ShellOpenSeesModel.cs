using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.Structural;

/// <summary>Нормализованная shell-модель OpenSees без UI и persistence-зависимостей.</summary>
public sealed record ShellOpenSeesModel
{
    /// <summary>Узлы модели.</summary>
    public IReadOnlyList<NormalizedShellNode> Nodes { get; init; } = [];

    /// <summary>Native shell-материалы модели.</summary>
    public IReadOnlyList<NativeShellMaterialDefinition> Materials { get; init; } = [];

    /// <summary>Слоистые shell-секции модели.</summary>
    public IReadOnlyList<RCShellLayeredSection> Sections { get; init; } = [];

    /// <summary>Оболочечные элементы модели.</summary>
    public IReadOnlyList<NormalizedShellElement> Elements { get; init; } = [];

    /// <summary>Стадии пропорционального нагружения (минимум одна).</summary>
    public IReadOnlyList<ShellNonlinearStage> Stages { get; init; } = [];

    /// <summary>Fiber-секции нелинейных стержней (общее тег-пространство с Sections —
    /// see Validate). Переиспользует OpenSeesSectionModel из FemNonlinearModel.</summary>
    public IReadOnlyDictionary<int, OpenSeesSectionModel> NonlinearBeamSections { get; init; } =
        new Dictionary<int, OpenSeesSectionModel>();

    /// <summary>Нелинейные fiber-стержни (forceBeamColumn/dispBeamColumn), с общими узлами с
    /// остальной моделью — основа смешанной плитно-стержневой схемы.</summary>
    public IReadOnlyList<FemNonlinearElement> NonlinearBeamElements { get; init; } = [];

    /// <summary>"forceBeamColumn" (по умолчанию) | "dispBeamColumn".</summary>
    public string NonlinearBeamElementFormulation { get; init; } = "forceBeamColumn";

    /// <summary>"Linear" (по умолчанию) | "PDelta" | "Corotational".</summary>
    public string NonlinearBeamGeomTransfKind { get; init; } = "Linear";

    /// <summary>Геометрическая нелинейность shell-элементов: включает -corotational у
    /// ASDShellQ4/ASDShellT3. Независим от NonlinearBeamGeomTransfKind — стержни и оболочки
    /// смешанной модели могут иметь разную кинематику.</summary>
    public bool ShellCorotational { get; init; } = false;

    /// <summary>Enhanced Assumed Strain у ASDShellQ4 (default true = поведение OpenSees без
    /// флага). false эмитит -noeas. Не применяется к ASDShellT3 — у элемента нет такой опции,
    /// поэтому false на T3 не эмитит ничего (тихий no-op, не ошибка).</summary>
    public bool EnhancedAssumedStrain { get; init; } = true;

    /// <summary>Единая на модель политика drilling-стабилизации shell-элементов.</summary>
    public DrillingPolicy Drilling { get; init; } = new();

    /// <summary>Политика записи material states слоёв оболочки и fibers стержней.</summary>
    public ShellStateRecordingPolicy MaterialStateRecording { get; init; } = new();

    /// <summary>Политика нелинейного Newton-анализа. Дефолт подтверждён вручную на реальном
    /// OpenSees 3.8.0 (срез 1) для shell-материалов.</summary>
    public NonlinearAnalysisPolicy Policy { get; init; } = new()
    {
        Tolerance = 1e-6, MaxIterations = 25, ConvergenceTest = "NormDispIncr",
        Algorithm = "Newton", RefinementDivisions = 10, MaxRefinementDepth = 4
    };

    /// <summary>Стержневые (beam) элементы, разделяющие узлы с shell-моделью.</summary>
    public IReadOnlyList<FemLinearElement> BeamElements { get; init; } = [];

    /// <summary>Связи equalDOF между узлами модели.</summary>
    public IReadOnlyList<ShellEqualDofConstraint> EqualDofConstraints { get; init; } = [];

    /// <summary>Жёсткие связи rigidLink между узлами модели.</summary>
    public IReadOnlyList<ShellRigidLinkConstraint> RigidLinks { get; init; } = [];

    /// <summary>Проверяет topology, geometry, materials и section mappings.</summary>
    public void Validate()
    {
        if (Nodes.Count == 0)
            throw new InvalidOperationException("Shell-модель не содержит узлов.");
        if (Sections.Count == 0)
            throw new InvalidOperationException("Shell-модель не содержит секций.");
        if (Elements.Count == 0)
            throw new InvalidOperationException("Shell-модель не содержит элементов.");

        var nodes = new Dictionary<int, NormalizedShellNode>();
        foreach (NormalizedShellNode node in Nodes)
        {
            node.Validate();
            if (!nodes.TryAdd(node.Tag, node))
                throw new InvalidOperationException($"Дублирующийся tag shell-узла {node.Tag}.");
        }

        var materials = new Dictionary<int, NativeShellMaterialDefinition>();
        foreach (NativeShellMaterialDefinition material in Materials)
        {
            material.Validate();
            if (!materials.TryAdd(material.Tag, material))
                throw new InvalidOperationException($"Дублирующийся tag shell-материала {material.Tag}.");
        }

        var sections = new Dictionary<int, RCShellLayeredSection>();
        foreach (RCShellLayeredSection section in Sections)
        {
            section.Validate();
            if (!sections.TryAdd(section.Tag, section))
                throw new InvalidOperationException($"Дублирующийся tag shell-секции {section.Tag}.");
            foreach (RCShellLayer layer in section.Layers)
                if (!materials.ContainsKey(layer.MaterialTag))
                    throw new InvalidOperationException(
                        $"Секция {section.Tag}, слой {layer.Index} ссылается на неизвестный material tag {layer.MaterialTag}.");
        }

        var elements = new HashSet<int>();
        foreach (NormalizedShellElement element in Elements)
        {
            if (!elements.Add(element.Tag))
                throw new InvalidOperationException($"Дублирующийся tag shell-элемента {element.Tag}.");
            element.Validate(nodes, sections.Keys.ToHashSet());
            if (!HasPositiveArea(element, nodes))
                throw new InvalidOperationException($"Элемент {element.Tag}: площадь geometry должна быть положительной.");
            if (!string.Equals(element.SectionFingerprint, sections[element.SectionTag].Fingerprint,
                               StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Элемент {element.Tag}: fingerprint секции не совпадает с секцией {element.SectionTag}.");
        }

        if (Stages.Count == 0)
            throw new InvalidOperationException("Shell-модель не содержит ни одной стадии нагружения.");
        foreach (ShellNonlinearStage stage in Stages)
        {
            if (!double.IsFinite(stage.LoadFactorStep) || stage.LoadFactorStep <= 0)
                throw new InvalidOperationException($"Стадия «{stage.Tag}»: шаг коэффициента нагрузки должен быть конечным и положительным.");
            if (!double.IsFinite(stage.MaxLoadFactor) || stage.MaxLoadFactor <= 0)
                throw new InvalidOperationException($"Стадия «{stage.Tag}»: максимальный коэффициент нагрузки должен быть конечным и положительным.");
            if (stage.MaxLoadFactor < stage.LoadFactorStep)
                throw new InvalidOperationException($"Стадия «{stage.Tag}»: максимальный коэффициент нагрузки не может быть меньше шага.");
            foreach (ShellNodalLoad load in stage.Loads)
            {
                if (!nodes.ContainsKey(load.NodeTag))
                    throw new InvalidOperationException($"Нагрузка стадии «{stage.Tag}» ссылается на неизвестный узел {load.NodeTag}.");
                if (!double.IsFinite(load.Fx) || !double.IsFinite(load.Fy) || !double.IsFinite(load.Fz) ||
                    !double.IsFinite(load.Mx) || !double.IsFinite(load.My) || !double.IsFinite(load.Mz))
                    throw new InvalidOperationException($"Нагрузка узла {load.NodeTag} стадии «{stage.Tag}»: компоненты должны быть конечны.");
            }

            var kinematicDofs = new HashSet<(int NodeTag, int Dof)>();
            foreach (ShellKinematicLoad load in stage.KinematicLoads)
            {
                if (!nodes.TryGetValue(load.NodeTag, out NormalizedShellNode? node))
                    throw new InvalidOperationException($"Kinematic load стадии «{stage.Tag}» ссылается на неизвестный узел {load.NodeTag}.");
                load.Validate();
                if (!kinematicDofs.Add((load.NodeTag, load.Dof)))
                    throw new InvalidOperationException($"Duplicate kinematic load стадии «{stage.Tag}» для узла {load.NodeTag}, DOF {load.Dof}.");
                if (node.Fixed[load.Dof - 1])
                    throw new InvalidOperationException($"Kinematic load стадии «{stage.Tag}» конфликтует с fixed DOF узла {load.NodeTag}, DOF {load.Dof}.");
            }
        }

        foreach (FemLinearElement beam in BeamElements)
        {
            if (!elements.Add(beam.Tag))
                throw new InvalidOperationException($"Дублирующийся tag элемента {beam.Tag} (shell/beam).");
            if (!nodes.ContainsKey(beam.NodeI) || !nodes.ContainsKey(beam.NodeJ))
                throw new InvalidOperationException($"Beam-элемент {beam.Tag} ссылается на неизвестный узел.");
            if (beam.A <= 0 || beam.E <= 0)
                throw new InvalidOperationException($"Beam-элемент {beam.Tag}: A и E должны быть положительны.");
        }

        var slaveDofCoverage = new Dictionary<(int Node, int Dof), string>();
        void ClaimDof(int node, int dof, string owner)
        {
            var key = (node, dof);
            if (slaveDofCoverage.TryGetValue(key, out string? existingOwner))
                throw new InvalidOperationException(
                    $"Конфликт связей: узел {node}, DOF {dof} одновременно задан «{existingOwner}» и «{owner}».");
            slaveDofCoverage[key] = owner;
        }

        foreach (ShellEqualDofConstraint constraint in EqualDofConstraints)
        {
            if (!nodes.ContainsKey(constraint.MasterNode) || !nodes.ContainsKey(constraint.SlaveNode))
                throw new InvalidOperationException($"equalDOF {constraint.MasterNode}-{constraint.SlaveNode} ссылается на неизвестный узел.");
            if (constraint.MasterNode == constraint.SlaveNode)
                throw new InvalidOperationException($"equalDOF: master и slave совпадают (узел {constraint.MasterNode}).");
            if (constraint.Dofs is null || constraint.Dofs.Count == 0 || constraint.Dofs.Any(dof => dof is < 1 or > 6))
                throw new InvalidOperationException($"equalDOF {constraint.MasterNode}-{constraint.SlaveNode}: DOF должны быть в диапазоне 1..6.");

            string owner = $"equalDOF {constraint.MasterNode}->{constraint.SlaveNode}";
            foreach (int dof in constraint.Dofs)
                ClaimDof(constraint.SlaveNode, dof, owner);
        }

        foreach (ShellRigidLinkConstraint constraint in RigidLinks)
        {
            if (!nodes.ContainsKey(constraint.MasterNode) || !nodes.ContainsKey(constraint.SlaveNode))
                throw new InvalidOperationException($"rigidLink {constraint.MasterNode}-{constraint.SlaveNode} ссылается на неизвестный узел.");
            if (constraint.MasterNode == constraint.SlaveNode)
                throw new InvalidOperationException($"rigidLink: master и slave совпадают (узел {constraint.MasterNode}).");

            string owner = $"rigidLink {constraint.Type} {constraint.MasterNode}->{constraint.SlaveNode}";
            int[] dofs = constraint.Type == ShellRigidLinkType.Bar ? [1, 2, 3] : [1, 2, 3, 4, 5, 6];
            foreach (int dof in dofs)
                ClaimDof(constraint.SlaveNode, dof, owner);
        }

        if (NonlinearBeamElementFormulation is not ("forceBeamColumn" or "dispBeamColumn"))
            throw new InvalidOperationException($"Неизвестная формулировка нелинейного стержня «{NonlinearBeamElementFormulation}».");
        if (NonlinearBeamGeomTransfKind is not ("Linear" or "PDelta" or "Corotational"))
            throw new InvalidOperationException($"Неизвестная формулировка geomTransf нелинейного стержня «{NonlinearBeamGeomTransfKind}».");
        Policy.Validate();
        MaterialStateRecording.Validate();

        Drilling.Validate();
        if (Drilling.Mode == ShellDrillingMode.Stabilization)
        {
            List<int> t3Tags = Elements.Where(e => e.Kind == ShellElementKind.ASDShellT3)
                .Select(e => e.Tag).ToList();
            if (t3Tags.Count > 0)
                throw new InvalidOperationException(
                    $"DrillingPolicy.Stabilization несовместим с ASDShellT3 (нет численного " +
                    $"-drillingStab у T3); конфликтующие теги: {string.Join(", ", t3Tags)}.");
        }

        // Тег-пространство секций — общее для shell (LayeredShell) и нелинейных стержней
        // (Fiber): в реальном OpenSees `section`-команды делят одно пространство тегов
        // независимо от типа. sections (shell) уже заполнен выше в этом методе.
        foreach (int beamSectionTag in NonlinearBeamSections.Keys)
            if (sections.ContainsKey(beamSectionTag))
                throw new InvalidOperationException($"Секция нелинейного стержня {beamSectionTag} конфликтует с shell-секцией того же тега.");

        // Тег-пространство материалов — общее для shell (Materials, уже единый пул
        // nDMaterial+uniaxial с среза 1) и beam-fiber uniaxialMaterial материалов.
        var beamMaterialTags = new HashSet<int>();
        foreach (OpenSeesSectionModel beamSection in NonlinearBeamSections.Values)
            foreach (OpenSeesMaterialDefinition beamMaterial in beamSection.Materials)
            {
                if (materials.ContainsKey(beamMaterial.Tag))
                    throw new InvalidOperationException($"Материал нелинейного стержня {beamMaterial.Tag} конфликтует с shell-материалом того же тега.");
                if (!beamMaterialTags.Add(beamMaterial.Tag))
                    throw new InvalidOperationException($"Дублирующийся tag материала нелинейного стержня {beamMaterial.Tag}.");
            }

        foreach (FemNonlinearElement beam in NonlinearBeamElements)
        {
            if (!elements.Add(beam.Tag))
                throw new InvalidOperationException($"Дублирующийся tag элемента {beam.Tag} (нелинейный стержень).");
            if (!nodes.ContainsKey(beam.NodeI) || !nodes.ContainsKey(beam.NodeJ))
                throw new InvalidOperationException($"Нелинейный стержень {beam.Tag} ссылается на неизвестный узел.");
            if (!NonlinearBeamSections.ContainsKey(beam.SectionTag))
                throw new InvalidOperationException($"Нелинейный стержень {beam.Tag} ссылается на неизвестную секцию {beam.SectionTag}.");
            if (beam.NumIntegrationPoints <= 0)
                throw new InvalidOperationException($"Нелинейный стержень {beam.Tag}: число точек интегрирования должно быть положительным.");
        }
    }

    private static bool HasPositiveArea(
        NormalizedShellElement element,
        IReadOnlyDictionary<int, NormalizedShellNode> nodes)
    {
        ShellVector3 areaVector = ShellVector3.Zero;
        for (int i = 0; i < element.NodeTags.Count; i++)
        {
            NormalizedShellNode current = nodes[element.NodeTags[i]];
            NormalizedShellNode next = nodes[element.NodeTags[(i + 1) % element.NodeTags.Count]];
            var currentPoint = new ShellVector3(current.X, current.Y, current.Z);
            var nextPoint = new ShellVector3(next.X, next.Y, next.Z);
            areaVector += currentPoint.Cross(nextPoint);
        }

        return 0.5 * areaVector.Length > 1e-12;
    }
}
