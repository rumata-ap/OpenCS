using System.Collections.ObjectModel;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Подгруппа расчётных задач по типу.</summary>
class CalcTasksSubGroupNode
{
    public string                           GroupKey { get; }
    public string                           Label    { get; }
    public ObservableCollection<CalcTaskVM> Tasks    { get; } = [];

    public CalcTasksSubGroupNode(string groupKey, string label)
    {
        GroupKey = groupKey;
        Label    = label;
    }
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
        _nds   = new("nds",   Loc.S("CalcTaskGroupNds"));
        _uls   = new("uls",   Loc.S("CalcTaskGroupUls"));
        _sls   = new("sls",   Loc.S("CalcTaskGroupSls"));
        _fire  = new("fire",  Loc.S("CalcTaskGroupFire"));
        _other = new("other", Loc.S("CalcTaskGroupOther"));
        Groups = [_nds, _uls, _sls, _fire, _other];

        Rebuild();

        app.CalcTasks.CollectionChanged   += (_, _) => Rebuild();
        app.CalcResults.CollectionChanged += (_, _) => RefreshResults();
    }

    public void Rebuild()
    {
        foreach (var g in Groups) g.Tasks.Clear();
        foreach (var ct in _app.CalcTasks)
            SubGroupFor(Classify(ct)).Tasks.Add(BuildVM(ct));
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
                vm.Results.Clear();
                foreach (var r in _app.CalcResults.Where(r => r.TaskId == vm.Model.Id))
                    vm.Results.Add(r);
            }
    }

    CalcTasksSubGroupNode SubGroupFor(string key) => key switch
    {
        "nds"  => _nds,
        "uls"  => _uls,
        "sls"  => _sls,
        "fire" => _fire,
        _      => _other
    };

    static string Classify(CalcTask t) => t.Kind switch
    {
        "strain_state"
            or "strain_state_batch"
            or "two_stage_strain"
            or "two_stage_strain_batch"
            or "shell_strain_state"
            or "shell_strain_state_batch"                             => "nds",
        "limit_force"          or "limit_force_batch"
            or "limit_moment"  or "limit_moment_batch"
            or "limit_axial"   or "limit_axial_batch"
            or "strength_ndm_batch"
            or "shell_simpl_wa_uls"    or "shell_simpl_wa_uls_batch"
            or "shell_simpl_capri_uls" or "shell_simpl_capri_uls_batch"
            or "steel_check"                                          => "uls",
        "shell_simpl_wa_sls"    or "shell_simpl_wa_sls_batch"
            or "shell_simpl_capri_sls" or "shell_simpl_capri_sls_batch" => "sls",
        _ when t.Kind.StartsWith("fire_", StringComparison.Ordinal)  => "fire",
        _                                                             => "other"
    };
}
