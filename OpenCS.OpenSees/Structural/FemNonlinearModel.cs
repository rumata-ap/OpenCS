using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.Structural;

/// <summary>Типизированная нелинейная 3D-стержневая модель для OpenSees (ndm 3, ndf 6):
/// fiber-сечения + forceBeamColumn + шаги нагрузки с критерием сходимости.</summary>
public sealed class FemNonlinearModel
{
    public IReadOnlyList<FemLinearNode> Nodes { get; init; } = [];
    public IReadOnlyDictionary<int, OpenSeesSectionModel> Sections { get; init; } =
        new Dictionary<int, OpenSeesSectionModel>();
    public IReadOnlyList<FemNonlinearElement> Elements { get; init; } = [];
    /// <summary>Стадии нагружения в порядке приложения (минимум одна). Каждая стадия — добавочный
    /// набор нагрузок поверх зафиксированных предыдущих (см. FemNonlinearTclGenerator).</summary>
    public IReadOnlyList<FemNonlinearStage> Stages { get; init; } = [];

    public string GeomTransfKind { get; init; } = "Linear";
    /// <summary>Формулировка стержневого элемента: "forceBeamColumn" (по умолчанию, force-based —
    /// точнее при распределённой нелинейности с меньшим числом элементов) | "dispBeamColumn"
    /// (displacement-based). При определении состояния forceBeamColumn обращает матрицу ГИБКОСТИ
    /// сечения на каждой точке интегрирования — если все фибры сечения одновременно попадают на
    /// строго горизонтальный участок диаграммы (полностью растрескавшийся бетон при растяжении,
    /// см. MaterialDiagramMapper), эта матрица вырождена и обращение падает с
    /// "could not invert flexibility". dispBeamColumn вместо обращения напрямую интегрирует
    /// жёсткость сечения (без инверсии) — вырожденное сечение просто не вносит вклада, а не
    /// валит расчёт. Эмпирически на реальном кинематическом сценарии с обрывом растяжения в
    /// строгий ноль dispBeamColumn сошёлся дальше и без единой ошибки обращения матрицы, тогда
    /// как forceBeamColumn давал сотни таких ошибок — доступен как выбор на постановке именно
    /// для таких сценариев.</summary>
    public string ElementFormulation { get; init; } = "forceBeamColumn";
    /// <summary>Политика нелинейного Newton-анализа (допуск, критерий сходимости, алгоритм,
    /// адаптивное дробление шага). См. NonlinearAnalysisPolicy — общий тип с ShellOpenSeesModel.</summary>
    public NonlinearAnalysisPolicy Policy { get; init; } = new();
    /// <summary>Имя набора диаграмм CScore, использованного при построении fiber-сечения.</summary>
    public string CalcTypeName { get; init; } = "C";

    /// <summary>Записывать ли состояния (σ, ε) отдельных волокон в nonlinear_fiber_states.out.
    /// См. CalcSettings.OpenSeesRecordFiberStates.</summary>
    public bool RecordFiberStates { get; init; } = true;

    /// <summary>Ограничение записи волоконных состояний конкретными точками интегрирования вдоль
    /// длины КАЖДОГО элемента (1-based номера); null — писать все точки элемента.
    /// См. CalcSettings.OpenSeesFiberStatesIntegrationPoints.</summary>
    public IReadOnlySet<int>? FiberStatesIntegrationPoints { get; init; }

