namespace CScore.Fem.Editing;

public sealed class AddLoadCaseCommand(FemLoadCase loadCase) : IFemEditCommand
{
    public void Do(FemSchemaEditSession session)
    {
        if (loadCase.Id == 0) loadCase.Id = session.AllocateTemporaryLoadCaseId();
        session.LoadCases.Add(loadCase);
    }
    public void Undo(FemSchemaEditSession session) => session.LoadCases.Remove(loadCase);
}

/// <summary>Копирует изменяемые поля newValues поверх target (Id/SchemaId не трогаются). Обратимо.</summary>
public sealed class EditLoadCaseCommand(FemLoadCase target, FemLoadCase newValues) : IFemEditCommand
{
    FemLoadCase _old = new();

    public void Do(FemSchemaEditSession session)
    {
        _old = Snapshot(target);
        Apply(target, newValues);
    }

    public void Undo(FemSchemaEditSession session) => Apply(target, _old);

    static FemLoadCase Snapshot(FemLoadCase source) => new()
    {
        Tag = source.Tag, LoadType = source.LoadType, Sp20Type = source.Sp20Type,
        Sp20Group = source.Sp20Group, GammaFUnfav = source.GammaFUnfav, GammaFFav = source.GammaFFav,
        Psi1 = source.Psi1, Psi2 = source.Psi2
    };

    static void Apply(FemLoadCase target, FemLoadCase source)
    {
        target.Tag = source.Tag;
        target.LoadType = source.LoadType;
        target.Sp20Type = source.Sp20Type;
        target.Sp20Group = source.Sp20Group;
        target.GammaFUnfav = source.GammaFUnfav;
        target.GammaFFav = source.GammaFFav;
        target.Psi1 = source.Psi1;
        target.Psi2 = source.Psi2;
    }
}

public sealed class DeleteLoadCaseCommand(FemLoadCase loadCase) : IFemEditCommand
{
    List<FemNodeLoad> _removedLoads = [];
    List<FemMemberLoad> _removedMemberLoads = [];

    public void Do(FemSchemaEditSession session)
    {
        session.LoadCases.Remove(loadCase);
        _removedLoads = session.NodeLoads.Where(l => l.LoadCaseId == loadCase.Id).ToList();
        foreach (var l in _removedLoads) session.NodeLoads.Remove(l);
        _removedMemberLoads = session.MemberLoads.Where(l => l.LoadCaseId == loadCase.Id).ToList();
        foreach (var l in _removedMemberLoads) session.MemberLoads.Remove(l);
    }

    public void Undo(FemSchemaEditSession session)
    {
        session.LoadCases.Add(loadCase);
        foreach (var l in _removedLoads) session.NodeLoads.Add(l);
        foreach (var l in _removedMemberLoads) session.MemberLoads.Add(l);
    }
}

/// <summary>Добавляет сохранённое определение одиночного загружения или сочетания.</summary>
public sealed class AddLoadDefinitionCommand(FemLoadDefinition definition) : IFemEditCommand
{
    public void Do(FemSchemaEditSession session) => session.LoadDefinitions.Add(definition);
    public void Undo(FemSchemaEditSession session) => session.LoadDefinitions.Remove(definition);
}

/// <summary>Удаляет определение нагрузки вместе с возможностью отмены операции.</summary>
public sealed class DeleteLoadDefinitionCommand(FemLoadDefinition definition) : IFemEditCommand
{
    public void Do(FemSchemaEditSession session) => session.LoadDefinitions.Remove(definition);
    public void Undo(FemSchemaEditSession session) => session.LoadDefinitions.Add(definition);
}

/// <summary>Заменяет изменяемые поля определения нагрузки, сохраняя идентификатор и схему.</summary>
public sealed class EditLoadDefinitionCommand(FemLoadDefinition target, FemLoadDefinition values) : IFemEditCommand
{
    FemLoadDefinition? _old;

    public void Do(FemSchemaEditSession session)
    {
        _old ??= Snapshot(target);
        Apply(target, values);
    }

    public void Undo(FemSchemaEditSession session)
    {
        if (_old != null) Apply(target, _old);
    }

    static FemLoadDefinition Snapshot(FemLoadDefinition source) => new()
    {
        Tag = source.Tag, Description = source.Description, ExpressionJson = source.ExpressionJson,
        SourceKind = source.SourceKind, CombinationType = source.CombinationType
    };

    static void Apply(FemLoadDefinition target, FemLoadDefinition source)
    {
        target.Tag = source.Tag;
        target.Description = source.Description;
        target.ExpressionJson = source.ExpressionJson;
        target.SourceKind = source.SourceKind;
        target.CombinationType = source.CombinationType;
    }
}

