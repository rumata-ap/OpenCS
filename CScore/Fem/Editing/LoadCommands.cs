namespace CScore.Fem.Editing;

public sealed class AddLoadCaseCommand(FemLoadCase loadCase) : IFemEditCommand
{
    public void Do(FemSchemaEditSession session) => session.LoadCases.Add(loadCase);
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

    public void Do(FemSchemaEditSession session)
    {
        session.LoadCases.Remove(loadCase);
        _removedLoads = session.NodeLoads.Where(l => l.LoadCaseId == loadCase.Id).ToList();
        foreach (var l in _removedLoads) session.NodeLoads.Remove(l);
    }

    public void Undo(FemSchemaEditSession session)
    {
        session.LoadCases.Add(loadCase);
        foreach (var l in _removedLoads) session.NodeLoads.Add(l);
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
            _created = new FemNodeLoad { LoadCaseId = loadCaseId, NodeId = nodeId };
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