    /// <summary>Проверяет целостность модели перед генерацией Tcl.</summary>
    public void Validate()
    {
        if (Nodes.Count == 0) throw new InvalidOperationException("Модель не содержит узлов.");
        if (Elements.Count == 0) throw new InvalidOperationException("Модель не содержит элементов.");
        if (Sections.Count == 0) throw new InvalidOperationException("Модель не содержит fiber-сечений.");
        if (Stages.Count == 0) throw new InvalidOperationException("Модель не содержит ни одной стадии нагружения.");
        Policy.Validate();
        if (GeomTransfKind is not ("Linear" or "PDelta" or "Corotational"))
            throw new InvalidOperationException($"Неизвестная формулировка geomTransf «{GeomTransfKind}».");
        if (ElementFormulation is not ("forceBeamColumn" or "dispBeamColumn"))
            throw new InvalidOperationException($"Неизвестная формулировка элемента «{ElementFormulation}».");

        var tags = new HashSet<int>();
        foreach (var n in Nodes)
        {
            if (n.Fixed is null || n.Fixed.Length != 6)
                throw new InvalidOperationException($"Узел {n.Tag}: маска закрепления должна иметь 6 флагов.");
            if (!double.IsFinite(n.X) || !double.IsFinite(n.Y) || !double.IsFinite(n.Z))
                throw new InvalidOperationException($"Узел {n.Tag}: неконечные координаты.");
            if (!tags.Add(n.Tag))
                throw new InvalidOperationException($"Дублирующийся тег узла {n.Tag}.");
        }

        var elemTags = new HashSet<int>();
        foreach (var e in Elements)
        {
            if (!elemTags.Add(e.Tag))
                throw new InvalidOperationException($"Дублирующийся тег элемента {e.Tag}.");
            if (!tags.Contains(e.NodeI) || !tags.Contains(e.NodeJ))
                throw new InvalidOperationException($"Элемент {e.Tag} ссылается на несуществующий узел.");
            if (!Sections.ContainsKey(e.SectionTag))
                throw new InvalidOperationException($"Элемент {e.Tag} ссылается на несуществующее сечение {e.SectionTag}.");
            if (e.NumIntegrationPoints <= 0)
                throw new InvalidOperationException($"Элемент {e.Tag}: число точек интегрирования должно быть положительным.");
        }

        var kinematicDofs = new HashSet<(int NodeTag, int Dof)>();
        bool anyDistributed = false, anyPoint = false;
        foreach (var stage in Stages)
        {
            if (!double.IsFinite(stage.LoadFactorStep) || stage.LoadFactorStep <= 0)
                throw new InvalidOperationException($"Стадия «{stage.Tag}»: шаг коэффициента нагрузки должен быть конечным и положительным.");
            if (!double.IsFinite(stage.MaxLoadFactor) || stage.MaxLoadFactor <= 0)
                throw new InvalidOperationException($"Стадия «{stage.Tag}»: максимальный коэффициент нагрузки должен быть конечным и положительным.");
            if (stage.MaxLoadFactor < stage.LoadFactorStep)
                throw new InvalidOperationException($"Стадия «{stage.Tag}»: максимальный коэффициент нагрузки не может быть меньше шага.");

            foreach (var l in stage.Loads)
                if (!tags.Contains(l.NodeTag))
                    throw new InvalidOperationException($"Нагрузка стадии «{stage.Tag}» ссылается на несуществующий узел {l.NodeTag}.");

            foreach (var l in stage.KinematicLoads)
            {
                if (!tags.Contains(l.NodeTag))
                    throw new InvalidOperationException($"Кинематическая нагрузка стадии «{stage.Tag}» ссылается на несуществующий узел {l.NodeTag}.");
                if (l.Dof is < 1 or > 6)
                    throw new InvalidOperationException($"Кинематическая нагрузка стадии «{stage.Tag}» узла {l.NodeTag}: DOF должен быть от 1 до 6.");
                if (!double.IsFinite(l.Value))
                    throw new InvalidOperationException($"Кинематическая нагрузка стадии «{stage.Tag}» узла {l.NodeTag}: значение должно быть конечным.");
                if (!kinematicDofs.Add((l.NodeTag, l.Dof)))
                    throw new InvalidOperationException($"Дублирующееся кинематическое воздействие узла {l.NodeTag}, DOF {l.Dof} (стадия «{stage.Tag}» конфликтует с более ранней стадией).");
                var node = Nodes.First(n => n.Tag == l.NodeTag);
                if (node.Fixed[l.Dof - 1] && l.Value != 0)
                    throw new InvalidOperationException($"Кинематическая нагрузка стадии «{stage.Tag}» узла {l.NodeTag}, DOF {l.Dof} конфликтует с закреплением.");
            }

            foreach (var l in stage.DistributedLoads)
            {
                anyDistributed = true;
                if (!elemTags.Contains(l.ElementTag))
                    throw new InvalidOperationException($"Распределённая нагрузка стадии «{stage.Tag}» ссылается на несуществующий элемент {l.ElementTag}.");
                if (!double.IsFinite(l.WyStart) || !double.IsFinite(l.WzStart) || !double.IsFinite(l.WxStart) ||
                    !double.IsFinite(l.WyEnd) || !double.IsFinite(l.WzEnd) || !double.IsFinite(l.WxEnd))
                    throw new InvalidOperationException($"Распределённая нагрузка стадии «{stage.Tag}» элемента {l.ElementTag}: интенсивности должны быть конечными.");
                if (!double.IsFinite(l.AOverL) || !double.IsFinite(l.BOverL) ||
                    l.AOverL < 0 || l.BOverL > 1 || l.BOverL <= l.AOverL)
                    throw new InvalidOperationException($"Распределённая нагрузка стадии «{stage.Tag}» элемента {l.ElementTag}: некорректный интервал приложения.");
            }

            foreach (var l in stage.PointLoads)
            {
                anyPoint = true;
                if (!elemTags.Contains(l.ElementTag))
                    throw new InvalidOperationException($"Сосредоточенная нагрузка стадии «{stage.Tag}» ссылается на несуществующий элемент {l.ElementTag}.");
                if (!double.IsFinite(l.Py) || !double.IsFinite(l.Pz) || !double.IsFinite(l.Px))
                    throw new InvalidOperationException($"Сосредоточенная нагрузка стадии «{stage.Tag}» элемента {l.ElementTag}: компоненты должны быть конечными.");
                if (!double.IsFinite(l.XOverL) || l.XOverL <= 0 || l.XOverL >= 1)
                    throw new InvalidOperationException($"Сосредоточенная нагрузка стадии «{stage.Tag}» элемента {l.ElementTag}: xL должен быть строго между 0 и 1.");
            }

            ValidatePathControl(stage.PathControl, stage.Tag, tags, Nodes);
        }

        if (GeomTransfKind == "Corotational" && anyDistributed)
            throw new InvalidOperationException("Распределённые нагрузки не поддерживаются для 3D forceBeamColumn с geomTransf Corotational.");
        if (GeomTransfKind == "Corotational" && anyPoint)
            throw new InvalidOperationException("Сосредоточенные нагрузки внутри элемента не поддерживаются для 3D forceBeamColumn с geomTransf Corotational.");
    }