/// <summary>Создаёт или обновляет единственную FemNodeLoad для пары (loadCaseId, nodeId). Обратимо.</summary>
public sealed class SetNodeLoadCommand(
    int loadCaseId, int nodeId,
    double fx, double fy, double fz, double mx, double my, double mz) : IFemEditCommand
{
    FemNodeLoad? _existing;
    FemNodeLoad? _old;
    FemNodeLoad? _created;

    public void Do(FemSchemaEditSession session)
    {
        _existing = session.NodeLoads.FirstOrDefault(l => l.LoadCaseId == loadCaseId && l.NodeId == nodeId);
        if (_existing != null)
        {
            _old = new FemNodeLoad
            {
                Fx = _existing.Fx, Fy = _existing.Fy, Fz = _existing.Fz,
                Mx = _existing.Mx, My = _existing.My, Mz = _existing.Mz
            };
            Apply(_existing);
        }
        else
        {
            _created = new FemNodeLoad
            {
                SchemaId = session.Schema.Id,
                LoadCaseId = loadCaseId,
                NodeId = nodeId
            };
            Apply(_created);
            session.NodeLoads.Add(_created);
        }
    }

    public void Undo(FemSchemaEditSession session)
    {
        if (_existing != null && _old != null)
        {
            _existing.Fx = _old.Fx; _existing.Fy = _old.Fy; _existing.Fz = _old.Fz;
            _existing.Mx = _old.Mx; _existing.My = _old.My; _existing.Mz = _old.Mz;
        }
        else if (_created != null)
        {
            session.NodeLoads.Remove(_created);
        }
    }

    void Apply(FemNodeLoad target)
    {
        target.Fx = fx; target.Fy = fy; target.Fz = fz;
        target.Mx = mx; target.My = my; target.Mz = mz;
    }
}

public sealed class DeleteNodeLoadCommand(FemNodeLoad load) : IFemEditCommand
{
    public void Do(FemSchemaEditSession session) => session.NodeLoads.Remove(load);
    public void Undo(FemSchemaEditSession session) => session.NodeLoads.Add(load);
}

/// <summary>Создаёт или обновляет распределённую нагрузку конструктивного стержня. Обратимо.</summary>
public sealed class SetMemberLoadCommand(FemMemberLoad values) : IFemEditCommand
{
    FemMemberLoad? _existing;
    FemMemberLoad? _old;
    FemMemberLoad? _created;

    public void Do(FemSchemaEditSession session)
    {
        _existing = session.MemberLoads.FirstOrDefault(load => load.Id == values.Id && values.Id != 0);
        if (_existing != null)
        {
            _old = Snapshot(_existing);
            Apply(_existing, values);
        }
        else
        {
            _created = Snapshot(values);
            _created.SchemaId = session.Schema.Id;
            session.MemberLoads.Add(_created);
        }
    }

    public void Undo(FemSchemaEditSession session)
    {
        if (_existing != null && _old != null)
            Apply(_existing, _old);
        else if (_created != null)
            session.MemberLoads.Remove(_created);
    }

    static FemMemberLoad Snapshot(FemMemberLoad source) => new()
    {
        Id = source.Id, SchemaId = source.SchemaId, LoadCaseId = source.LoadCaseId,
        MemberId = source.MemberId, CoordinateSystem = source.CoordinateSystem,
        DistributionType = source.DistributionType, StartOffsetM = source.StartOffsetM,
        EndOffsetM = source.EndOffsetM, QxStart = source.QxStart, QyStart = source.QyStart,
        QzStart = source.QzStart, QxEnd = source.QxEnd, QyEnd = source.QyEnd, QzEnd = source.QzEnd
    };

    static void Apply(FemMemberLoad target, FemMemberLoad source)
    {
        target.LoadCaseId = source.LoadCaseId;
        target.MemberId = source.MemberId;
        target.CoordinateSystem = source.CoordinateSystem;
        target.DistributionType = source.DistributionType;
        target.StartOffsetM = source.StartOffsetM;
        target.EndOffsetM = source.EndOffsetM;
        target.QxStart = source.QxStart;
        target.QyStart = source.QyStart;
        target.QzStart = source.QzStart;
        target.QxEnd = source.QxEnd;
        target.QyEnd = source.QyEnd;
        target.QzEnd = source.QzEnd;
    }
}

/// <summary>Удаляет распределённую нагрузку с возможностью отмены.</summary>
public sealed class DeleteMemberLoadCommand(FemMemberLoad load) : IFemEditCommand
{
    public void Do(FemSchemaEditSession session) => session.MemberLoads.Remove(load);
    public void Undo(FemSchemaEditSession session) => session.MemberLoads.Add(load);
}
