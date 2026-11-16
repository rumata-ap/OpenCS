using System.Collections.ObjectModel;
using System.ComponentModel;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Подгруппа расчётных задач по типу; подпись — по ключу строки, перечитывается при смене языка.</summary>
public class CalcTasksSubGroupNode : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string                           GroupKey { get; }
    /// <summary>Ключ строки подписи.</summary>
    public string                           LabelKey { get; }
    public string                           Label    => Loc.S(LabelKey);
    public ObservableCollection<CalcTaskVM> Tasks    { get; } = [];

    public CalcTasksSubGroupNode(string groupKey, string labelKey)
    {
        GroupKey = groupKey;
        LabelKey = labelKey;
    }

    /// <summary>Перечитывает подпись после смены языка.</summary>
    public void RefreshLabel() => PropertyChanged?.Invoke(this, new(nameof(Label)));
}

/// <summary>
/// Корневой узел «Задачи» — содержит 5 подгрупп по типу задачи:
/// НДС / 1-я ГПС / 2-я ГПС / Огнестойкость / Прочие.
/// По аналогии с <see cref="FemChecksRootNode"/>.
/// </summary>
class CalcTasksRootNode
{
    public CalcTasksSubGroupNode[] Groups { get; }

    readonly CalcTasksSubGroupNode _nds, _uls, _sls, _fire, _other;
    readonly AppViewModel          _app;

    public CalcTasksRootNode(AppViewModel app)
    {
        _app   = app;
        _nds   = new(CalcTaskGroups.Nds,   "CalcTaskGroupNds");
        _uls   = new(CalcTaskGroups.Uls,   "CalcTaskGroupUls");
        _sls   = new(CalcTaskGroups.Sls,   "CalcTaskGroupSls");
        _fire  = new(CalcTaskGroups.Fire,  "CalcTaskGroupFire");
        _other = new(CalcTaskGroups.Other, "CalcTaskGroupOther");
        Groups = [_nds, _uls, _sls, _fire, _other];

        Rebuild();

        app.CalcTasks.CollectionChanged   += (_, _) => Rebuild();
        app.CalcResults.CollectionChanged += (_, _) => RefreshResults();
    }

    /// <summary>Подписи подгрупп — на текущем языке.</summary>
    public void RefreshLanguage()
    {
        foreach (var g in Groups) g.RefreshLabel();
    }

    public void Rebuild()
    {
        foreach (var g in Groups) g.Tasks.Clear();
        foreach (var ct in _app.CalcTasks)
            SubGroupFor(CalcTaskGroups.Classify(ct.Kind)).Tasks.Add(BuildVM(ct));
    }

    CalcTaskVM BuildVM(CalcTask ct)
    {
        var vm  = new CalcTaskVM(ct);
        var sec = _app.CrossSections.FirstOrDefault(s => s.Id == ct.SectionId);
        var fs  = _app.BarForceSets.FirstOrDefault(f => f.Id == ct.ForceSetId);
        var fi  = fs?.Items.FirstOrDefault(i => i.Id == ct.ForceItemId);
        vm.SectionTag     = sec?.Tag  ?? "";
        vm.ForceSetTag    = fs?.Tag   ?? "";
        vm.ForceItemLabel = fi?.Label ?? "";
        foreach (var r in _app.CalcResults.Where(r => r.TaskId == ct.Id))
            vm.Results.Add(r);
        return vm;
    }

    void RefreshResults()
    {
        foreach (var g in Groups)
            foreach (var vm in g.Tasks)
            {
                var fresh = _app.CalcResults.Where(r => r.TaskId == vm.Model.Id).ToList();
                // Новый результат (пересчёт) — раскрыть задачу, иначе он не виден в свёрнутом узле.
                bool added = fresh.Any(r => !vm.Results.Contains(r));
                vm.Results.Clear();
                foreach (var r in fresh)
                    vm.Results.Add(r);
                if (added) vm.IsExpanded = true;
            }
    }

    CalcTasksSubGroupNode SubGroupFor(string key) => key switch
    {
        CalcTaskGroups.Nds  => _nds,
        CalcTaskGroups.Uls  => _uls,
        CalcTaskGroups.Sls  => _sls,
        CalcTaskGroups.Fire => _fire,
        _                   => _other
    };
}