    static void ValidatePathControl(FemPathControlSettings pc, string stageTag, HashSet<int> nodeTags, IReadOnlyList<FemLinearNode> nodes)
    {
        // Enum.IsDefined отклоняет значения вне трёх определённых режимов (например,
        // (FemPathControlMode)99) явной ошибкой — без этой проверки switch ниже пропустил
        // бы такое значение молча через отсутствующую ветку как эквивалент LoadControl.
        if (!Enum.IsDefined(pc.Mode))
            throw new InvalidOperationException($"Стадия «{stageTag}»: неизвестный режим управления траекторией «{pc.Mode}».");

        switch (pc.Mode)
        {
            case FemPathControlMode.DisplacementControl:
                if (pc.DisplacementControl is not { } dc)
                    throw new InvalidOperationException($"Стадия «{stageTag}»: режим DisplacementControl требует заданных параметров.");
                ValidateDisplacementControl(dc, stageTag, nodeTags, nodes);
                if (pc.ArcLength != null)
                    throw new InvalidOperationException($"Стадия «{stageTag}»: заданы параметры ArcLength при режиме DisplacementControl.");
                break;
            case FemPathControlMode.ArcLength:
                if (pc.ArcLength is not { } al)
                    throw new InvalidOperationException($"Стадия «{stageTag}»: режим ArcLength требует заданных параметров.");
                ValidateArcLength(al, stageTag, nodeTags, nodes);
                if (pc.DisplacementControl != null)
                    throw new InvalidOperationException($"Стадия «{stageTag}»: заданы параметры DisplacementControl при режиме ArcLength.");
                break;
            case FemPathControlMode.LoadControl:
                if (pc.DisplacementControl != null || pc.ArcLength != null)
                    throw new InvalidOperationException($"Стадия «{stageTag}»: заданы параметры DisplacementControl/ArcLength при режиме LoadControl.");
                break;
        }

        if (pc.ContinueWithMode is { } continueMode)
        {
            if (!Enum.IsDefined(continueMode))
                throw new InvalidOperationException($"Стадия «{stageTag}»: неизвестный режим продолжения «{continueMode}».");
            if (pc.Mode != FemPathControlMode.LoadControl)
                throw new InvalidOperationException($"Стадия «{stageTag}»: продолжение (continuation) допустимо только для LoadControl.");
            switch (continueMode)
            {
                case FemPathControlMode.DisplacementControl:
                    if (pc.ContinueWithDisplacementControl is not { } cdc)
                        throw new InvalidOperationException($"Стадия «{stageTag}»: продолжение DisplacementControl требует заданных параметров.");
                    ValidateDisplacementControl(cdc, stageTag, nodeTags, nodes);
                    if (pc.ContinueWithArcLength != null)
                        throw new InvalidOperationException($"Стадия «{stageTag}»: заданы параметры продолжения ArcLength при continuation-режиме DisplacementControl.");
                    break;
                case FemPathControlMode.ArcLength:
                    if (pc.ContinueWithArcLength is not { } cal)
                        throw new InvalidOperationException($"Стадия «{stageTag}»: продолжение ArcLength требует заданных параметров.");
                    ValidateArcLength(cal, stageTag, nodeTags, nodes);
                    if (pc.ContinueWithDisplacementControl != null)
                        throw new InvalidOperationException($"Стадия «{stageTag}»: заданы параметры продолжения DisplacementControl при continuation-режиме ArcLength.");
                    break;
                default:
                    throw new InvalidOperationException($"Стадия «{stageTag}»: продолжение LoadControl→LoadControl не имеет смысла.");
            }
        }
        else if (pc.ContinueWithDisplacementControl != null || pc.ContinueWithArcLength != null)
        {
            throw new InvalidOperationException($"Стадия «{stageTag}»: заданы параметры продолжения без ContinueWithMode.");
        }
    }

    static void ValidateDisplacementControl(FemDisplacementControlSettings dc, string stageTag, HashSet<int> nodeTags, IReadOnlyList<FemLinearNode> nodes)
    {
        ValidateControlDof(dc.ControlNodeTag, dc.ControlDof, stageTag, nodeTags, nodes);
        if (!double.IsFinite(dc.InitialIncrement) || !double.IsFinite(dc.MinIncrement) ||
            !double.IsFinite(dc.MaxIncrement) || !double.IsFinite(dc.TargetDisplacement))
            throw new InvalidOperationException($"Стадия «{stageTag}»: параметры DisplacementControl должны быть конечными числами.");
        if (dc.MinIncrement <= 0 || dc.MinIncrement > dc.InitialIncrement || dc.InitialIncrement > dc.MaxIncrement)
            throw new InvalidOperationException($"Стадия «{stageTag}»: должно быть 0 < MinIncrement <= InitialIncrement <= MaxIncrement.");
        if (dc.MaxSteps <= 0)
            throw new InvalidOperationException($"Стадия «{stageTag}»: MaxSteps должен быть положительным.");
    }

    static void ValidateArcLength(FemArcLengthSettings al, string stageTag, HashSet<int> nodeTags, IReadOnlyList<FemLinearNode> nodes)
    {
        ValidateControlDof(al.MonitorNodeTag, al.MonitorDof, stageTag, nodeTags, nodes);
        if (!double.IsFinite(al.S) || !double.IsFinite(al.Alpha) || !double.IsFinite(al.MinS))
            throw new InvalidOperationException($"Стадия «{stageTag}»: параметры ArcLength должны быть конечными числами.");
        if (al.S <= 0 || al.Alpha <= 0 || al.MinS <= 0 || al.MinS > al.S)
            throw new InvalidOperationException($"Стадия «{stageTag}»: должно быть 0 < MinS <= S и Alpha > 0.");
        if (al.MaxSteps <= 0)
            throw new InvalidOperationException($"Стадия «{stageTag}»: MaxSteps должен быть положительным.");
    }

    static void ValidateControlDof(int nodeTag, int dof, string stageTag, HashSet<int> nodeTags, IReadOnlyList<FemLinearNode> nodes)
    {
        if (!nodeTags.Contains(nodeTag))
            throw new InvalidOperationException($"Стадия «{stageTag}»: узел {nodeTag} не найден.");
        if (dof is < 1 or > 6)
            throw new InvalidOperationException($"Стадия «{stageTag}»: DOF должен быть от 1 до 6.");
        var node = nodes.First(n => n.Tag == nodeTag);
        if (node.Fixed[dof - 1])
            throw new InvalidOperationException($"Стадия «{stageTag}»: узел {nodeTag}, DOF {dof} закреплён — не может использоваться как контрольный/мониторинговый.");
    }
}
